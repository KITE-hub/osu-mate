using OsuMate.Models;
using OsuMate.Services;
using OsuMate.Services.Trainer;
using OsuMate.Utils;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Threading;

namespace OsuMate.ViewModels
{
    public class BatchPreviewItem
    {
        public string RateText { get; set; } = "";
        public string BpmText { get; set; } = "";
        public string ArText { get; set; } = "";
        public string OdText { get; set; } = "";
        public string HpText { get; set; } = "";
        public string CsText { get; set; } = "";
    }

    public class TrainerViewModel : ObservableBase, IDisposable
    {
        /// <summary>
        /// AR/OD/HP/CS に共通する「元譜面の値(Original) → スライダーの仮の値(Base) →
        /// （対応するパラメータのみ）Rateに応じたScale後の値」という状態と計算をまとめたヘルパー。
        /// AR/OD はコンストラクタに変換式を渡すことでRate変化時のScalingに対応させ、
        /// HP/CS（Scaling非対応）は既定値のまま渡さないことで、4パラメータ分の
        /// クランプ処理・「元の値がない場合は "-"」表示処理の重複を1箇所に集約する。
        /// 変更通知(OnPropertyChanged)はプロパティ名がAR/OD/HP/CSごとに異なるため、
        /// 呼び出し元（TrainerViewModel の各プロパティ）側で引き続き行う。
        /// </summary>
        private sealed class DifficultyParameter
        {
            private readonly Func<decimal, decimal, bool, bool, decimal>? _computeScaled;

            /// <summary>元譜面が持っていた値。譜面側が未設定（-1等）の場合は null。</summary>
            public decimal? Original { get; set; }
            public bool HasOriginal => Original.HasValue;

            private decimal _base;
            /// <summary>スライダーが示す仮の値（クランプ済み）。</summary>
            public decimal Base
            {
                get => _base;
                set => _base = TrainerCalculationService.ClampDifficulty(value);
            }

            public string BaseText => HasOriginal ? $"{Base:F1}" : "-";

            /// <summary>Scaling有効フラグ。Scaling非対応のパラメータ（HP/CS）では参照されない。</summary>
            public bool ScaleEnabled { get; set; } = true;

            public DifficultyParameter(Func<decimal, decimal, bool, bool, decimal>? computeScaled = null)
            {
                _computeScaled = computeScaled;
            }

            /// <summary>指定Rateにおけるスケール後の値。Scaling非対応のパラメータはBaseをそのまま返す。</summary>
            public decimal Scaled(decimal rate)
                => _computeScaled != null ? _computeScaled(Base, rate, ScaleEnabled, HasOriginal) : Base;
        }

        private readonly BeatmapTrainerService _trainerService;
        private readonly OsuMemoryService      _memory;
        private readonly Dispatcher            _dispatcher;

        private readonly System.Threading.Timer _pollTimer;

        private string _lastActualPath = "";
        private string _effectiveBeatmapPath = "";

        // ============================================================
        //  ゲームモード（taiko/mania では AR/CS がゲームプレイに影響しない）
        // ============================================================

        private int _mode = 0;

        private bool _isArCsEditable = true;
        /// <summary>
        /// AR/CS の編集可否。taiko(1)/mania(3) では AR は未使用、CS は無意味な値
        /// （mania ではキー数を表すため、変更すると譜面のキー数自体が変わってしまう）
        /// となるため false にし、スライダー・Scaleトグルを無効化する。
        /// </summary>
        public bool IsArCsEditable
        {
            get => _isArCsEditable;
            private set
            {
                if (_isArCsEditable == value) return;
                _isArCsEditable = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanGenerate));
            }
        }

        // AR/OD は Rate に応じたScalingに対応するため計算式を渡す。HP/CS は非対応（スライダーの値をそのまま使う）。
        private readonly DifficultyParameter _ar = new(TrainerCalculationService.ComputeApproachRate);
        private readonly DifficultyParameter _od = new(TrainerCalculationService.ComputeOverallDifficulty);
        private readonly DifficultyParameter _hp = new();
        private readonly DifficultyParameter _cs = new();

        // ============================================================
        //  BPM
        // ============================================================

        private decimal _originalBpm = 0M;
        private decimal _minBpm = 0M;
        private decimal _maxBpm = 0M;

        private string _originalBpm_ = "-";
        public string OriginalBpm
        {
            get => _originalBpm_;
            private set { _originalBpm_ = value; OnPropertyChanged(); }
        }

        private string _originalBpmRange = "";
        /// <summary>InfoViewModel.BpmRange と同様、min/maxを条件なしで " ( min - max )" 形式で返す。</summary>
        public string OriginalBpmRange
        {
            get => _originalBpmRange;
            private set { _originalBpmRange = value; OnPropertyChanged(); }
        }

        /// <summary>_originalBpm/_minBpm/_maxBpm から OriginalBpm(Range) を再計算する。
        /// InfoViewModel の Bpm/BpmRange 更新方法（都度セットしてOnPropertyChangedを発火）に合わせている。</summary>
        private void UpdateBpmTexts()
        {
            if (_originalBpm > 0)
            {
                OriginalBpm      = _originalBpm.ToString("F1", CultureInfo.InvariantCulture);
                OriginalBpmRange = $" ( {_minBpm:F0} - {_maxBpm:F0} )";
            }
            else
            {
                OriginalBpm      = "-";
                OriginalBpmRange = "";
            }
        }

        // ============================================================
        //  連続生成 (Batch)
        // ============================================================

        private decimal _batchStartRate = 1.05M;
        public decimal BatchStartRate
        {
            get => _batchStartRate;
            set { _batchStartRate = Math.Max(0.5M, Math.Min(2.0M, value)); OnPropertyChanged(); UpdateBatchPreviews(); }
        }

        private decimal _batchStep = 0.05M;
        public decimal BatchStep
        {
            get => _batchStep;
            set { _batchStep = Math.Max(0.01M, Math.Min(1.0M, value)); OnPropertyChanged(); UpdateBatchPreviews(); }
        }

        private int _batchCount = 4;
        public int BatchCount
        {
            get => _batchCount;
            set { _batchCount = Math.Max(1, Math.Min(20, value)); OnPropertyChanged(); UpdateBatchPreviews(); }
        }

        public ObservableCollection<BatchPreviewItem> BatchPreviews { get; } = new();

        private void UpdateBatchPreviews()
        {
            var request = new BatchPreviewRequest(
                StartRate: BatchStartRate,
                Step: BatchStep,
                Count: BatchCount,
                MaxRate: 2.0M,
                OriginalBpm: _originalBpm,
                MinBpm: _minBpm,
                MaxBpm: _maxBpm,
                ArBase: ArBase,
                // taiko/mania では AR がゲームプレイに影響しないため、Scale ARが（別譜面から
                // 切り替わった際などに）ON のまま残っていてもPreview計算では無視する。
                // 実際にはAR列自体が非表示になるため表には出ないが、値の整合性のため合わせておく。
                ScaleAr: ScaleAR && IsArCsEditable,
                HasOriginalAr: _ar.HasOriginal,
                OdBase: OdBase,
                ScaleOd: ScaleOD,
                HasOriginalOd: _od.HasOriginal,
                HpBase: HpBase,
                HasOriginalHp: _hp.HasOriginal,
                CsBase: CsBase,
                HasOriginalCs: _cs.HasOriginal);

            var previews = TrainerCalculationService.ComputeBatchPreviews(request);

            BatchPreviews.Clear();
            foreach (var p in previews)
            {
                BatchPreviews.Add(new BatchPreviewItem
                {
                    RateText = $"{p.Rate:0.00}x",
                    BpmText  = p.Bpm.HasValue
                        ? $"{p.Bpm:F1} ( {p.MinBpm:F0} - {p.MaxBpm:F0} )"
                        : "-",
                    ArText   = p.Ar.HasValue ? $"{p.Ar:F1}" : "-",
                    OdText   = p.Od.HasValue ? $"{p.Od:F1}" : "-",
                    HpText   = p.Hp.HasValue ? $"{p.Hp:F1}" : "-",
                    CsText   = p.Cs.HasValue ? $"{p.Cs:F1}" : "-"
                });
            }
        }

        // ============================================================
        //  譜面情報
        // ============================================================

        private string _beatmapTitle = "-";
        public string BeatmapTitle
        {
            get => _beatmapTitle;
            private set { _beatmapTitle = value; OnPropertyChanged(); }
        }

        private string _beatmapArtist = "";
        public string BeatmapArtist
        {
            get => _beatmapArtist;
            private set { _beatmapArtist = value; OnPropertyChanged(); }
        }

        private string _beatmapVersion = "";
        public string BeatmapVersion
        {
            get => _beatmapVersion;
            private set { _beatmapVersion = value; OnPropertyChanged(); }
        }

        private string _redirectedMessage = "";
        public string RedirectedMessage
        {
            get => _redirectedMessage;
            private set { _redirectedMessage = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsRedirected)); }
        }
        public bool IsRedirected => !string.IsNullOrEmpty(_redirectedMessage);

        private bool _isOriginalMapMissing = false;
        /// <summary>
        /// 選択中の譜面が osutrainer 生成済みで、かつ元譜面が特定できなかった場合に true。
        /// この状態のまま生成すると、既にRate適用済みの譜面へさらにRate/難易度を
        /// 二重適用してしまうため、CanGenerate でブロックする。
        /// </summary>
        public bool IsOriginalMapMissing
        {
            get => _isOriginalMapMissing;
            private set { _isOriginalMapMissing = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanGenerate)); }
        }

        // ============================================================
        //  AR — スライダー=仮の値、各RateごとのScale後の値はArScaledForで算出
        // ============================================================

        /// <summary>スライダーが示す仮の値（ユーザーが自由に設定）。</summary>
        public decimal ArBase
        {
            get => _ar.Base;
            set
            {
                _ar.Base = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ArBaseText));
                OnPropertyChanged(nameof(CanGenerate));
                UpdateBatchPreviews();
            }
        }
        public string  ArBaseText   => _ar.BaseText;

        /// <summary>
        /// 指定Rateにおける「実際に生成時に使うAR値」（Scale ON → 計算値、OFF → 仮の値）。
        /// taiko/mania（IsArCsEditable=false）では AR 自体がゲームプレイに影響しないため、
        /// Scale ARトグルが（別譜面から切り替わった際などに）ON のまま残っていても無視し、
        /// 常に Base 値（＝元譜面の AR）をそのまま返す。これにより、Scale AR を ON にした状態で
        /// std/catch譜面 → taiko/mania譜面 へ切り替えて生成しても、意図しない AR 差分が
        /// 難易度名に紛れ込むことがなくなる。GenerateAsync のバッチ生成ループから呼び出される。
        /// </summary>
        private decimal ArScaledFor(decimal rate) => IsArCsEditable ? _ar.Scaled(rate) : _ar.Base;

        public bool ScaleAR
        {
            get => _ar.ScaleEnabled;
            set { _ar.ScaleEnabled = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanGenerate)); UpdateBatchPreviews(); }
        }

        // ============================================================
        //  OD
        // ============================================================

        public decimal OdBase
        {
            get => _od.Base;
            set
            {
                _od.Base = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(OdBaseText));
                OnPropertyChanged(nameof(CanGenerate));
                UpdateBatchPreviews();
            }
        }
        public string  OdBaseText   => _od.BaseText;

        public bool ScaleOD
        {
            get => _od.ScaleEnabled;
            set { _od.ScaleEnabled = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanGenerate)); UpdateBatchPreviews(); }
        }

        public decimal HpBase
        {
            get => _hp.Base;
            set
            {
                _hp.Base = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HpBaseText));
                OnPropertyChanged(nameof(CanGenerate));
                UpdateBatchPreviews();
            }
        }
        public string  HpBaseText => _hp.BaseText;

        // ============================================================
        //  CS — スケーリングなし（HPと同様の理由）。
        // ============================================================

        public decimal CsBase
        {
            get => _cs.Base;
            set
            {
                _cs.Base = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CsBaseText));
                OnPropertyChanged(nameof(CanGenerate));
                UpdateBatchPreviews();
            }
        }
        public string  CsBaseText => _cs.BaseText;

        // ============================================================
        //  Options
        // ============================================================

        private bool _adjustPitchWithSpeed = false;
        public bool AdjustPitchWithSpeed
        {
            get => _adjustPitchWithSpeed;
            set
            {
                if (_adjustPitchWithSpeed == value) return;
                _adjustPitchWithSpeed = value;
                OnPropertyChanged();
                SaveAdjustPitchWithSpeed(value);
            }
        }

        /// <summary>Adjust Pitch with Speed の状態を Config.json の Global セクションへ保存する。</summary>
        private static void SaveAdjustPitchWithSpeed(bool value)
        {
            var root = ConfigUtils.LoadRootConfig();
            root.Global.AdjustPitchWithSpeed = value;
            ConfigUtils.SaveRootConfig(root);
        }

        // ============================================================
        //  生成状態
        // ============================================================

        private bool _isGenerating = false;
        public bool IsGenerating
        {
            get => _isGenerating;
            private set { _isGenerating = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanGenerate)); }
        }

        private string _statusMessage = "Please select a beatmap in osu!";
        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        private bool _isBeatmapLoaded = false;
        public bool IsBeatmapLoaded
        {
            get => _isBeatmapLoaded;
            private set { _isBeatmapLoaded = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanGenerate)); }
        }

        public bool CanGenerate
            => IsBeatmapLoaded
            && !IsGenerating
            && !IsOriginalMapMissing;

        // ============================================================
        //  コンストラクタ
        // ============================================================

        public TrainerViewModel(BeatmapTrainerService trainerService, OsuMemoryService memory, Dispatcher dispatcher)
        {
            _trainerService = trainerService;
            _memory         = memory;
            _dispatcher     = dispatcher;

            _adjustPitchWithSpeed = ConfigUtils.LoadGlobalConfig().AdjustPitchWithSpeed;

            _pollTimer = new System.Threading.Timer(PollBeatmap, null, 2000, 2000);
        }

        /// <summary>
        /// Trainerタブが非表示のとき呼び出す。ポーリングを一時停止し、
        /// 他のタブを見ている間の無駄なディスクI/Oを防ぐ。
        /// </summary>
        public void PausePolling()
            => _pollTimer.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);

        /// <summary>
        /// Trainerタブに戻ったとき呼び出す。即座に1回ポーリングし、以後2秒間隔で再開する。
        /// </summary>
        public void ResumePolling()
            => _pollTimer.Change(0, 2000);

        /// <summary>アプリ終了時にポーリングタイマーを破棄する。</summary>
        public void Dispose()
        {
            _pollTimer.Dispose();
            GC.SuppressFinalize(this);
        }

        // ============================================================
        //  ポーリング
        // ============================================================

        private void PollBeatmap(object? _)
        {
            try
            {
                string? path = _trainerService.GetCurrentBeatmapPath();
                if (path == null || path == _lastActualPath) return;
                _lastActualPath = path;

                OsuBeatmapFile bm;
                try
                {
                    bm = OsuBeatmapFile.Load(path);
                }
                catch (Exception ex)
                {
                    LogUtils.DebugLogger($"[Trainer] Failed to load beatmap: {ex.Message}", true);
                    return;
                }

                if (bm.IsOsuTrainerMap)
                {
                    string? origPath = BeatmapTrainerService.FindOriginalMap(path);
                    if (origPath != null)
                    {
                        OsuBeatmapFile origBm;
                        try
                        {
                            origBm = OsuBeatmapFile.Load(origPath);
                        }
                        catch (Exception ex)
                        {
                            LogUtils.DebugLogger($"[Trainer] Failed to load original beatmap: {ex.Message}", true);
                            return;
                        }
                        string msg = $"↩ Trainer map detected -> Switched to original map";
                        _effectiveBeatmapPath = origPath;
                        LoadBeatmapInfo(origBm, msg, originalMissing: false);
                        return;
                    }
                    // 元譜面が特定できない状態。このままGenerateすると既にRate適用済みの
                    // 譜面へさらにRate/難易度を二重適用してしまうため、
                    // originalMissing:true を渡してCanGenerateをブロックする。
                    _effectiveBeatmapPath = path;
                    LoadBeatmapInfo(bm,
                        "⚠ Trainer map detected (Original map not found) - Generation disabled",
                        originalMissing: true);
                    return;
                }

                _effectiveBeatmapPath = path;
                LoadBeatmapInfo(bm, "", originalMissing: false);
            }
            catch (Exception ex)
            {
                // Timerコールバックなので、想定外の例外が1つでも漏れるとプロセスごと落ちる。
                // 個別のtry/catchで対策していない箇所が将来増えても道連れにしないための最終防衛ライン。
                LogUtils.DebugLogger($"[Trainer] Unexpected exception in PollBeatmap: {ex.Message}", true);
            }
        }

        /// <summary>
        /// 既に読み込み済みの <see cref="OsuBeatmapFile"/> から画面表示用の状態を更新する。
        /// 呼び出し側（PollBeatmap）で既にパース済みのインスタンスを受け取ることで、
        /// 同じ .osu ファイルを二重にパースすることを避ける。
        /// </summary>
        private void LoadBeatmapInfo(OsuBeatmapFile bm, string redirectedMessage, bool originalMissing)
        {
            _dispatcher.BeginInvoke(() =>
            {
                BeatmapTitle   = bm.Title;
                BeatmapArtist  = bm.Artist;
                BeatmapVersion = bm.Version;
                _originalBpm   = bm.DominantBpm;
                _minBpm        = bm.MinBpm;
                _maxBpm        = bm.MaxBpm;

                _mode          = bm.Mode;
                IsArCsEditable = !bm.IsTaikoOrMania;

                _ar.Original = bm.ApproachRate      >= 0 ? bm.ApproachRate      : (decimal?)null;
                _od.Original = bm.OverallDifficulty >= 0 ? bm.OverallDifficulty : (decimal?)null;
                _hp.Original = bm.HPDrainRate       >= 0 ? bm.HPDrainRate       : (decimal?)null;
                _cs.Original = bm.CircleSize        >= 0 ? bm.CircleSize        : (decimal?)null;

                // スライダー（仮の値）を元の値で初期化
                if (_ar.HasOriginal) ArBase = _ar.Original!.Value;
                if (_od.HasOriginal) OdBase = _od.Original!.Value;
                if (_hp.HasOriginal) HpBase = _hp.Original!.Value;
                if (_cs.HasOriginal) CsBase = _cs.Original!.Value;

                RedirectedMessage    = redirectedMessage;
                IsOriginalMapMissing = originalMissing;
                IsBeatmapLoaded      = true;

                UpdateBpmTexts();

                StatusMessage = string.IsNullOrEmpty(redirectedMessage)
                    ? "Beatmap loaded"
                    : redirectedMessage;

                UpdateBatchPreviews();
            });
        }

        // ============================================================
        //  コマンド
        // ============================================================

        public async Task GenerateAsync()
        {
            if (string.IsNullOrEmpty(_effectiveBeatmapPath))
            {
                StatusMessage = "Please select a beatmap in osu!";
                return;
            }

            IsGenerating  = true;
            StatusMessage = "Generating...";
            try
            {
                var requests = new List<BatchGenerationRequest>();
                for (int i = 0; i < BatchCount; i++)
                {
                    decimal rate = BatchStartRate + (BatchStep * i);
                    if (rate > 2.0M) break;

                    requests.Add(new BatchGenerationRequest
                    {
                        Rate       = rate,
                        ArOverride = TrainerCalculationService.ResolveOverride(_ar.Original, ArScaledFor(rate)),
                        OdOverride = TrainerCalculationService.ResolveOverride(_od.Original, _od.Scaled(rate)),
                        HpOverride = TrainerCalculationService.ResolveOverride(_hp.Original, HpBase),
                        CsOverride = TrainerCalculationService.ResolveOverride(_cs.Original, CsBase)
                    });
                }

                await _trainerService.GenerateBeatmapsBatchAsync(
                    _effectiveBeatmapPath,
                    requests,
                    AdjustPitchWithSpeed,
                    msg => _dispatcher.BeginInvoke(() => StatusMessage = msg));
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsGenerating = false;
            }
        }
    }
}
