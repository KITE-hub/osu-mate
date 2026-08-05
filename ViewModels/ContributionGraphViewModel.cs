using OsuMate.Models;
using OsuMate.Services.PlayLog;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;

namespace OsuMate.ViewModels
{
    /// <summary>
    /// PlayLogView上部に表示する、月単位カレンダー形式のコントリビューショングラフ用ViewModel。
    /// 日付の比較バグなどを防ぐため、「月」や「日」はすべて <see cref="DateOnly"/> で統一して扱う。
    /// </summary>
    public class ContributionGraphViewModel : ObservableBase
    {
        /// <summary>プレイありの日に割り当てる色分けレベルの段階数（0=プレイなしは含まない）。</summary>
        private const int ActivityLevelCount = 4;

        private readonly PlayLogAggregationService _aggregationService;
        private readonly PlayStatsAggregationService _playStatsAggregationService;

        // Recalculate() が最後に受け取った全エントリのキャッシュ。
        // 月切り替え時に再集計を行うために保持する。
        private IReadOnlyDictionary<DateOnly, int> _cachedDailyHits = new Dictionary<DateOnly, int>();

        /// <summary>
        /// 直近の <see cref="Recalculate"/> で集計された日付ごとの合計打数。
        /// 重複集計を防ぐため、全期間の集計結果を他ViewModelへの参照元として公開する。
        /// </summary>
        public IReadOnlyDictionary<DateOnly, int> DailyHits => _cachedDailyHits;

        // 日付ごとのSR/pp/Acc統計。DailyHitsと同様、
        // Recalculate() が最後に受け取った全エントリから全期間分をキャッシュする。
        private IReadOnlyDictionary<DateOnly, DailyPlayStats> _cachedDailyStats = new Dictionary<DateOnly, DailyPlayStats>();

        /// <summary>
        /// 直近の <see cref="Recalculate"/> で集計された日付ごとのSR/pp/Acc統計。
        /// セルツールチップの表示に使うほか、重複集計を防ぐため、他ViewModelへの参照元として公開する。
        /// </summary>
        public IReadOnlyDictionary<DateOnly, DailyPlayStats> DailyStats => _cachedDailyStats;

        // 色分けレベルの基準となる、選択中モードの「全期間」における1日あたり最大打数。
        private int _cachedMaxHits = 1;

        // ===== CurrentMonth =====

        private DateOnly _currentMonth;

        /// <summary>
        /// 現在表示中の月（その月の1日を表す DateOnly）。
        /// 変更時は <see cref="Days"/> を再構築し、<see cref="SelectedMonth"/> も同期する。
        /// </summary>
        public DateOnly CurrentMonth
        {
            get => _currentMonth;
            private set
            {
                if (_currentMonth == value) return;
                _currentMonth = value;
                OnPropertyChanged();
                // SelectedMonth を同期（ComboBox の選択状態を CurrentMonth に追従させる）
                if (_selectedMonth != value)
                {
                    _selectedMonth = value;
                    OnPropertyChanged(nameof(SelectedMonth));
                }
                RebuildDays();
                UpdateNavigationState();
            }
        }

        // ===== AvailableMonths / SelectedMonth (ComboBox) =====

        /// <summary>
        /// ComboBox に表示する選択可能な月の一覧（各要素はその月の1日を表す DateOnly）。
        /// ログの最古月〜今月までを昇順で保持する。Recalculate() 時に更新される。
        /// </summary>
        public ObservableCollection<DateOnly> AvailableMonths { get; } = new();

        private DateOnly _selectedMonth;

        /// <summary>
        /// ComboBox で選択中の月。変更時に <see cref="CurrentMonth"/> へ反映する。
        /// </summary>
        public DateOnly SelectedMonth
        {
            get => _selectedMonth;
            set
            {
                if (_selectedMonth == value) return;
                _selectedMonth = value;
                OnPropertyChanged();
                // CurrentMonth を更新すると RebuildDays() が走る
                CurrentMonth = value;
            }
        }

        // ===== Navigation Commands =====

        private bool _canGoToPreviousMonth;

        /// <summary>
        /// 前月ボタンを表示してよいか（= 現在表示中の月がログの最古月ではないか）。
        /// </summary>
        public bool CanGoToPreviousMonth
        {
            get => _canGoToPreviousMonth;
            private set
            {
                if (_canGoToPreviousMonth == value) return;
                _canGoToPreviousMonth = value;
                OnPropertyChanged();
            }
        }

        private bool _canGoToNextMonth;

        /// <summary>次月ボタンを表示してよいか（= 現在表示中の月が今月ではないか）。</summary>
        public bool CanGoToNextMonth
        {
            get => _canGoToNextMonth;
            private set
            {
                if (_canGoToNextMonth == value) return;
                _canGoToNextMonth = value;
                OnPropertyChanged();
            }
        }

        /// <summary>前月に切り替えるコマンド。</summary>
        public ICommand PreviousMonthCommand { get; }

        /// <summary>翌月に切り替えるコマンド。翌月が今月より後にはならない。</summary>
        public ICommand NextMonthCommand { get; }

        // ===== Days =====

        /// <summary>
        /// グラフ描画用の全マス（プレースホルダーを含む）。
        /// </summary>
        public ObservableCollection<ContributionDay> Days { get; } = new();

        public ContributionGraphViewModel(PlayLogAggregationService aggregationService, PlayStatsAggregationService playStatsAggregationService)
        {
            _aggregationService = aggregationService;
            _playStatsAggregationService = playStatsAggregationService;
            _currentMonth = ThisRealMonth();
            // CurrentMonth プロパティのセッターを経由せず backing field へ直接代入しているため、
            // SelectedMonth との同期処理が走らない。ここで明示的に揃えておかないと
            // _selectedMonth が既定値(0001/01/01)のまま残り、AvailableMonths のどの項目とも
            // 一致しないため ComboBox の初期選択が空白表示になってしまう。
            _selectedMonth = _currentMonth;

            PreviousMonthCommand = new RelayCommand(
                _ => CurrentMonth = CurrentMonth.AddMonths(-1),
                _ => CanGoToPreviousMonth);
            NextMonthCommand = new RelayCommand(
                _ => CurrentMonth = CurrentMonth.AddMonths(1),
                _ => CanGoToNextMonth);

            UpdateNavigationState();
        }

        // ===== Public API =====

        /// <summary>
        /// プレイ履歴から日ごとの合計打数を再集計し、<see cref="Days"/> を更新する。
        /// フィルタ状態の変更時に呼び出される。
        /// </summary>
        /// <param name="filteredEntries">ヒートマップの色分けおよびセルツールチップのSR/pp/Acc集計に用いる、全フィルタ適用済みのエントリ。</param>
        /// <param name="allEntriesIgnoringMode">モード切替時の月リセットを防ぐため、モード条件のみ除外したエントリ。<see cref="AvailableMonths"/> の算出に用いる。</param>
        public void Recalculate(IEnumerable<PlayLogEntry> filteredEntries, IEnumerable<PlayLogEntry> allEntriesIgnoringMode)
        {
            // AggregateDailyHits / AggregateDailyStats の両方で列挙するため、遅延列挙のまま渡さず一度だけ具現化する
            var filteredList = filteredEntries as ICollection<PlayLogEntry> ?? filteredEntries.ToList();

            _cachedDailyHits = _aggregationService.AggregateDailyHits(filteredList);
            _cachedDailyStats = _playStatsAggregationService.AggregateDailyStats(filteredList).ToDictionary(s => s.Date);
            _cachedMaxHits = _cachedDailyHits.Count > 0 ? _cachedDailyHits.Values.Max() : 1;
            RebuildAvailableMonths(allEntriesIgnoringMode);
            RebuildDays();
        }

        // ===== Private =====

        /// <summary>「今月」の1日を表す DateOnly。範囲判定・上限判定で繰り返し使う。</summary>
        private static DateOnly ThisRealMonth()
        {
            var now = DateTime.Now;
            return new DateOnly(now.Year, now.Month, 1);
        }

        /// <summary>
        /// <see cref="CanGoToPreviousMonth"/> / <see cref="CanGoToNextMonth"/> を最新の状態に更新する。
        /// CurrentMonth が変わった時だけでなく、AvailableMonths の範囲自体が変わった時
        /// （モード切替で最古月が動いた場合など）にも呼び出す必要がある。
        /// </summary>
        private void UpdateNavigationState()
        {
            CanGoToPreviousMonth = AvailableMonths.Count > 0 && CurrentMonth > AvailableMonths[0];
            CanGoToNextMonth = CurrentMonth < ThisRealMonth();
        }

        /// <summary>
        /// ログの最古月〜今月までの月一覧を構築する。
        /// AvailableMonths を更新し、現在の CurrentMonth が範囲外なら今月に戻す。
        /// 範囲の算出には <paramref name="allEntriesIgnoringMode"/>（モード非依存の全件）を使う。
        /// モード絞り込み後の <see cref="_cachedDailyHits"/> から算出すると、モードごとに
        /// 最古プレイ日が異なるせいで選べる月の範囲自体がモード切替のたびに変わってしまい、
        /// 選択中の月が範囲外になって今月へ強制リセットされる不具合につながる。
        /// </summary>
        private void RebuildAvailableMonths(IEnumerable<PlayLogEntry> allEntriesIgnoringMode)
        {
            var thisMonth = ThisRealMonth();

            // Count/Min で2回列挙しないよう、必要なら一度だけ具現化する
            var entriesList = allEntriesIgnoringMode as ICollection<PlayLogEntry> ?? allEntriesIgnoringMode.ToList();

            DateOnly earliest;
            if (entriesList.Count == 0)
            {
                earliest = thisMonth;
            }
            else
            {
                var minDate = entriesList.Min(e => e.PlayedAt);
                earliest = new DateOnly(minDate.Year, minDate.Month, 1);
            }

            AvailableMonths.Clear();
            var m = earliest;
            while (m <= thisMonth)
            {
                AvailableMonths.Add(m);
                m = m.AddMonths(1);
            }

            // CurrentMonth が範囲外（ログ読み込み前の初期値など）なら今月にリセット
            if (!AvailableMonths.Contains(_currentMonth))
            {
                _currentMonth = thisMonth;
                _selectedMonth = thisMonth;
                OnPropertyChanged(nameof(CurrentMonth));
            }

            // AvailableMonths.Clear() によって ComboBox の SelectedItem が内部でリセットされるため、
            // 範囲内・範囲外どちらのケースでも必ず通知して ComboBox を再同期させる。
            OnPropertyChanged(nameof(SelectedMonth));

            // モード切替等で最古月自体が動いた場合、CurrentMonth が変わらなくても
            // 「前月ボタンを表示してよいか」の判定結果は変わりうるため、ここでも更新する
            // （CurrentMonth の setter 経由の更新だけでは、上の直接代入ケースを含め拾いきれない）。
            UpdateNavigationState();
        }

        /// <summary>
        /// <see cref="_cachedDailyHits"/> と <see cref="CurrentMonth"/> をもとに
        /// <see cref="Days"/> を再構築する。
        /// 月切り替え時にもエントリ再集計なしで呼び出せるよう、集計とレイアウト構築を分離している。
        /// </summary>
        private void RebuildDays()
        {
            Days.Clear();

            var today = DateOnly.FromDateTime(DateTime.Now);
            var targetYear = _currentMonth.Year;
            var targetMonth = _currentMonth.Month;

            // 対象月の日数と、月の1日が何曜日かを取得する
            var daysInMonth = DateTime.DaysInMonth(targetYear, targetMonth);
            var firstDayOfMonth = new DateOnly(targetYear, targetMonth, 1);
            // DayOfWeek は 0=Sun, 1=Mon, ..., 6=Sat（日曜始まりオフセットとして直接使える）
            var startOffset = (int)firstDayOfMonth.DayOfWeek;

            // 先頭の空白プレースホルダーを埋める
            for (int i = 0; i < startOffset; i++)
                Days.Add(ContributionDay.Placeholder);

            // 対象月の各日のセルを追加する（色分け基準は _cachedMaxHits＝選択中モードの全期間の最大打数）
            for (int day = 1; day <= daysInMonth; day++)
            {
                var date = new DateOnly(targetYear, targetMonth, day);

                // 当月表示時、今日より未来のマスは作成しない
                if (date > today) break;

                var hits = _cachedDailyHits.GetValueOrDefault(date);
                var level = CalculateLevel(hits, _cachedMaxHits);
                var isToday = date == today;
                _cachedDailyStats.TryGetValue(date, out var dailyStats);
                Days.Add(new ContributionDay(
                    date, hits, level, isToday,
                    dailyStats?.StarRating ?? MetricStat.Empty,
                    dailyStats?.Pp ?? MetricStat.Empty,
                    dailyStats?.Accuracy ?? MetricStat.Empty));
            }
        }

        /// <summary>
        /// 打数を、選択中モードの全期間における最大値（<see cref="_cachedMaxHits"/>）に対する比率で
        /// 0〜<see cref="ActivityLevelCount"/> の段階に振り分ける。
        /// </summary>
        private static int CalculateLevel(int hits, int maxHits)
        {
            if (hits <= 0 || maxHits <= 0) return 0;

            var ratio = (double)hits / maxHits;
            var level = (int)Math.Ceiling(ratio * ActivityLevelCount);
            return Math.Clamp(level, 1, ActivityLevelCount);
        }

        // ===== Inner class =====

        /// <summary>
        /// 月ナビゲーション用の軽量 ICommand 実装。
        /// プロジェクト全体で RelayCommand が未使用のため、ここにスコープを絞って定義する。
        /// </summary>
        private sealed class RelayCommand : ICommand
        {
            private readonly Action<object?> _execute;
            private readonly Func<object?, bool>? _canExecute;

            public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
            {
                _execute = execute;
                _canExecute = canExecute;
            }

            public event EventHandler? CanExecuteChanged
            {
                add => CommandManager.RequerySuggested += value;
                remove => CommandManager.RequerySuggested -= value;
            }

            public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
            public void Execute(object? parameter) => _execute(parameter);
        }
    }
}
