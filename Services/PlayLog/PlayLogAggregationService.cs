using OsuMate.Models;

namespace OsuMate.Services.PlayLog
{
  public class PlayLogAggregationService
  {
    public IReadOnlyDictionary<DateOnly, int> AggregateDailyHits(IEnumerable<PlayLogEntry> entries)
    {
      var result = new Dictionary<DateOnly, int>();

      foreach (var entry in entries)
      {
        var date = DateOnly.FromDateTime(entry.PlayedAt);
        var hits =
          entry.Count300 + entry.Count100 + entry.Count50 + entry.CountGeki + entry.CountKatu;

        result[date] = result.GetValueOrDefault(date) + hits;
      }

      return result;
    }
  }
}
