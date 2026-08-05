using Material.Icons;
using OsuMate.Models;
using OsuMate.Utils;
using System.Windows;
using System.Windows.Media;

namespace OsuMate.ViewModels
{
    public class ThemeViewModel : ObservableBase
    {
        private bool _isDark = true;
        public bool IsDark => _isDark;
        public ThemeSettings Current { get; private set; } = ThemeSettings.Dark();
        public string CurrentFont { get; private set; } = "Segoe UI";

        public MaterialIconKind ThemeIconKind => _isDark
            ? MaterialIconKind.WeatherNight
            : MaterialIconKind.WhiteBalanceSunny;

        public ThemeViewModel()
        {
            // フォントリソースの初期設定はここでは行わない。
            // MainWindow が SettingsViewModel をロードした後に SetFont() を呼ぶので
            // そこで AppFont リソースが正しい値で設定される。
            ApplyTheme();
        }

        // テーマXAMLファイルのURI
        private static readonly Uri DarkThemeUri  = new("/osu-mate;component/Resources/DarkTheme.xaml",  UriKind.Relative);
        private static readonly Uri LightThemeUri = new("/osu-mate;component/Resources/LightTheme.xaml", UriKind.Relative);

        private void ApplyTheme()
        {
            // App.xaml の MergedDictionaries[0] がテーマ辞書。丸ごと差し替える。
            var merged = Application.Current.Resources.MergedDictionaries;
            var themeUri = _isDark ? DarkThemeUri : LightThemeUri;
            merged[0] = new ResourceDictionary { Source = themeUri };
            OnPropertyChanged(nameof(ThemeIconKind));
        }

        public void Toggle()
        {
            _isDark = !_isDark;
            // テーマカラーを切り替えつつ、現在のフォントを引き継ぐ
            Current = (_isDark ? ThemeSettings.Dark() : ThemeSettings.Light())
                .WithFont(CurrentFont);
            ApplyTheme();
        }

        public void SetFont(string fontFamily)
        {
            CurrentFont = fontFamily;
            Application.Current.Resources["AppFont"] = AppFonts.Resolve(fontFamily);
            Current = Current.WithFont(fontFamily);
            OnPropertyChanged(nameof(CurrentFont));
        }
    }
}
