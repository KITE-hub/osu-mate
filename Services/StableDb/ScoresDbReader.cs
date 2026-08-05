using System;
using System.Collections.Generic;
using System.IO;

namespace OsuMate.Services.StableDb
{
    /// <summary>
    /// scores.db (プレイ履歴DB) を読み、ScoreRecord のリストを作る。
    ///
    /// 個々のスコアのフォーマットは .osr (リプレイファイル) のヘッダー部分と
    /// ほぼ同一だが、操作ログ(LZMA圧縮された入力データ)を持たない点が異なる。
    /// また stable の設計上、中断(リタイア)したプレイはそもそも記録されない
    /// </summary>
    public class ScoresDbReader : OsuBinaryReader
    {
        public int Version { get; private set; }

        private const int ModTargetPractice = 1 << 23; // 8388608

        private ScoresDbReader(byte[] data) : base(data)
        {
        }

        public static List<ScoreRecord> ReadScores(string scoresDbPath)
        {
            var bytes = File.ReadAllBytes(scoresDbPath);
            var reader = new ScoresDbReader(bytes);
            return reader.Parse();
        }

        private List<ScoreRecord> Parse()
        {
            Version = ReadInt32();
            int numBeatmaps = ReadInt32();

            var result = new List<ScoreRecord>();

            for (int i = 0; i < numBeatmaps; i++)
            {
                string beatmapMd5 = ReadString() ?? "";
                int numScores = ReadInt32();

                for (int j = 0; j < numScores; j++)
                {
                    var score = ReadScoreEntry(beatmapMd5);
                    result.Add(score);
                }
            }

            return result;
        }

        private ScoreRecord ReadScoreEntry(string beatmapMd5FromParent)
        {
            byte mode = ReadByte();
            ReadInt32(); // score version (osu!バージョン番号)
            ReadString(); // score MD5 (スコア自体のハッシュ。ビートマップMD5ではないので使わない)
            string playerName = ReadString() ?? "";
            string replayMd5 = ReadString() ?? "";

            ushort n300 = ReadUInt16();
            ushort n100 = ReadUInt16();
            ushort n50 = ReadUInt16();
            ushort nGeki = ReadUInt16();
            ushort nKatu = ReadUInt16();
            ushort nMiss = ReadUInt16();

            int totalScore = ReadInt32();
            ushort maxCombo = ReadUInt16();
            bool perfect = ReadByte() != 0;
            int mods = ReadInt32();

            ReadString(); // 常に空文字列 (.osr との互換性のための予約フィールド)

            long timestampTicks = ReadInt64();

            ReadInt32(); // 常に -1 (.osr ではここに圧縮リプレイデータ長が入る)

            long onlineScoreId = ReadInt64();

            // ターゲットプラクティス使用時のみ、末尾に精度(double)が追加される
            if ((mods & ModTargetPractice) != 0)
            {
                ReadDouble();
            }

            return new ScoreRecord
            {
                Mode = mode,
                Md5Hash = beatmapMd5FromParent,
                PlayerName = playerName,
                ReplayMd5 = replayMd5,
                Count300 = n300,
                Count100 = n100,
                Count50 = n50,
                CountGeki = nGeki,
                CountKatu = nKatu,
                CountMiss = nMiss,
                TotalScore = totalScore,
                MaxCombo = maxCombo,
                IsPerfectCombo = perfect,
                Mods = mods,
                TimestampTicks = timestampTicks,
                OnlineScoreId = onlineScoreId,
            };
        }
    }
}
