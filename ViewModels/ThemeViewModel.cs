using System.Windows;
using System.Windows.Media;
using Material.Icons;
using OsuMate.Models;
using OsuMate.Utils;

namespace OsuMate.ViewModels
{
  public class ThemeViewModel : ObservableBase
  {
    private bool _isDark = true;
    public bool IsDark => _isDark;
    public ThemeSettings Current { get; private set; } = ThemeSettings.Dark();
    public string CurrentFont { get; private set; } = "Segoe UI";

    public MaterialIconKind ThemeIconKind =>
      _isDark ? MaterialIconKind.WeatherNight : MaterialIconKind.WhiteBalanceSunny;

    public ThemeViewModel()
    {
      ApplyTheme();
    }

    private static readonly Uri DarkThemeUri = new(
      "/osu-mate;component/Resources/DarkTheme.xaml",
      UriKind.Relative
    );
    private static readonly Uri LightThemeUri = new(
      "/osu-mate;component/Resources/LightTheme.xaml",
      UriKind.Relative
    );

    private void ApplyTheme()
    {
      var themeUri = _isDark ? DarkThemeUri : LightThemeUri;
      Application.Current.Dispatcher.Invoke(() =>
      {
        Application.Current.Resources.MergedDictionaries[0] = new ResourceDictionary
        {
          Source = themeUri,
        };
      });
      OnPropertyChanged(nameof(ThemeIconKind));
    }

    public void Toggle()
    {
      _isDark = !_isDark;

      Current = (_isDark ? ThemeSettings.Dark() : ThemeSettings.Light()).WithFont(CurrentFont);
      ApplyTheme();
      OnPropertyChanged(nameof(IsDark));
      OnPropertyChanged(nameof(Current));
    }

    public void SetFont(string fontFamily)
    {
      CurrentFont = fontFamily;
      var resolvedFont = AppFonts.Resolve(fontFamily);
      Application.Current.Dispatcher.Invoke(() =>
      {
        Application.Current.Resources["AppFont"] = resolvedFont;
      });
      Current = Current.WithFont(fontFamily);
      OnPropertyChanged(nameof(CurrentFont));
      OnPropertyChanged(nameof(Current));
    }
  }
}
