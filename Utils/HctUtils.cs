using System.Windows.Media;
using MaterialColorUtilities.ColorAppearance;
using OxyPlot;

namespace OsuMate.Utils
{
  public static class HctUtils
  {
    public static Color ToColor(double hue, double chroma, double tone)
    {
      uint argb = Hct.From(hue, chroma, tone).ToInt();
      byte a = (byte)((argb >> 24) & 0xFF);
      byte r = (byte)((argb >> 16) & 0xFF);
      byte g = (byte)((argb >> 8) & 0xFF);
      byte b = (byte)(argb & 0xFF);
      return Color.FromArgb(a, r, g, b);
    }

    public static OxyColor ToOxyColor(double hue, double chroma, double tone)
    {
      uint argb = Hct.From(hue, chroma, tone).ToInt();
      byte a = (byte)((argb >> 24) & 0xFF);
      byte r = (byte)((argb >> 16) & 0xFF);
      byte g = (byte)((argb >> 8) & 0xFF);
      byte b = (byte)(argb & 0xFF);
      return OxyColor.FromArgb(a, r, g, b);
    }
  }
}
