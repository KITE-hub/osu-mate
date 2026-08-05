using osu.Game.Rulesets.Scoring;
using OsuMemoryDataProvider;
using OsuMate.Models;
using OsuMate.PPCalculation;
using OsuMate.Services.Osu;
using OsuMate.Services.PlayLog;
using OsuMate.Utils;
using System.IO;
using System.Windows.Threading;

namespace OsuMate.Services
{
    public class PpCalculationService(OsuMemoryService memory)
    {
        private PpCalculator? _calculator;
        private string _preMapPath = string.Empty;
        private int _currentBeatmapGamemode;
        private int _currentGamemode;
        private int _preOsuGamemode;
        private string[] _prevStrainMods = [];
        private HitsResult _previousHits = new();

        internal string[] PrevMods { get; private set; } = [];
        internal int CurrentGamemode { get; private set; }
        internal Dictionary<HitResult, double> CurrentHitWindows { get; private set; } = [];
        internal BeatmapData? LastCalculatedData { get; private set; }

        /// <summary>現在選択中の譜面（この特定の難易度ファイル）のMD5。Best pp検索のキーとして使用する。</summary>
        internal string CurrentBeatmapMd5 { get; private set; } = string.Empty;
        internal event Action<BeatmapData, HitsResult>? OnCalculated;
        internal event Action<BeatmapData, List<float[]>, string[], double>? OnStrainDataUpdated;

        /// <summary>
        /// offset平均・URの統計量アキュムレータ。Slow Lane（このクラスのStart()ループ）と
        /// Fast Lane（MainViewModel.UpdateFastUI）の両方から同じインスタンスを共有して呼び出す。
        /// これによりHitErrors全体の再走査を避け（Welfordのオンライン更新）、
        /// かつ2箇所で独立していた同じ計算を1本にまとめている。
        /// </summary>
        internal readonly HitErrorStatsAccumulator HitErrorStats = new();

        private List<int> _lastSafeHitErrors = [];

        private List<int> GetSafeHitErrors()
        {
            // HitErrorsの読み取り・ロック・コピーは OsuMemoryService.TryCopyPlayerHitErrors() に
            // 委譲する（PlayerLockはOsuMemoryService内に隠蔽されており、ここからは触れない）。
            var (sourceWasNull, copy) = memory.TryCopyPlayerHitErrors();
            if (sourceWasNull) return [];
            if (copy != null)
            {
                // コピーに成功した場合のみ更新する。
                // 稀な競合（InvalidOperationException/ArgumentException）でcopyがnullの場合は
                // 直前の安全なスナップショットを返してクラッシュを防ぐ。
                _lastSafeHitErrors = copy;
            }
            return _lastSafeHitErrors;
        }

        /// <summary>
        /// ループ間隔(ms)を毎tick取得するためのデリゲート。
        /// GlobalConfig.DataUpdateIntervalMs（SettingsViewから変更可能。Fast Laneの
        /// OsuMemoryService.StartMemoryReaderと共有）を参照する想定。
        /// 呼び出し元が省略した場合は既定の15msで動作する。
        /// </summary>
        internal void Start(Dispatcher dispatcher, Func<int>? intervalMsProvider = null, CancellationToken ct = default)
        {
            var getIntervalMs = intervalMsProvider ?? (() => 15);

            new Thread(() =>
            {
                while (!ct.IsCancellationRequested)
                {
                    if (dispatcher.HasShutdownStarted) break;
                    try
                    {
                        Thread.Sleep(Math.Max(1, getIntervalMs()));
                        ProcessTick(dispatcher, ct);
                    }
                    catch (TaskCanceledException) { }
                    catch (Exception e) { LogUtils.DebugLogger(e.Message, true); }
                }
            })
            { IsBackground = true }.Start();
        }

        /// <summary>
        /// ループ1tick分の処理本体。マップ変更・ゲームモード変更・モッド変更の検知、
        /// PP計算、UI通知までを順番に行う。各検知/計算ロジックは専用メソッドに分離してある。
        /// 元の while ループ内の `continue` は、対応するメソッドからの早期 return に置き換えている
        /// （呼び出し元の while ループは1tickにつき1回だけ本メソッドを呼ぶため、動作は変わらない）。
        /// </summary>
        private void ProcessTick(Dispatcher dispatcher, CancellationToken ct)
        {
            if (!memory.IsOsuRunning || !memory.IsDirectoryLoaded) return;

            string beatmapPath = ResolveBeatmapPath();
            if (!File.Exists(beatmapPath)) return;

            PrevMods = ResolveCurrentMods();

            bool strainUpdated = false;
            List<float[]> strains = [];
            string[] skillNames = [];

            // --- マップ変更検知 ---
            if (!DetectMapChange(beatmapPath, ref strainUpdated, ref strains, ref skillNames)) return;

            // --- ゲームモード変更検知 ---
            DetectGamemodeChange(ref strainUpdated, ref strains, ref skillNames);

            // --- モッド変更検知（DT/HT等でStrain横軸が変わる） ---
            DetectModChange(ref strainUpdated, ref strains, ref skillNames);

            if (_calculator == null) return;

            // --- PP計算 ---
            var calculated = CalculatePp();
            if (calculated == null) return;

            // --- UI通知（1回にまとめる） ---
            NotifyUi(dispatcher, ct, calculated.Value.Data, calculated.Value.Hits, strainUpdated, strains, skillNames, calculated.Value.SpeedMultiplier);
        }

        private string ResolveBeatmapPath()
        {
            var beatmap = memory.GetBaseAddressSnapshot().Beatmap;
            return Path.Combine(
                memory.SongsPath,
                beatmap.FolderName?.Trim() ?? "",
                beatmap.OsuFileName?.Trim() ?? "");
        }

        private string[] ResolveCurrentMods()
        {
            var baseAddresses = memory.GetBaseAddressSnapshot();
            return memory.CurrentStatus switch
            {
                OsuMemoryStatus.Playing => OsuUtils.ParseMods(baseAddresses.Player.Mods.Value).Calculation,
                OsuMemoryStatus.ResultsScreen => OsuUtils.ParseMods(baseAddresses.ResultsScreen.Mods.Value).Calculation,
                _ => OsuUtils.ParseMods(baseAddresses.GeneralData.Mods).Calculation
            };
        }

        /// <summary>
        /// マップパスの変化を検知し、変化していれば PpCalculator を新規作成/再セットアップして
        /// Strainデータを再取得する。
        /// </summary>
        /// <returns>ゲームモードが不正でこのtickを中断すべき場合は false。それ以外は true。</returns>
        private bool DetectMapChange(string beatmapPath, ref bool strainUpdated, ref List<float[]> strains, ref string[] skillNames)
        {
            if (_preMapPath == beatmapPath) return true;

            _preMapPath = beatmapPath;
            LogUtils.DebugLogger($"Map changed: {beatmapPath}");

            int gamemode = OsuUtils.GetMapMode(beatmapPath);
            if (gamemode is -1 or not (0 or 1 or 2 or 3)) return false;

            _currentBeatmapGamemode = gamemode;
            _currentGamemode = _currentBeatmapGamemode == 0 ? memory.CurrentOsuGamemode : _currentBeatmapGamemode;
            CurrentGamemode = _currentGamemode;
            CurrentBeatmapMd5 = BeatmapPathResolver.ComputeMd5(beatmapPath);

            if (_calculator == null) _calculator = new PpCalculator(beatmapPath, _currentGamemode);
            else _calculator.SetMap(beatmapPath, _currentGamemode);

            RefreshStrainData(ref strainUpdated, ref strains, ref skillNames);
            return true;
        }

        /// <summary>
        /// osu!本体側のゲームモード切り替えを検知する。マップ自体がConvert対象（_currentBeatmapGamemode == 0）の
        /// 場合のみ、切り替え後のモードでStrainを再計算する。
        /// </summary>
        private void DetectGamemodeChange(ref bool strainUpdated, ref List<float[]> strains, ref string[] skillNames)
        {
            if (memory.CurrentOsuGamemode == _preOsuGamemode) return;

            if (_calculator != null && _currentBeatmapGamemode == 0)
            {
                _calculator.SetMode(memory.CurrentOsuGamemode);
                _currentGamemode = memory.CurrentOsuGamemode;
                CurrentGamemode = _currentGamemode;
                RefreshStrainData(ref strainUpdated, ref strains, ref skillNames);
            }
            _preOsuGamemode = memory.CurrentOsuGamemode;
        }

        /// <summary>
        /// DT/HT等、Strainの横軸（時間）に影響するモッドの変更を検知する。
        /// マップ変更・ゲームモード変更で既にStrainを更新済みの場合はスキップする。
        /// </summary>
        private void DetectModChange(ref bool strainUpdated, ref List<float[]> strains, ref string[] skillNames)
        {
            if (strainUpdated || _calculator == null || PrevMods.SequenceEqual(_prevStrainMods)) return;
            RefreshStrainData(ref strainUpdated, ref strains, ref skillNames);
        }

        /// <summary>
        /// マップ変更/ゲームモード変更/モッド変更の3箇所で共通して行っていた
        /// 「Strainリスト再取得 + strainUpdatedフラグ更新 + _prevStrainMods更新」の処理をまとめた。
        /// </summary>
        private void RefreshStrainData(ref bool strainUpdated, ref List<float[]> strains, ref string[] skillNames)
        {
            var strainsData = _calculator!.GetStrainLists(PrevMods);
            strains = strainsData.Strains;
            skillNames = strainsData.SkillNames;
            strainUpdated = true;
            _prevStrainMods = PrevMods;
        }

        /// <summary>
        /// 現在のヒット状況からPPを計算する。hitsが前回tickと変化していない場合（プレイ中のみ）はnullを返し、
        /// 呼び出し側でこのtickをスキップさせる。
        /// </summary>
        private (BeatmapData Data, HitsResult Hits, double SpeedMultiplier)? CalculatePp()
        {
            var (hits, accuracy) = memory.ReadHitsAndAccuracy();

            // hitsが変わっていなければスキップ（プレイ中のみ）。
            if (hits.Equals(_previousHits) && memory.IsPlaying && !hits.IsEmpty()) return null;
            if (memory.IsPlaying) _previousHits = hits.Clone();

            var args = new CalculateArgs
            {
                Mods = PrevMods,
                Time = memory.GetBaseAddressSnapshot().GeneralData.AudioTime,
                Combo = hits.Combo,
                Score = hits.Score,
                Accuracy = accuracy,
                HitErrors = GetSafeHitErrors()
            };

            var data = _calculator!.Calculate(args, memory.IsPlaying, memory.IsResultScreen, hits);
            memory.FirstObjectTimeModified = data.FirstObjectTimeModified;
            CurrentHitWindows = data.ModifiedHitWindows;

            double speedMultiplier = RulesetHelper.GetSpeedMultiplier(PrevMods);

            // offset平均・URはSlow Lane / Fast Lane共有のアキュムレータで増分計算し、
            // Calculate()の戻り値に上書きする（PpCalculator側ではもう計算しない）
            var hitErrorStats = HitErrorStats.Sync(args.HitErrors ?? [], speedMultiplier);
            data.DetailedOffset = (hitErrorStats.RawAvg, hitErrorStats.ModifiedAvg, hitErrorStats.ModifiedStdev);
            data.UR = (hitErrorStats.RawUR, hitErrorStats.ModifiedUR);

            LastCalculatedData = data;

            return (data, hits, speedMultiplier);
        }

        /// <summary>
        /// 計算結果をUIスレッドへ1回のBeginInvokeにまとめて通知する。
        /// </summary>
        private void NotifyUi(Dispatcher dispatcher, CancellationToken ct, BeatmapData data, HitsResult hits, bool strainUpdated, List<float[]> strains, string[] skillNames, double speedMultiplier)
        {
            if (dispatcher.HasShutdownStarted || ct.IsCancellationRequested) return;

            dispatcher.BeginInvoke(() =>
            {
                OnCalculated?.Invoke(data, hits);
                if (strainUpdated) OnStrainDataUpdated?.Invoke(data, strains, skillNames, speedMultiplier);
            });
        }
    }
}
