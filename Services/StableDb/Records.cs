using System;

namespace OsuMate.Services.StableDb
{
  public class BeatmapInfo
  {
    public string Md5Hash { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Title { get; set; } = "";
    public string Creator { get; set; } = "";
    public string DifficultyName { get; set; } = "";

    public int DifficultyId { get; set; }

    public int BeatmapSetId { get; set; }

    public float OverallDifficulty { get; set; }
    public float CircleSize { get; set; }
    public float ApproachRate { get; set; }
    public float HpDrain { get; set; }

    public byte Mode { get; set; }

    public string FolderName { get; set; } = "";

    public string OsuFileName { get; set; } = "";
  }

  public class ScoreRecord
  {
    public byte Mode { get; set; }
    public string Md5Hash { get; set; } = "";
    public string PlayerName { get; set; } = "";
    public string ReplayMd5 { get; set; } = "";

    public ushort Count300 { get; set; }
    public ushort Count100 { get; set; }
    public ushort Count50 { get; set; }
    public ushort CountGeki { get; set; }
    public ushort CountKatu { get; set; }
    public ushort CountMiss { get; set; }

    public int TotalScore { get; set; }
    public ushort MaxCombo { get; set; }
    public bool IsPerfectCombo { get; set; }
    public int Mods { get; set; }

    public long TimestampTicks { get; set; }

    public long OnlineScoreId { get; set; }

    public int TotalJudged => Count300 + Count100 + Count50 + CountGeki + CountKatu + CountMiss;
  }
}
