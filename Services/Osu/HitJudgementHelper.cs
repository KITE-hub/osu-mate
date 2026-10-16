using osu.Game.Rulesets.Scoring;

namespace OsuMate.Services.Osu
{
  internal readonly struct HitJudgementWindows
  {
    private readonly double _perfect;
    private readonly double _great;
    private readonly double _good;
    private readonly double _ok;
    private readonly double _meh;

    internal double Max { get; }

    internal HitJudgementWindows(IReadOnlyDictionary<HitResult, double> hitWindows)
    {
      _perfect = HitJudgementHelper.GetWindow(hitWindows, HitResult.Perfect);
      _great = HitJudgementHelper.GetWindow(hitWindows, HitResult.Great);
      _good = HitJudgementHelper.GetWindow(hitWindows, HitResult.Good);
      _ok = HitJudgementHelper.GetWindow(hitWindows, HitResult.Ok);
      _meh = HitJudgementHelper.GetWindow(hitWindows, HitResult.Meh);
      Max = HitJudgementHelper.GetMaxWindow(hitWindows);
    }

    internal int GetJudgement(double offsetMs)
    {
      double abs = Math.Abs(offsetMs);

      if (_perfect > 0 && abs <= _perfect)
        return 1;
      if (_great > 0 && abs <= _great)
        return 2;
      if (_good > 0 && abs <= _good)
        return 3;
      if (_ok > 0 && abs <= _ok)
        return 4;
      if (_meh > 0 && abs <= _meh)
        return 5;
      return 6;
    }
  }

  internal static class HitJudgementHelper
  {
    public static int GetJudgement(double offsetMs, IReadOnlyDictionary<HitResult, double> hitWindows) =>
      new HitJudgementWindows(hitWindows).GetJudgement(offsetMs);

    public static double GetWindow(IReadOnlyDictionary<HitResult, double> hitWindows, HitResult r) =>
      hitWindows.TryGetValue(r, out var v) ? v : 0;

    public static double GetMaxWindow(IReadOnlyDictionary<HitResult, double> hitWindows)
    {
      double max = 0;
      foreach (double value in hitWindows.Values)
      {
        if (value > max)
          max = value;
      }

      return max;
    }

    public static List<(
      int judgement,
      double msValue,
      double from,
      double to
    )> GetCenterLineSegments(IReadOnlyDictionary<HitResult, double> hitWindows)
    {
      double maxWindow = GetMaxWindow(hitWindows);
      if (maxWindow == 0)
        return [];

      double total = maxWindow * 2;
      double ToRatio(double ms) => (ms + maxWindow) / total;

      var result = new List<(int, double, double, double)>();
      foreach (
        var (w, j) in new[]
        {
          (GetWindow(hitWindows, HitResult.Meh), 5),
          (GetWindow(hitWindows, HitResult.Ok), 4),
          (GetWindow(hitWindows, HitResult.Good), 3),
          (GetWindow(hitWindows, HitResult.Great), 2),
          (GetWindow(hitWindows, HitResult.Perfect), 1),
        }
      )
      {
        if (w > 0)
          result.Add((j, w, ToRatio(-w), ToRatio(w)));
      }
      return result;
    }
  }
}
