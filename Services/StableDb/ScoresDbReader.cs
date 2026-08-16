using System;
using System.Collections.Generic;
using System.IO;

namespace OsuMate.Services.StableDb
{
  public class ScoresDbReader : OsuBinaryReader
  {
    public int Version { get; private set; }

    private const int ModTargetPractice = 1 << 23;

    private ScoresDbReader(Stream stream)
      : base(stream) { }

    public static List<ScoreRecord> ReadScores(string scoresDbPath)
    {
      using var stream = File.OpenRead(scoresDbPath);
      using var reader = new ScoresDbReader(stream);
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
      ReadInt32();
      ReadString();
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

      ReadString();

      long timestampTicks = ReadInt64();

      ReadInt32();

      long onlineScoreId = ReadInt64();

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
