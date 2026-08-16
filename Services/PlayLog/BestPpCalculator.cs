using OsuMate.Models;
using OsuMate.Services.Osu;

namespace OsuMate.Services.PlayLog
{
  internal static class BestPpCalculator
  {
    internal static double? GetBestPp(
      IEnumerable<PlayLogEntry> entries,
      string beatmapMd5,
      IEnumerable<string> targetPlayerNames
    )
    {
      if (string.IsNullOrEmpty(beatmapMd5))
        return null;

      double? best = null;
      foreach (var entry in entries)
      {
        if (entry.BeatmapMd5 != beatmapMd5)
          continue;
        if (!entry.IsCompleted)
          continue;
        if (entry.Pp is not { } pp)
          continue;
        if (!TargetPlayerFilter.Matches(entry.PlayerName, targetPlayerNames))
          continue;

        if (best == null || pp > best.Value)
          best = pp;
      }

      return best;
    }
  }
}
