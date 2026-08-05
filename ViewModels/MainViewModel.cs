using osu.Game.Rulesets.Scoring;
using OsuMate.Models;
using OsuMate.Services;
using OsuMate.Services.Osu;
using OsuMate.Services.PlayLog;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace OsuMate.ViewModels
{
    public class MainViewModel
    {
        public ThemeViewModel Theme { get; } = new();
        public InfoViewModel Info { get; } = new();
        public StrainGraphViewModel StrainGraph { get; }
        public URTimeGraphViewModel URTimeGraph { get; }
        public URDistGraphViewModel URDistGraph { get; }
        public URBarViewModel URBar { get; } = new();
        public InGameOverlayViewModel InGameOverlay { get; } = new();

        public event Action<bool>? IsPlayingChanged;
        public event Action<IntPtr>? OnOsuWindowFound;

        private readonly OsuMemoryService _memory;
        public bool IsPlaying => _memory.IsPlaying;
        private readonly PpCalculationService _ppService;
        private readonly SettingsViewModel _settings;
        private readonly PlayLogService _playLogService;
        private List<int> _enabledOverlayIds = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13];
        private bool _previousIsPlaying = false;
        private bool _previousIsResultScreen = false;

        // Best pp: 現在譜面に対する自己ベストのキャッシュ。InGameOverlay.Update() へ毎tick渡すため保持する。
        // 実際の監視・再計算ロジックは BestPpTracker（ドメインサービス）に委譲している。
        private readonly BestPpTracker _bestPpTracker;

        // アプリ全体のシャットダウン協調用
        private readonly CancellationTokenSource _cts = new();
        public void SetOverlayFontSize(double fontSize)
        {
            InGameOverlay.FontSize = fontSize;
        }

        public void SetOverlayShowValueFirst(bool isShowValueFirst)
        {
            InGameOverlay.IsShowValueFirst = isShowValueFirst;
        }

        public void SetEnabledOverlayIds(List<int> ids)
        {
            _enabledOverlayIds = ids;
        }

        public MainViewModel(OsuMemoryService memory, PpCalculationService ppService, SettingsViewModel settings, PlayLogService playLogService)
        {
            _memory = memory;
            _ppService = ppService;
            _settings = settings;
            _playLogService = playLogService;
            _ppService.OnCalculated += UpdateUI;
            _memory.OnMemoryRead += UpdateFastUI;
            _memory.OnOsuWindowFound += handle => OnOsuWindowFound?.Invoke(handle);
            StrainGraph = new(Theme.Current);
            URTimeGraph = new(Theme.Current);
            URDistGraph = new(Theme.Current);
            _ppService.OnStrainDataUpdated += (data, strains, labels, speed) =>
            {
                StrainGraph.SetData(strains, labels, data.strainTimeModified, data.FirstObjectTimeModified, speed);
                URTimeGraph.SetData(
                    data.ModifiedHitWindows,
                    data.strainTimeModified / 1000.0);
            };

            // Best pp: 対象プレイヤー名リストの編集や、プレイ履歴の追加・完走確定・pp確定を
            // 即座に拾って再計算する（監視・計算の実処理はBestPpTrackerに委譲）。
            _bestPpTracker = new BestPpTracker(_playLogService, _settings.TargetPlayerNames);
            _bestPpTracker.BestPpChanged += OnBestPpChanged;
        }

        private void OnBestPpChanged(double? bestPp)
        {
            void Apply() => Info.UpdateBestPp(bestPp);

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
                Apply();
            else
                dispatcher.BeginInvoke(Apply);
        }

        private IEnumerable<IThemeable> Themeables => [StrainGraph, URTimeGraph, URDistGraph];

        public void OnThemeChanged()
        {
            foreach (var t in Themeables)
                t.ApplyTheme(Theme.Current);
        }

        public void Start(Dispatcher dispatcher)
        {
            _memory.StartProcessMonitor(_cts.Token);
            // Fast Lane(メモリ読み取り) / Slow Lane(pp・Strain計算)は、SettingsViewの
            // "Update Interval" (DataUpdateIntervalMs)を共有間隔として毎tick参照する。
            _memory.StartMemoryReader(() => _settings.DataUpdateIntervalMs, _cts.Token);
            _ppService.Start(dispatcher, () => _settings.DataUpdateIntervalMs, _cts.Token);
        }

        public void Stop()
        {
            _cts.Cancel();
        }

        private void UpdateUI(BeatmapData data, HitsResult hits)
        {
            if (_memory.IsPlaying != _previousIsPlaying)
            {
                _previousIsPlaying = _memory.IsPlaying;
                IsPlayingChanged?.Invoke(_memory.IsPlaying);
            }

            // 譜面変更を検知したらBest ppを再計算する（UpdateUI自体は既にUIスレッド上で実行される）
            _bestPpTracker.RefreshIfChanged(_ppService.CurrentBeatmapMd5);

            var baseAddresses = _memory.GetBaseAddressSnapshot();

            // Accuracyは baseAddresses.Player から直接読まず、torn read対策済みの
            // ReadHitsAndAccuracy() 経由で取得する（HitErrors同様、状態遷移直後などに
            // 一瞬だけ非現実的な値になることがあるため）。
            double accuracy = _memory.ReadHitsAndAccuracy().Accuracy;

            Info.UpdateMapInfo(data, _memory.IsPlaying, baseAddresses.GeneralData.AudioTime, _ppService.PrevMods);
            Info.UpdatePp(data, _memory.IsPlaying, _memory.IsResultScreen);
            Info.UpdateJudge(data, hits, _ppService.CurrentGamemode);
            StrainGraph.Update(baseAddresses.GeneralData.AudioTime);

            double currentTimeSec = (baseAddresses.GeneralData.AudioTime
                * RulesetHelper.GetSpeedMultiplier(_ppService.PrevMods)
                - data.FirstObjectTimeModified) / 1000.0;

            List<(double timeSec, double offsetMs)> urSnapshot = _memory.GetURTimelineSnapshot();

            URTimeGraph.Update(urSnapshot, currentTimeSec, _memory.IsPlaying);
            URDistGraph.Update(urSnapshot, data.ModifiedHitWindows, _memory.IsPlaying);

            InGameOverlay.Update(
                data,
                hits,
                accuracy,
                _ppService.CurrentGamemode,
                _enabledOverlayIds,
                baseAddresses.GeneralData.AudioTime,
                RulesetHelper.GetSpeedMultiplier(_ppService.PrevMods),
                _bestPpTracker.CachedBestPp);
        }

        // UpdateFastUI の末尾ブロック（WPFにバインドされたVMプロパティを更新する部分）を
        // Dispatcher.BeginInvoke でUIスレッドへディスパッチする際の多重発行防止フラグ。
        // 0 = 空き、1 = ディスパッチ中。DataUpdateIntervalMs（既定15ms）ごとに呼ばれる高頻度パスのため、
        // UIスレッドが詰まってもBeginInvokeが際限なく積み上がらないよう間引く。
        private int _fastUiDispatchPending;

        // GetHitErrorsModified() が保持する、HitErrors（Raw値）にspeedMultiplierを適用した
        // 実時間ms版のキャッシュ。HitErrorsは末尾に追記されるだけの前提で、新規分だけを
        // このリストへ追記していく（全件を毎tick作り直すことはしない）。
        // GetHitErrorsModified()の呼び出しはDispatcher.BeginInvoke内（UIスレッド）に限定しているため、
        // この3フィールドはUIスレッドからのみアクセスされる。
        private readonly List<int> _hitErrorsModifiedCache = [];
        private int _hitErrorsModifiedCacheCount;
        private double _hitErrorsModifiedCacheSpeedMultiplier;

        private void UpdateFastUI()
        {
            if (!_memory.IsPlaying && !_memory.IsResultScreen) return;

            List<int> hitErrors = _memory.ReadPlayerHitErrors();
            double speedMultiplier = RulesetHelper.GetSpeedMultiplier(_ppService.PrevMods);

            // URTimelineData / PreviousHitCount の管理は OsuMemoryService 側に委譲する。
            // ViewModel はここで結果（URTimelineData）を意識せず、後段の UpdateUI で読み出すだけにする。
            _memory.SyncURTimeline(hitErrors, speedMultiplier);

            bool enteredResultScreen = _memory.IsResultScreen && !_previousIsResultScreen;
            _previousIsResultScreen = _memory.IsResultScreen;

            // HitsResult / Accuracy は baseAddresses.Player から直接読まず、torn read対策済みの
            // ReadHitsAndAccuracy() 経由でまとめて取得する（リトライ等の状態遷移直後に判定数が
            // 一瞬だけ非現実的な値になることがあるため）。
            var (hits, accuracy) = _memory.ReadHitsAndAccuracy();

            int gamemode = _ppService.CurrentGamemode;

            // ここから先はWPFにバインドされたVMプロパティ（Judge/Acc/InGameOverlay）を更新するため、
            // 必ずUIスレッドで実行する。URBar.Update自体はスレッドセーフだが、
            // まとめてディスパッチしても実害はなく、コードの見通しを優先してブロックごと移動している。
            if (Interlocked.CompareExchange(ref _fastUiDispatchPending, 1, 0) != 0)
                return; // 前回のUI反映がまだ終わっていない → 今回分は間引く

            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    // URBarに渡す値だけは、CurrentHitWindows（TimingHelper.GetModifiedHitWindowsが
                    // speedMultiplier換算済みの「実時間(ms)」で返す）と単位を揃える必要がある。
                    // メモリから読むHitErrorsは譜面内部時間（DT/HT等のレート変更を受けない、素の値）なので、
                    // ここで同じspeedMultiplierを掛けてから渡す。掛け忘れると、DT/HT時にURBarの
                    // ドット位置・色分けの境界（judgement window）が実際のHitErrorsとズレる。
                    // HitErrorStats.Sync（下記）はRaw値+speedMultiplierを別々に受け取る契約なので、
                    // hitErrors（Raw）自体は変更せずそのまま使う。
                    // GetHitErrorsModified()はUIスレッド上のこのクロージャからのみ呼ばれるため、
                    // 内部キャッシュ（_hitErrorsModifiedCache）を単一スレッド専有で安全に更新できる。
                    var hitErrorsModifiedInt = GetHitErrorsModified(hitErrors, speedMultiplier);

                    // CurrentHitWindows は参照渡しなので安全に読める
                    URBar.Update(hitErrorsModifiedInt, _ppService.CurrentHitWindows, _memory.IsPlaying);
                    Info.UpdateHits(hits);

                    if (hitErrors.Count > 0)
                    {
                        // Slow Lane（PpCalculationService.Start）と同じHitErrorStatsAccumulatorを
                        // 共有して呼ぶ。差分件数だけの増分計算になり、かつSlow Lane側が同じtickで
                        // 既に取り込み済みならここはO(1)で返る。
                        var stats = _ppService.HitErrorStats.Sync(hitErrors, speedMultiplier);

                        Info.UpdateAccFast(accuracy, _memory.IsPlaying, stats.RawAvg, stats.ModifiedAvg, stats.ModifiedStdev, stats.RawUR, stats.ModifiedUR);
                        InGameOverlay.UpdateFast(hits, accuracy, stats.ModifiedAvg, stats.ModifiedStdev, stats.RawUR, stats.ModifiedUR, gamemode);
                    }
                    else
                    {
                        Info.UpdateAccFast(accuracy, _memory.IsPlaying, null, null, null, null, null);
                        InGameOverlay.UpdateFast(hits, accuracy, null, null, null, null, gamemode);
                    }
                }
                finally
                {
                    Interlocked.Exchange(ref _fastUiDispatchPending, 0);
                }
            });
        }

        // hitErrors（Raw値）にspeedMultiplierを適用した実時間ms版を _hitErrorsModifiedCache として返す。
        // hitErrorsは末尾に追記されるだけの前提（SyncURTimeline等と同じ）なので、前回からの新規分だけを
        // 変換してキャッシュへ追記し、全件を毎tick作り直すことはしない。
        // リトライ等でHitErrorsが巻き戻った場合、およびmod変更でspeedMultiplierが変わった場合は
        // キャッシュの単位・系譜が合わなくなるため作り直す。
        // 呼び出しはDispatcher.BeginInvoke内（UIスレッド）に限定すること。戻り値は内部キャッシュそのものの
        // 参照であり、防御的コピーを取っていないため、他スレッドから呼ぶとキャッシュの書き換えと
        // 読み取り側（URBarViewModel.Update内のnew List<int>(...)等）が競合し得る。
        private List<int> GetHitErrorsModified(List<int> hitErrors, double speedMultiplier)
        {
            bool discontinuous = hitErrors.Count < _hitErrorsModifiedCacheCount
                || speedMultiplier != _hitErrorsModifiedCacheSpeedMultiplier;

            if (discontinuous)
            {
                _hitErrorsModifiedCache.Clear();
                _hitErrorsModifiedCacheCount = 0;
                _hitErrorsModifiedCacheSpeedMultiplier = speedMultiplier;
            }

            for (int i = _hitErrorsModifiedCacheCount; i < hitErrors.Count; i++)
                _hitErrorsModifiedCache.Add((int)Math.Round(hitErrors[i] * speedMultiplier, MidpointRounding.AwayFromZero));

            _hitErrorsModifiedCacheCount = hitErrors.Count;
            return _hitErrorsModifiedCache;
        }
    }
}
