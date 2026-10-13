using OsuMate.Models;

namespace OsuMate.Services.PlayLog
{
  internal sealed class PlayLogEntryDto
  {
    public DateTimeOffset PlayedAt { get; set; }
    public string DedupeKey { get; set; } = "";
    public long? OnlineScoreId { get; set; }
    public string? ReplayMd5 { get; set; }
    public int BeatmapId { get; set; }
    public int BeatmapSetId { get; set; }
    public string Artist { get; set; } = "";
    public string Title { get; set; } = "";
    public string DifficultyName { get; set; } = "";
    public string Creator { get; set; } = "";
    public string BeatmapMd5 { get; set; } = "";
    public string PlayerName { get; set; } = "";
    public int Mode { get; set; }
    public int? ManiaKeyCount { get; set; }
    public int Count300 { get; set; }
    public int Count100 { get; set; }
    public int Count50 { get; set; }
    public int CountGeki { get; set; }
    public int CountKatu { get; set; }
    public int CountMiss { get; set; }
    public int MaxCombo { get; set; }
    public int TotalScore { get; set; }
    public double Accuracy { get; set; }
    public double OverallDifficulty { get; set; }
    public string ModsString { get; set; } = "NM";
    public int ModsRaw { get; set; }
    public bool IsCompleted { get; set; }
    public bool IsProvisional { get; set; }
    public double? StarRating { get; set; }
    public double? Pp { get; set; }
    public bool IsCalculationFailed { get; set; }

    internal static PlayLogEntryDto FromEntry(PlayLogEntry entry) =>
      new()
      {
        PlayedAt = entry.PlayedAt,
        DedupeKey = entry.DedupeKey,
        OnlineScoreId = entry.OnlineScoreId,
        ReplayMd5 = entry.ReplayMd5,
        BeatmapId = entry.BeatmapId,
        BeatmapSetId = entry.BeatmapSetId,
        Artist = entry.Artist,
        Title = entry.Title,
        DifficultyName = entry.DifficultyName,
        Creator = entry.Creator,
        BeatmapMd5 = entry.BeatmapMd5,
        PlayerName = entry.PlayerName,
        Mode = entry.Mode,
        ManiaKeyCount = entry.ManiaKeyCount,
        Count300 = entry.Count300,
        Count100 = entry.Count100,
        Count50 = entry.Count50,
        CountGeki = entry.CountGeki,
        CountKatu = entry.CountKatu,
        CountMiss = entry.CountMiss,
        MaxCombo = entry.MaxCombo,
        TotalScore = entry.TotalScore,
        Accuracy = entry.Accuracy,
        OverallDifficulty = entry.OverallDifficulty,
        ModsString = entry.ModsString,
        ModsRaw = entry.ModsRaw,
        IsCompleted = entry.IsCompleted,
        IsProvisional = entry.IsProvisional,
        StarRating = entry.StarRating,
        Pp = entry.Pp,
        IsCalculationFailed = entry.IsCalculationFailed,
      };

    internal PlayLogEntry ToEntry() =>
      new()
      {
        PlayedAt = PlayedAt,
        DedupeKey = DedupeKey,
        OnlineScoreId = OnlineScoreId,
        ReplayMd5 = ReplayMd5,
        BeatmapId = BeatmapId,
        BeatmapSetId = BeatmapSetId,
        Artist = Artist,
        Title = Title,
        DifficultyName = DifficultyName,
        Creator = Creator,
        BeatmapMd5 = BeatmapMd5,
        PlayerName = PlayerName,
        Mode = Mode,
        ManiaKeyCount = ManiaKeyCount,
        Count300 = Count300,
        Count100 = Count100,
        Count50 = Count50,
        CountGeki = CountGeki,
        CountKatu = CountKatu,
        CountMiss = CountMiss,
        MaxCombo = MaxCombo,
        TotalScore = TotalScore,
        Accuracy = Accuracy,
        OverallDifficulty = OverallDifficulty,
        ModsString = ModsString,
        ModsRaw = ModsRaw,
        IsCompleted = IsCompleted,
        IsProvisional = IsProvisional,
        StarRating = StarRating,
        Pp = Pp,
        IsCalculationFailed = IsCalculationFailed,
      };
  }
}
