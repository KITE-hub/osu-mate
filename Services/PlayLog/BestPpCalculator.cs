using OsuMate.Models;
using OsuMate.Services.Osu;

namespace OsuMate.Services.PlayLog
{
  public readonly record struct BeatmapPlayStats(
    double? BestPp,
    DateTime? BestPpAchievedAt,
    DateTime? LatestPlayedAt
  );

  internal static class BestPpCalculator
  {
    internal static BeatmapPlayStats GetStats(
      IEnumerable<PlayLogEntry> entries,
      string beatmapMd5,
      IEnumerable<string> targetPlayerNames
    )
    {
      if (string.IsNullOrEmpty(beatmapMd5))
        return default;

      double? bestPp = null;
      DateTime? bestPpPlayedAt = null;
      DateTime? latestPlayedAt = null;

      foreach (var entry in entries)
      {
        if (entry.BeatmapMd5 != beatmapMd5)
          continue;
        if (!entry.IsCompleted)
          continue;
        if (!TargetPlayerFilter.Matches(entry.PlayerName, targetPlayerNames))
          continue;

        if (entry.Pp is { } pp && (bestPp == null || pp > bestPp.Value))
        {
          bestPp = pp;
          bestPpPlayedAt = entry.PlayedAt;
        }

        if (latestPlayedAt == null || entry.PlayedAt > latestPlayedAt.Value)
          latestPlayedAt = entry.PlayedAt;
      }

      return new BeatmapPlayStats(bestPp, bestPpPlayedAt, latestPlayedAt);
    }
  }
}
