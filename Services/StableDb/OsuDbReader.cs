using System;
using System.Collections.Generic;
using System.IO;

namespace OsuMate.Services.StableDb
{
  public class OsuDbReader : OsuBinaryReader
  {
    public int Version { get; private set; }
    public string? PlayerName { get; private set; }

    private OsuDbReader(Stream stream)
      : base(stream) { }

    public static Dictionary<string, BeatmapInfo> ReadBeatmaps(string osuDbPath)
    {
      using var stream = File.OpenRead(osuDbPath);
      using var reader = new OsuDbReader(stream);
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
          result[info.Md5Hash] = info;
        }
      }

      return result;
    }

    private BeatmapInfo ReadBeatmapEntry()
    {
      if (Version < 20191106)
      {
        ReadInt32();
      }

      string artistAscii = ReadString() ?? "";
      ReadString();
      string titleAscii = ReadString() ?? "";
      ReadString();
      string creator = ReadString() ?? "";
      string difficulty = ReadString() ?? "";
      ReadString();
      string md5 = ReadString() ?? "";
      string osuFileName = ReadString() ?? "";
      ReadByte();

      ReadUInt16();
      ReadUInt16();
      ReadUInt16();
      ReadInt64();

      float ar,
        cs,
        hp,
        od;
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

      ReadDouble();

      if (Version >= 20140609)
      {
        SkipStarRatingPairs();
        SkipStarRatingPairs();
        SkipStarRatingPairs();
        SkipStarRatingPairs();
      }

      ReadInt32();
      ReadInt32();
      ReadInt32();

      int numTimingPoints = ReadInt32();

      if (numTimingPoints < 0)
        throw new InvalidDataException("Negative timing point count.");
      Skip(checked(numTimingPoints * 17));

      int difficultyId = ReadInt32();
      int beatmapSetId = ReadInt32();
      ReadInt32();

      ReadByte();
      ReadByte();
      ReadByte();
      ReadByte();

      ReadUInt16();
      ReadSingle();
      byte mode = ReadByte();

      ReadString();
      ReadString();
      ReadUInt16();
      ReadString();
      ReadByte();
      ReadInt64();
      ReadByte();
      string folderName = ReadString() ?? "";
      ReadInt64();
      ReadByte();
      ReadByte();
      ReadByte();
      ReadByte();
      ReadByte();

      if (Version < 20140609)
      {
        ReadUInt16();
      }

      ReadInt32();
      ReadByte();

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
      int count = ReadInt32();
      int bytesPerPair = Version < 20250107 ? 14 : 10;
      if (count < 0)
        throw new InvalidDataException("Negative star rating pair count.");
      Skip(checked(count * bytesPerPair));
    }
  }
}
