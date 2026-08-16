using System.Collections.Concurrent;
using System.Windows.Media;
using OxyPlot;

namespace OsuMate.Utils;

internal static class ColorUtils
{
  internal static Color FromHsl(double h, double s, double l)
  {
    h /= 360;

    double r = l;
    double g = l;
    double b = l;

    if (s != 0)
    {
      double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
      double p = 2 * l - q;

      r = HueToRgb(p, q, h + 1.0 / 3);
      g = HueToRgb(p, q, h);
      b = HueToRgb(p, q, h - 1.0 / 3);
    }

    return Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
  }

  internal static OxyColor OxyFromHsl(double h, double s, double l)
  {
    var c = FromHsl(h, s, l);
    return OxyColor.FromRgb(c.R, c.G, c.B);
  }

  private static double HueToRgb(double p, double q, double t)
  {
    if (t < 0)
      t += 1;
    if (t > 1)
      t -= 1;
    if (t < 1.0 / 6)
      return p + (q - p) * 6 * t;
    if (t < 1.0 / 2)
      return q;
    if (t < 2.0 / 3)
      return p + (q - p) * (2.0 / 3 - t) * 6;
    return p;
  }

  private static readonly ConcurrentDictionary<Color, SolidColorBrush> _brushCache = new();

  internal static SolidColorBrush ColorBrush(Color c)
  {
    return _brushCache.GetOrAdd(
      c,
      static color =>
      {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
      }
    );
  }

  internal static SolidColorBrush ColorBrushAlpha(Color c, byte alpha) =>
    ColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));

  internal static Color JudgementColor(int judgement) =>
    judgement switch
    {
      1 => Color.FromRgb(50, 188, 200),
      2 => Color.FromRgb(255, 200, 50),
      3 => Color.FromRgb(50, 200, 100),
      4 => Color.FromRgb(50, 100, 255),
      5 => Color.FromRgb(200, 100, 255),
      _ => Color.FromRgb(150, 150, 150),
    };
}
