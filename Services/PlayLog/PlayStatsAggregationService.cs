using System.Linq;
using OsuMate.Models;

namespace OsuMate.Services.PlayLog
{
  public class PlayStatsAggregationService
  {
    public IReadOnlyList<DailyPlayStats> AggregateDailyStats(IEnumerable<PlayLogEntry> entries)
    {
      return entries
        .GroupBy(e => DateOnly.FromDateTime(e.PlayedAt))
        .OrderBy(g => g.Key)
        .Select(g => new DailyPlayStats(
          g.Key,
          ComputeStat(g.Select(e => e.StarRating)),
          ComputeStat(g.Select(e => e.Pp)),
          ComputeStat(g.Select(e => (double?)e.Accuracy))
        ))
        .ToList();
    }

    private static MetricStat ComputeStat(IEnumerable<double?> values)
    {
      var list = values.Where(v => v.HasValue).Select(v => v!.Value).ToList();
      if (list.Count == 0)
        return MetricStat.Empty;

      var mean = list.Average();

      var variance = list.Count > 1 ? list.Sum(v => (v - mean) * (v - mean)) / (list.Count - 1) : 0;

      return new MetricStat(mean, Math.Sqrt(variance), list.Count);
    }
  }
}
