using System;
using System.Collections.Generic;
using System.IO;

namespace OsuMate.Services.StableDb
{
    /// <summary>
    /// osu!.db (曲マスタDB) を読み、MD5ハッシュをキーにした BeatmapInfo の辞書を作る。
    ///
    /// フォーマットは osu! 公式 wiki (Client/File formats/osu!.db) に準拠。
    /// </summary>
    public class OsuDbReader : OsuBinaryReader
    {
        public int Version { get; private set; }
        public string? PlayerName { get; private set; }

        private OsuDbReader(byte[] data) : base(data)
        {
        }

        public static Dictionary<string, BeatmapInfo> ReadBeatmaps(string osuDbPath)
        {
            var bytes = File.ReadAllBytes(osuDbPath);
            var reader = new OsuDbReader(bytes);
            return reader.Parse();
        }

        private Dictionary<string, BeatmapInfo> Parse()
        {
            Version = ReadInt32();
            int folderCount = ReadInt32();
            bool accountUnlocked = ReadByte() != 0;
            long dateUnlockTicks = ReadInt64();
            PlayerName = ReadString();
            int numBeatmaps = ReadInt32();

            var result = new Dictionary<string, BeatmapInfo>(numBeatmaps);

            for (int i = 0; i < numBeatmaps; i++)
            {
                var info = ReadBeatmapEntry();
                if (!string.IsNullOrEmpty(info.Md5Hash))
                {
                    // 同一MD5が複数回出ることは想定していないが、
                    // 万一あれば後勝ちで上書きする(最新スキャン結果を優先)。
                    result[info.Md5Hash] = info;
                }
            }

            return result;
        }

        private BeatmapInfo ReadBeatmapEntry()
        {
            // version < 20191106 の場合のみ、エントリ全体のバイトサイズが先頭に入る。
            if (Version < 20191106)
            {
                ReadInt32(); // entry size in bytes (未使用、スキップ用)
            }

            string artistAscii = ReadString() ?? "";
            ReadString(); // artist unicode (未使用)
            string titleAscii = ReadString() ?? "";
            ReadString(); // title unicode (未使用)
            string creator = ReadString() ?? "";
            string difficulty = ReadString() ?? "";
            ReadString(); // audio file name (未使用)
            string md5 = ReadString() ?? "";
            string osuFileName = ReadString() ?? "";
            ReadByte();   // ranked status (未使用)

            ReadUInt16(); // circle count
            ReadUInt16(); // slider count
            ReadUInt16(); // spinner count
            ReadInt64();  // last modification time

            float ar, cs, hp, od;
            if (Version < 20140609)
            {
                ar = ReadByte();
                cs = ReadByte();
                hp = ReadByte();
                od = ReadByte();
            }
            else
            {
                ar = ReadSingle();
                cs = ReadSingle();
                hp = ReadSingle();
                od = ReadSingle();
            }

            ReadDouble(); // slider velocity

            if (Version >= 20140609)
            {
                SkipStarRatingPairs(); // std star rating pairs (mod -> SR)
                SkipStarRatingPairs(); // taiko
                SkipStarRatingPairs(); // ctb
                SkipStarRatingPairs(); // mania
            }

            ReadInt32(); // drain time (seconds)
            ReadInt32(); // total time (ms)
            ReadInt32(); // preview time (ms)

            int numTimingPoints = ReadInt32();
            // 各タイミングポイントは double(BPM) + double(offset) + bool = 8+8+1 = 17 bytes
            _offset += numTimingPoints * 17;

            int difficultyId = ReadInt32();  // .osu の BeatmapID に相当 (個別差分ID)
            int beatmapSetId = ReadInt32();  // .osu の BeatmapSetID に相当 (曲セットID)
            ReadInt32();                     // thread id (未使用)

            ReadByte(); // grade std
            ReadByte(); // grade taiko
            ReadByte(); // grade ctb
            ReadByte(); // grade mania

            ReadUInt16(); // local offset
            ReadSingle();  // stack leniency
            byte mode = ReadByte();

            ReadString(); // source (未使用)
            ReadString(); // tags (未使用)
            ReadUInt16(); // online offset
            ReadString(); // title font (未使用)
            ReadByte();   // is unplayed
            ReadInt64();  // last played
            ReadByte();   // is osz2
            string folderName = ReadString() ?? "";
            ReadInt64();  // last checked against repo
            ReadByte();   // ignore beatmap sound
            ReadByte();   // ignore beatmap skin
            ReadByte();   // disable storyboard
            ReadByte();   // disable video
            ReadByte();   // visual override

            if (Version < 20140609)
            {
                ReadUInt16(); // unknown short, old versions only
            }

            ReadInt32(); // last modification time (again, "unknown")
            ReadByte();  // mania scroll speed

            return new BeatmapInfo
            {
                Md5Hash = md5,
                Artist = artistAscii,
                Title = titleAscii,
                Creator = creator,
                DifficultyName = difficulty,
                DifficultyId = difficultyId,
                BeatmapSetId = beatmapSetId,
                OverallDifficulty = od,
                CircleSize = cs,
                ApproachRate = ar,
                HpDrain = hp,
                Mode = mode,
                FolderName = folderName,
                OsuFileName = osuFileName,
            };
        }

        private void SkipStarRatingPairs()
        {
            // version < 20250107: 0x08(1) + int32(4) + 0x0d(1) + double(8) = 14 bytes/pair
            // version >= 20250107: 0x08(1) + int32(4) + 0x0c(1) + float(4)  = 10 bytes/pair
            int count = ReadInt32();
            int bytesPerPair = Version < 20250107 ? 14 : 10;
            _offset += count * bytesPerPair;
        }
    }
}
