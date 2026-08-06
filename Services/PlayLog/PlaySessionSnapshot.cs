using System;

namespace OsuMate.Services.PlayLog
{
    /// <summary>プレイ開始時のメモリスナップショット（プレイ中は更新されない）。</summary>
    public sealed class PlaySessionSnapshot
    {
        public DateTime StartedAt { get; set; }
        public int BeatmapId { get; set; }
        public int BeatmapSetId { get; set; }
        public string Artist { get; set; } = "";
        public string Title { get; set; } = "";
        public string DifficultyName { get; set; } = "";
        public string Creator { get; set; } = "";
        public string FolderName { get; set; } = "";
        public string OsuFileName { get; set; } = "";

        /// <summary>.osu ファイルの MD5。scores.db との突き合わせキーに使う。</summary>
        public string BeatmapMd5 { get; set; } = "";

        public string PlayerName { get; set; } = "";
        public int Mode { get; set; }
        public int? ManiaKeyCount { get; set; }
        public string[] Mods { get; set; } = [];

        /// <summary>使用 Mod の生ビットマスク値。scores.db との突き合わせキーに使う。</summary>
        public int ModsRaw { get; set; }

        public double OverallDifficulty { get; set; }

        /// <summary>セッション開始時点の GeneralData.Retries。クイックリトライ検知の基準値。</summary>
        public int StartRetries { get; set; }

        /// <summary>
        /// isCompleted=true で CommitSession した際に発行した結合キーを一時保持する。
        /// ResultsScreen 離脱時の UpdateLastEntryAsCompleted で、同じエントリを取り違えなく探し当てるために使う。
        /// </summary>
        public string? PendingCompletedKey { get; set; }

        // 以下、Playing 中に HandleMemoryTick が毎tick更新する「最後に読めた正常値」。
        // 中断コミット時に Player を直接読むと、状態遷移直後で値が使い回されて壊れている
        // ことがある(例: 判定数が数万になる)ため、これらのキャッシュ値を代わりに使う。
        public int LastHit300 { get; set; }
        public int LastHit100 { get; set; }
        public int LastHit50 { get; set; }
        public int LastHitGeki { get; set; }
        public int LastHitKatu { get; set; }
        public int LastHitMiss { get; set; }
        public int LastMaxCombo { get; set; }
        public int LastScore { get; set; }
        public double LastAccuracy { get; set; }
    }
}
