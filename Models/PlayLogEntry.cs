using System;

namespace OsuMate.Models
{
    /// <summary>
    /// プレイ履歴 1 件。メモリ読み取りベースで記録する。
    /// scores.db には残らない中断(リタイア)プレイも含む。
    /// SR / pp は後から <see cref="PlayLogService"/> が計算して埋める。
    /// </summary>
    public class PlayLogEntry : ObservableBase
    {
        // ── 基本識別子 ────────────────────────────────────────────────
        public DateTime PlayedAt { get; set; }

        /// <summary>重複排除キー。BeatmapMd5 の代わりに使う一意識別子。</summary>
        public string DedupeKey { get; set; } = "";

        // ── 譜面情報 ──────────────────────────────────────────────────
        public int BeatmapId { get; set; }
        public int BeatmapSetId { get; set; }
        public string Artist { get; set; } = "";
        public string Title { get; set; } = "";
        public string DifficultyName { get; set; } = "";
        public string Creator { get; set; } = "";

        /// <summary>.osu ファイルの MD5 ハッシュ。SR/pp 再計算時に使用。</summary>
        public string BeatmapMd5 { get; set; } = "";

        // ── プレイヤー情報 ────────────────────────────────────────────
        public string PlayerName { get; set; } = "";

        /// <summary>0=osu!std, 1=taiko, 2=ctb, 3=mania</summary>
        public int Mode { get; set; }

        /// <summary>mania のキー数。譜面の CircleSize から取得する。mania 以外は null。</summary>
        public int? ManiaKeyCount { get; set; }

        /// <summary>Log 画面用の6分類。保存済みの Mode / ManiaKeyCount から常に算出する。</summary>
        public LogModeCategory ModeCategory => LogModeClassifier.Classify(Mode, ManiaKeyCount);

        // ── 判定内訳 ──────────────────────────────────────────────────
        private int _count300;
        public int Count300 { get => _count300; set => SetField(ref _count300, value); }

        private int _count100;
        public int Count100 { get => _count100; set => SetField(ref _count100, value); }

        private int _count50;
        public int Count50 { get => _count50; set => SetField(ref _count50, value); }

        private int _countGeki;
        public int CountGeki { get => _countGeki; set => SetField(ref _countGeki, value); }

        private int _countKatu;
        public int CountKatu { get => _countKatu; set => SetField(ref _countKatu, value); }

        private int _countMiss;
        public int CountMiss { get => _countMiss; set => SetField(ref _countMiss, value); }

        private int _maxCombo;
        public int MaxCombo { get => _maxCombo; set => SetField(ref _maxCombo, value); }

        private int _totalScore;
        public int TotalScore { get => _totalScore; set => SetField(ref _totalScore, value); }

        private double _accuracy;
        /// <summary>精度 (0.00 〜 100.00% などの形式、または 0.0 〜 1.0)</summary>
        public double Accuracy { get => _accuracy; set => SetField(ref _accuracy, value); }

        // ── 難易度情報 ────────────────────────────────────────────────
        /// <summary>Overall Difficulty (モッド適用後)。</summary>
        public double OverallDifficulty { get; set; }

        /// <summary>使用 Mod を人間可読な文字列で保存 (例: "HD,DT" / "NM")。</summary>
        public string ModsString { get; set; } = "NM";

        /// <summary>使用 Mod の生ビットマスク値。scores.db との突き合わせキーに使う(表示には使わない)。</summary>
        public int ModsRaw { get; set; }

        // ── ステータス ────────────────────────────────────────────────
        private bool _isCompleted;
        /// <summary>true = リザルト画面まで完了, false = 途中中断。</summary>
        public bool IsCompleted { get => _isCompleted; set => SetField(ref _isCompleted, value); }

        private bool _isProvisional;
        /// <summary>
        /// true = メモリ読み取り由来でまだ scores.db と突き合わせ未確定(中断プレイは常に true のまま)。
        /// false = scores.db 由来、または既に scores.db と突き合わせて確定済み。
        /// </summary>
        public bool IsProvisional { get => _isProvisional; set => SetField(ref _isProvisional, value); }

        // ── SR / pp (後から計算) ──────────────────────────────────────
        private double? _starRating;
        /// <summary>Star Rating。null = 未計算。</summary>
        public double? StarRating { get => _starRating; set => SetField(ref _starRating, value); }

        private double? _pp;
        /// <summary>Performance Points。null = 未計算。</summary>
        public double? Pp { get => _pp; set => SetField(ref _pp, value); }

        private bool _isCalculationFailed;
        /// <summary>true = 譜面が見つからない等の理由で計算に失敗したため、以降の自動再計算をスキップする。</summary>
        public bool IsCalculationFailed { get => _isCalculationFailed; set => SetField(ref _isCalculationFailed, value); }
    }
}
