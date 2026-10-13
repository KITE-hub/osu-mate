using System;

namespace OsuMate.Services.PlayLog
{
  public sealed class PlaySession
  {
    public DateTime StartedAt { get; init; }
    public int BeatmapId { get; init; }
    public int BeatmapSetId { get; init; }
    public string Artist { get; init; } = "";
    public string Title { get; init; } = "";
    public string DifficultyName { get; init; } = "";
    public string Creator { get; init; } = "";
    public string FolderName { get; init; } = "";
    public string OsuFileName { get; init; } = "";

    public string BeatmapMd5 { get; init; } = "";

    private string _playerName = "";
    private int _mode;
    private int? _maniaKeyCount;

    public string PlayerName
    {
      get => _playerName;
      init => _playerName = value;
    }

    public int Mode
    {
      get => _mode;
      init => _mode = value;
    }

    public int? ManiaKeyCount
    {
      get => _maniaKeyCount;
      init => _maniaKeyCount = value;
    }

    public bool ManiaKeyCountResolveAttempted { get; private set; }
    public string[] Mods { get; init; } = [];

    public int ModsRaw { get; init; }

    public double OverallDifficulty { get; init; }

    public int StartRetries { get; init; }

    public string? PendingCompletedKey { get; private set; }

    public int LastHit300 { get; private set; }
    public int LastHit100 { get; private set; }
    public int LastHit50 { get; private set; }
    public int LastHitGeki { get; private set; }
    public int LastHitKatu { get; private set; }
    public int LastHitMiss { get; private set; }
    public int LastMaxCombo { get; private set; }
    public int LastScore { get; private set; }
    public double LastAccuracy { get; private set; }

    public bool NeedsManiaKeyCountResolve =>
      Mode == Models.OsuMode.Mania && ManiaKeyCount == null && !ManiaKeyCountResolveAttempted;

    public void UpdateMode(int mode)
    {
      _mode = mode;
      if (mode != Models.OsuMode.Mania)
        _maniaKeyCount = null;
    }

    public void SetManiaKeyCount(int? keyCount)
    {
      _maniaKeyCount = keyCount;
      ManiaKeyCountResolveAttempted = true;
    }

    public void UpdatePlayerName(string? playerName)
    {
      if (!string.IsNullOrWhiteSpace(playerName))
        _playerName = playerName;
    }

    public void MarkCommitted(string completedKey) => PendingCompletedKey = completedKey;

    public void CaptureScore(
      int hit300,
      int hit100,
      int hit50,
      int hitGeki,
      int hitKatu,
      int hitMiss,
      int maxCombo,
      int score,
      double accuracy
    )
    {
      LastHit300 = hit300;
      LastHit100 = hit100;
      LastHit50 = hit50;
      LastHitGeki = hitGeki;
      LastHitKatu = hitKatu;
      LastHitMiss = hitMiss;
      LastMaxCombo = maxCombo;
      LastScore = score;
      LastAccuracy = accuracy;
    }
  }
}
