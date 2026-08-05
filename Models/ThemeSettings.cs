using OsuMate.Utils;
using OxyPlot;

namespace OsuMate.Models
{
    public class ThemeSettings
    {
        public OxyColor OxyTextColor { get; set; }
        public OxyColor OxyBorderColor { get; set; }

        /// <summary>
        /// アプリのアクセントカラー（WPF側の AccentBrush と同じ色）を OxyColor で表現したもの。
        /// LightTheme.xaml/DarkTheme.xaml の AccentBrush（Light: #FF2AB2B9, Dark: #FF87F1F7）と
        /// 値をハードコードで一致させている。XAMLリソース側は DynamicResource で動的に見た目が
        /// 切り替わるが、OxyPlot の PlotModel は WPF の DynamicResource を解決できない（構築時に
        /// 具体的な OxyColor 値として渡す必要がある）ため、ThemeSettings.Dark()/Light() の
        /// 生成タイミングでテーマに応じた固定値として持たせている。
        /// </summary>
        public OxyColor OxyAccentColor { get; set; }

        public double PlotSaturation { get; set; }
        public double PlotLightness { get; set; }
        public string OxyFontFamily { get; set; } = "Segoe UI";

        public static ThemeSettings Dark() => new()
        {
            OxyTextColor = OxyColor.FromRgb(220, 220, 220),
            OxyBorderColor = OxyColor.FromRgb(80, 80, 80),
            OxyAccentColor = OxyColor.FromRgb(0x87, 0xF1, 0xF7),
            PlotSaturation = 0.7,
            PlotLightness = 0.6,
        };

        public static ThemeSettings Light() => new()
        {
            OxyTextColor = OxyColor.FromRgb(30, 30, 30),
            OxyBorderColor = OxyColor.FromRgb(180, 180, 180),
            OxyAccentColor = OxyColor.FromRgb(0x2A, 0xB2, 0xB9),
            PlotSaturation = 0.65,
            PlotLightness = 0.45,
        };

        public ThemeSettings WithFont(string fontFamily) => new()
        {
            OxyTextColor = OxyTextColor,
            OxyBorderColor = OxyBorderColor,
            OxyAccentColor = OxyAccentColor,
            PlotSaturation = PlotSaturation,
            PlotLightness = PlotLightness,
            // AppFont（WPF UI側）と同じ解決ロジックを通すことで、Oxanium/Roboto選択時も
            // グラフ側の文字を同じフォントで揃える。
            OxyFontFamily = AppFonts.ResolveFontFamilyString(fontFamily),
        };
    }
}
