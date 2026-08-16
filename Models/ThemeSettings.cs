using OsuMate.Utils;
using OxyPlot;

namespace OsuMate.Models
{
  public sealed record ThemeSettings
  {
    public required OxyColor OxyTextColor { get; init; }
    public required OxyColor OxyBorderColor { get; init; }
    public required OxyColor OxyAccentColor { get; init; }
    public required double PlotSaturation { get; init; }
    public required double PlotLightness { get; init; }
    public string OxyFontFamily { get; init; } = "Segoe UI";

    public static ThemeSettings Dark() =>
      new()
      {
        OxyTextColor = OxyColor.FromRgb(220, 220, 220),
        OxyBorderColor = OxyColor.FromRgb(80, 80, 80),
        OxyAccentColor = OxyColor.FromRgb(0x87, 0xF1, 0xF7),
        PlotSaturation = 0.7,
        PlotLightness = 0.6,
      };

    public static ThemeSettings Light() =>
      new()
      {
        OxyTextColor = OxyColor.FromRgb(30, 30, 30),
        OxyBorderColor = OxyColor.FromRgb(180, 180, 180),
        OxyAccentColor = OxyColor.FromRgb(0x2A, 0xB2, 0xB9),
        PlotSaturation = 0.65,
        PlotLightness = 0.45,
      };

    public ThemeSettings WithFont(string fontFamily) =>
      this with
      {
        OxyFontFamily = AppFonts.ResolveFontFamilyString(fontFamily),
      };
  }
}
