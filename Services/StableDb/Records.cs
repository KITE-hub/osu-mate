using System;

namespace OsuMate.Services.StableDb
{
    /// <summary>
    /// osu!.db から読み取った、1譜面(差分)ぶんのメタデータ。
    /// 「曲マスタ」に相当し、MD5 がプレイ記録との結合キーになる。
    /// </summary>
    public class BeatmapInfo
    {
        public string Md5Hash { get; set; } = "";
        public string Artist { get; set; } = "";
        public string Title { get; set; } = "";
        public string Creator { get; set; } = "";
        public string DifficultyName { get; set; } = "";

        /// <summary>
        /// 個別差分のID。.osu の [Metadata] でいう BeatmapID。
        /// osu!.db wiki 上では紛らわしく "DifficultyID" 相当のフィールドから取得する。
        /// </summary>
        public int DifficultyId { get; set; }

        /// <summary>
        /// 曲セット全体のID。.osu の [Metadata] でいう BeatmapSetID。
        /// osu!.db wiki 上では "Beatmap ID" と表記されているフィールドの実体がこちら。
        /// </summary>
        public int BeatmapSetId { get; set; }

        public float OverallDifficulty { get; set; }
        public float CircleSize { get; set; }
        public float ApproachRate { get; set; }
        public float HpDrain { get; set; }

        /// <summary>0=osu!std, 1=taiko, 2=ctb, 3=mania</summary>
        public byte Mode { get; set; }

        /// <summary>Songs フォルダ内のサブフォルダ名。</summary>
        public string FolderName { get; set; } = "";

        /// <summary>.osu ファイル名（フォルダ名なし）。</summary>
        public string OsuFileName { get; set; } = "";
    }

    /// <summary>
    /// scores.db の1スコアぶんの生データ。
    /// 「プレイ履歴」に相当し、Md5Hash で BeatmapInfo と結合する。
    /// </summary>
    public class ScoreRecord
    {
        public byte Mode { get; set; }
        public string Md5Hash { get; set; } = "";
        public string PlayerName { get; set; } = "";
        public string ReplayMd5 { get; set; } = "";

        public ushort Count300 { get; set; }
        public ushort Count100 { get; set; }
        public ushort Count50 { get; set; }
        public ushort CountGeki { get; set; } // MAX (300g)
        public ushort CountKatu { get; set; } // 200
        public ushort CountMiss { get; set; }

        public int TotalScore { get; set; }
        public ushort MaxCombo { get; set; }
        public bool IsPerfectCombo { get; set; }
        public int Mods { get; set; }

        /// <summary>Windows FILETIME ticks (UTC, 100ns単位)。生のまま保持し変換は出力時に行う。</summary>
        public long TimestampTicks { get; set; }

        public long OnlineScoreId { get; set; }

        public int TotalJudged =>
            Count300 + Count100 + Count50 + CountGeki + CountKatu + CountMiss;
    }
}
