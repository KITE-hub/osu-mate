using osu.Game.Rulesets.Scoring;
using OsuMate.Models;
using OsuMate.Utils;
using OxyPlot;

namespace OsuMate.Services.Osu
{
  internal static class HitJudgementHelper
  {
    public static OxyColor GetOxyColor(int judgement, ThemeSettings theme) =>
      judgement switch
      {
        1 => ColorUtils.OxyFromHsl(180, theme.PlotSaturation, theme.PlotLightness),
        2 => ColorUtils.OxyFromHsl(45, theme.PlotSaturation, theme.PlotLightness),
        3 => ColorUtils.OxyFromHsl(120, theme.PlotSaturation, theme.PlotLightness),
        4 => ColorUtils.OxyFromHsl(210, theme.PlotSaturation, theme.PlotLightness),
        5 => ColorUtils.OxyFromHsl(300, theme.PlotSaturation, theme.PlotLightness),
        _ => ColorUtils.OxyFromHsl(0, 0, theme.PlotLightness),
      };

    public static int GetJudgement(double offsetMs, IReadOnlyDictionary<HitResult, double> hitWindows)
    {
      double abs = Math.Abs(offsetMs);
      double perfect = GetWindow(hitWindows, HitResult.Perfect);
      double great = GetWindow(hitWindows, HitResult.Great);
      double good = GetWindow(hitWindows, HitResult.Good);
      double ok = GetWindow(hitWindows, HitResult.Ok);
      double meh = GetWindow(hitWindows, HitResult.Meh);

      if (perfect > 0 && abs <= perfect)
        return 1;
      if (great > 0 && abs <= great)
        return 2;
      if (good > 0 && abs <= good)
        return 3;
      if (ok > 0 && abs <= ok)
        return 4;
      if (meh > 0 && abs <= meh)
        return 5;
      return 6;
    }

    public static double GetWindow(IReadOnlyDictionary<HitResult, double> hitWindows, HitResult r) =>
      hitWindows.TryGetValue(r, out var v) ? v : 0;

    public static double GetMaxWindow(IReadOnlyDictionary<HitResult, double> hitWindows) =>
      hitWindows.Values.Where(x => x > 0).DefaultIfEmpty(0).Max();

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
