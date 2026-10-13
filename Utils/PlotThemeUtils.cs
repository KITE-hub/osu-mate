using OsuMate.Models;
using OxyPlot;

namespace OsuMate.Utils
{

  internal static class PlotThemeUtils
  {
    internal static void ApplyBaseColors(PlotModel model, ThemeSettings theme)
    {
      model.TextColor = theme.OxyTextColor;
      model.PlotAreaBorderColor = theme.OxyBorderColor;
      model.DefaultFont = theme.OxyFontFamily;
    }

    internal static void ApplyGridTheme(PlotModel model, ThemeSettings theme)
    {
      ApplyBaseColors(model, theme);

      foreach (var axis in model.Axes)
      {
        axis.TicklineColor = theme.OxyBorderColor;
        axis.MajorGridlineColor = OxyColor.FromAColor(40, theme.OxyBorderColor);
      }

      model.InvalidatePlot(false);
    }

    internal static OxyColor JudgementColor(int judgement, ThemeSettings theme) =>
      judgement switch
      {
        1 => ColorUtils.OxyFromHsl(180, theme.PlotSaturation, theme.PlotLightness),
        2 => ColorUtils.OxyFromHsl(45, theme.PlotSaturation, theme.PlotLightness),
        3 => ColorUtils.OxyFromHsl(120, theme.PlotSaturation, theme.PlotLightness),
        4 => ColorUtils.OxyFromHsl(210, theme.PlotSaturation, theme.PlotLightness),
        5 => ColorUtils.OxyFromHsl(300, theme.PlotSaturation, theme.PlotLightness),
        _ => ColorUtils.OxyFromHsl(0, 0, theme.PlotLightness),
      };
  }
}
