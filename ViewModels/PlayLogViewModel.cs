using OsuMate.Models;
using OsuMate.Services.Osu;
using OsuMate.Services.PlayLog;
using OsuMate.Utils;
using OsuMate.ViewModels;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;
using System.Windows.Threading;

namespace OsuMate.ViewModels
{
    /// <summary>
    /// Log タブ用 ViewModel。
    /// PlayLogService の ObservableCollection をラップし、UIへ公開する。
    /// </summary>
    public class PlayLogViewModel : ObservableBase
    {
        private readonly PlayLogService _service;
        private readonly SettingsViewModel _settings;
        private readonly Dispatcher _dispatcher;
        private readonly ContributionGraphViewModel _contributionGraphViewModel;
        private readonly ContributionChartViewModel _contributionChartViewModel;
        private readonly PlayStatsChartViewModel _playStatsChartViewModel;

        /// <summary>
        /// NotifyFilteredEntriesChanged() が直近に計算した絞り込み済みエントリのキャッシュ。
        /// ContributionGraphViewModel.CurrentMonth（ComboBoxでの月選択）だけが変わった場合、
        /// エントリ自体は変わっていないため、再フィルタせずこのキャッシュをそのまま
        /// PlayStatsChartViewModel.Recalculate に渡す。
        /// </summary>
        private List<PlayLogEntry> _lastFilteredEntries = [];

        /// <summary>
        /// コントリビューショングラフ用のViewModel。
        /// PlayLogView.xamlからContributionGraphViewのDataContextとしてバインドされる。
        /// </summary>
        public ContributionGraphViewModel ContributionGraphVM => _contributionGraphViewModel;

        /// <summary>
        /// 対象月における日ごとの合計打数推移グラフ用のViewModel。
        /// PlayLogView.xamlからContributionChartViewのDataContextとしてバインドされる。
        /// </summary>
        public ContributionChartViewModel ContributionChartVM => _contributionChartViewModel;

        /// <summary>
        /// 選択中月における日ごとのSR/pp/Acc推移グラフ用のViewModel。
        /// PlayLogView.xamlからPlayStatsChartViewのDataContextとしてバインドされる。
        /// </summary>
        public PlayStatsChartViewModel PlayStatsChartVM => _playStatsChartViewModel;

        /// <summary>表示用エントリリスト（逆時系列: 先頭が最新）。</summary>
        public ObservableCollection<PlayLogEntry> Entries => _service.Entries;

        private readonly ICollectionView _filteredEntries;
        public ICollectionView FilteredEntries => _filteredEntries;

        private LogModeCategory _selectedModeCategory = LogModeCategory.Standard;
        public LogModeCategory SelectedModeCategory
        {
            get => _selectedModeCategory;
            private set
            {
                if (_selectedModeCategory == value) return;
                _selectedModeCategory = value;
                _filteredEntries.Refresh();
                OnPropertyChanged();
                NotifyFilteredEntriesChanged();
            }
        }

        public int FilteredEntryCount => _filteredEntries.Cast<object>().Count();

        /// <summary>全カラム（有効・無効問わず現在の順序）。列生成に使用。</summary>
        public IReadOnlyList<LogColumnItem> AllColumns
            => _settings.LogColumnSettings.LogColumnItems.ToList();

        /// <summary>現在有効なカラムリスト（順序付き）。Settings変更で即時更新。</summary>
        public IReadOnlyList<LogColumnItem> ActiveColumns
            => _settings.LogColumnSettings.LogColumnItems.Where(c => c.IsEnabled).ToList();

        private bool _isLoading = false;
        public bool IsLoading
        {
            get => _isLoading;
            set { _isLoading = value; RaiseOnUiThread(nameof(IsLoading)); }
        }

        private string _statusText = "Loading logs...";
        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; RaiseOnUiThread(nameof(StatusText)); }
        }

        /// <summary>
        /// LoadAsync() は MainWindow 起動時に Task.Run(...) 経由でバックグラウンドスレッドから
        /// 呼び出される（かつ、その後のawait再開先もUIスレッドとは限らない）。
        /// IsLoading/StatusText は PlayLogView.xaml から直接バインドされているため、
        /// PropertyChanged を必ずUIスレッドで発火させる必要がある
        /// （そうしないと WPF の DependencyObject アクセスで InvalidOperationException になる）。
        /// </summary>
        private void RaiseOnUiThread(string propertyName)
        {
            if (_dispatcher.CheckAccess())
                OnPropertyChanged(propertyName);
            else
                _dispatcher.Invoke(() => OnPropertyChanged(propertyName));
        }

        public PlayLogViewModel(PlayLogService service, SettingsViewModel settings, Dispatcher dispatcher,
            ContributionGraphViewModel contributionGraphViewModel, ContributionChartViewModel contributionChartViewModel,
            PlayStatsChartViewModel playStatsChartViewModel)
        {
            _service = service;
            _settings = settings;
            _dispatcher = dispatcher;
            _contributionGraphViewModel = contributionGraphViewModel;
            _contributionChartViewModel = contributionChartViewModel;
            _playStatsChartViewModel = playStatsChartViewModel;
            _filteredEntries = CollectionViewSource.GetDefaultView(Entries);
            _filteredEntries.Filter = item => item is PlayLogEntry entry
                && entry.ModeCategory == SelectedModeCategory
                && PassesNonModeFilters(entry);
            Entries.CollectionChanged += (_, _) => NotifyFilteredEntriesChanged();

            // CollectionChanged は1回だけ購読（重複排除）
            _settings.LogColumnSettings.LogColumnItems.CollectionChanged += OnLogColumnItemsChanged;
            foreach (var item in _settings.LogColumnSettings.LogColumnItems)
                item.PropertyChanged += (_, _) => NotifyActiveColumnsChanged();

            // 対象プレイヤー名リストをSettings画面で編集した瞬間に、Log絞り込みを即時反映する
            _settings.TargetPlayerNames.CollectionChanged += (_, _) =>
            {
                _filteredEntries.Refresh();
                NotifyFilteredEntriesChanged();
            };

            // Show Aborted Plays をSettings画面で切り替えた瞬間に、Log絞り込みを即時反映する
            _settings.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(SettingsViewModel.ShowAbortedPlays)) return;
                _filteredEntries.Refresh();
                NotifyFilteredEntriesChanged();
            };

            // ContributionGraph の ComboBox（または前月・次月ボタン）で選択中の月が変わったら、
            // ContributionChart / PlayStatsChart 側もその月にスコープし直して再描画
            _contributionGraphViewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(ContributionGraphViewModel.CurrentMonth)) return;
                _contributionChartViewModel.Recalculate(_contributionGraphViewModel.DailyHits, _contributionGraphViewModel.CurrentMonth);
                _playStatsChartViewModel.Recalculate(_lastFilteredEntries, _contributionGraphViewModel.DailyStats, _contributionGraphViewModel.CurrentMonth);
            };
        }

        public void SelectModeCategory(LogModeCategory category)
            => SelectedModeCategory = category;

        /// <summary>
        /// モード条件を除いた絞り込み条件（対象プレイヤー名・中断プレイ表示設定）を満たすかどうか
        /// </summary>
        private bool PassesNonModeFilters(PlayLogEntry entry)
            => TargetPlayerFilter.Matches(entry.PlayerName, _settings.TargetPlayerNames)
                && (_settings.ShowAbortedPlays || entry.IsCompleted);

        private void OnLogColumnItemsChanged(object? sender,
            System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            // 新しく追加された項目にも購読
            if (e.NewItems != null)
                foreach (LogColumnItem newItem in e.NewItems)
                    newItem.PropertyChanged += (_, _) => NotifyActiveColumnsChanged();

            NotifyActiveColumnsChanged();
        }

        // デバウンス用: 短時間に連続発火してもまとめて1回だけ通知
        private System.Threading.CancellationTokenSource? _activeColumnsCts;

        private void NotifyActiveColumnsChanged()
        {
            _activeColumnsCts?.Cancel();
            _activeColumnsCts = new System.Threading.CancellationTokenSource();
            var token = _activeColumnsCts.Token;

            System.Windows.Application.Current?.Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                new Action(() =>
                {
                    if (!token.IsCancellationRequested)
                        OnPropertyChanged(nameof(ActiveColumns));
                }));
        }

        // デバウンス用: 初回ロード時は Entries に1件ずつ大量に追加されるため、
        // その都度 ContributionGraphViewModel.Recalculate() / PlayStatsChartViewModel.Recalculate()
        // を呼ぶと無駄に重くなるのを防ぐ
        private System.Threading.CancellationTokenSource? _statsRecalculationCts;

        /// <summary>
        /// FilteredEntries が変化しうるタイミング（モード切替・絞り込み条件変更・新規プレイ読み込み等）で呼び出す処理
        /// </summary>
        private void NotifyFilteredEntriesChanged()
        {
            OnPropertyChanged(nameof(FilteredEntryCount));

            _statsRecalculationCts?.Cancel();
            _statsRecalculationCts = new System.Threading.CancellationTokenSource();
            var token = _statsRecalculationCts.Token;

            System.Windows.Application.Current?.Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                new Action(() =>
                {
                    if (token.IsCancellationRequested) return;

                    // filteredEntries: ヒートマップの色分け・SR/pp/Acc集計用（現在選択中のモードで絞り込み済み）
                    // allEntriesIgnoringMode: ComboBoxの月選択範囲(AvailableMonths)算出用。
                    var filtered = _filteredEntries.Cast<PlayLogEntry>().ToList();
                    var allEntriesIgnoringMode = Entries.Where(PassesNonModeFilters).ToList();

                    // _contributionGraphViewModel.Recalculate(...) より前にキャッシュを更新
                    _lastFilteredEntries = filtered;
                    _contributionGraphViewModel.Recalculate(filtered, allEntriesIgnoringMode);

                    // ContributionChartViewModel / PlayStatsChartViewModel はいずれも
                    // _contributionGraphViewModel.Recalculate が内部で計算し終えた
                    // DailyHits（PlayLogAggregationService.AggregateDailyHits の結果）/
                    // DailyStats（PlayStatsAggregationService.AggregateDailyStats の結果）を
                    // そのまま受け取るだけで、自分自身では集計しない
                    // 必ず上の Recalculate 呼び出しの後に呼ぶこと
                    _contributionChartViewModel.Recalculate(_contributionGraphViewModel.DailyHits, _contributionGraphViewModel.CurrentMonth);
                    _playStatsChartViewModel.Recalculate(filtered, _contributionGraphViewModel.DailyStats, _contributionGraphViewModel.CurrentMonth);
                }));
        }

        /// <summary>
        /// Logタブが開かれたとき（またはアプリ起動時）に呼び出す。
        /// 既存JSONを読み込み、SR/pp 未計算分を計算する。
        /// </summary>
        public async Task LoadAsync()
        {
            if (IsLoading) return;
            IsLoading = true;
            StatusText = "Loading logs...";

            try
            {
                await _service.LoadAndCalculateAsync();
                StatusText = Entries.Count == 0
                    ? "There is no play history."
                    : $"Loaded {Entries.Count} play history entries.";
            }
            catch (Exception ex)
            {
                // LoadAndCalculateAsync 内部では既に例外を握りつぶしているが、
                // ここでも念のため捕捉しておく。捕まえ損ねると IsLoading が true のまま
                // 固まり、Logタブがずっとローディング表示から戻らなくなる。
                LogUtils.DebugLogger("PlayLogViewModel.LoadAsync failed: " + ex.Message, true);
                StatusText = "Failed to load.";
            }
            finally
            {
                IsLoading = false;
            }
        }
    }
}
