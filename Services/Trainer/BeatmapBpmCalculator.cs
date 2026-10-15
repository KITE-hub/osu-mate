namespace OsuMate.Services.Trainer
{
  internal readonly record struct BpmSummary(decimal Dominant, decimal Min, decimal Max);

  internal static class BeatmapBpmCalculator
  {
    internal static BpmSummary? Compute(List<(double time, double beatLength)> pts, double lastHitObjectTime)
    {
      if (pts.Count == 0)
        return null;
      pts.Sort((a, b) => a.time.CompareTo(b.time));

      var durations = new Dictionary<double, double>();
      for (int i = 0; i < pts.Count; i++)
      {
        double endTime =
          (i + 1 < pts.Count) ? pts[i + 1].time : Math.Max(pts[i].time, lastHitObjectTime);
        double dur = Math.Max(0, endTime - pts[i].time);
        var bl = pts[i].beatLength;
        if (durations.ContainsKey(bl))
          durations[bl] += dur;
        else
          durations[bl] = dur;
      }

      var dominantBl = durations.OrderByDescending(kv => kv.Value).First().Key;
      return new BpmSummary(
        ToSafeBpm(60000.0 / dominantBl),
        ToSafeBpm(60000.0 / pts.Max(p => p.beatLength)),
        ToSafeBpm(60000.0 / pts.Min(p => p.beatLength))
      );
    }

    private const decimal SafeBpmBound = 1_000_000_000_000m;

    private static decimal ToSafeBpm(double bpm)
    {
      if (double.IsNaN(bpm))
        return 0m;
      if (bpm >= (double)SafeBpmBound)
        return SafeBpmBound;
      if (bpm <= -(double)SafeBpmBound)
        return -SafeBpmBound;
      return (decimal)bpm;
    }
  }
}
