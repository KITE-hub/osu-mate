using System;

namespace OsuMate.Services.PlayLog
{
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

    public string BeatmapMd5 { get; set; } = "";

    public string PlayerName { get; set; } = "";
    public int Mode { get; set; }
    public int? ManiaKeyCount { get; set; }
    public bool ManiaKeyCountResolveAttempted { get; set; }
    public string[] Mods { get; set; } = [];

    public int ModsRaw { get; set; }

    public double OverallDifficulty { get; set; }

    public int StartRetries { get; set; }

    public string? PendingCompletedKey { get; set; }

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
