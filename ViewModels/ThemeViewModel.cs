using System.Windows;
using System.Windows.Media;
using Material.Icons;
using OsuMate.Models;
using OsuMate.Utils;
using OxyPlot;

namespace OsuMate.ViewModels
{
  public class ThemeViewModel : ObservableBase
  {
    private bool _isDark = true;
    public bool IsDark => _isDark;
    public ThemeSettings Current { get; private set; } = ThemeSettings.Dark();
    public string CurrentFont { get; private set; } = "Segoe UI";
    public double CurrentHue { get; private set; } = 200;

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
      if (Application.Current?.Dispatcher != null)
      {
        Application.Current.Dispatcher.Invoke(() =>
        {
          Application.Current.Resources.MergedDictionaries[0] = new ResourceDictionary
          {
            Source = themeUri,
          };
          UpdateDynamicBrushes();
        });
      }
      OnPropertyChanged(nameof(ThemeIconKind));
    }

    private void UpdateDynamicBrushes()
    {
      if (Application.Current == null)
        return;

      double chromaSurface = 2.0;
      double chromaAccent = _isDark ? 42.0 : 45.0;

      double t0 = _isDark ? 2.4 : 96.0;
      double t1 = _isDark ? 4.9 : 100.0;
      double t2 = _isDark ? 10.0 : 92.0;
      double tAccent = _isDark ? 89.0 : 66.0;

      double chroma1 = _isDark ? chromaSurface : 0.0;

      var color0 = HctUtils.ToColor(CurrentHue, chromaSurface, t0);
      var color1 = HctUtils.ToColor(CurrentHue, chroma1, t1);
      var color2 = HctUtils.ToColor(CurrentHue, chromaSurface, t2);
      var colorAccent = HctUtils.ToColor(CurrentHue, chromaAccent, tAccent);

      var colorAccentSubtle = Color.FromArgb(0x1F, colorAccent.R, colorAccent.G, colorAccent.B);
      var colorAccentOn = _isDark ? color2 : Color.FromRgb(0x0F, 0x11, 0x11);

      var b0 = new SolidColorBrush(color0);
      b0.Freeze();
      var b1 = new SolidColorBrush(color1);
      b1.Freeze();
      var b2 = new SolidColorBrush(color2);
      b2.Freeze();
      var bAccent = new SolidColorBrush(colorAccent);
      bAccent.Freeze();
      var bAccentSubtle = new SolidColorBrush(colorAccentSubtle);
      bAccentSubtle.Freeze();
      var bAccentOn = new SolidColorBrush(colorAccentOn);
      bAccentOn.Freeze();

      Application.Current.Resources["SurfaceLevel0Brush"] = b0;
      Application.Current.Resources["SurfaceLevel1Brush"] = b1;
      Application.Current.Resources["SurfaceLevel2Brush"] = b2;
      Application.Current.Resources["AccentBrush"] = bAccent;
      Application.Current.Resources["AccentSubtleBrush"] = bAccentSubtle;
      Application.Current.Resources["AccentOnBrush"] = bAccentOn;

      Current = Current.WithAccent(OxyColor.FromRgb(colorAccent.R, colorAccent.G, colorAccent.B));
    }

    public void SetHue(double hue)
    {
      CurrentHue = hue;
      if (Application.Current?.Dispatcher != null)
      {
        if (Application.Current.Dispatcher.CheckAccess())
          UpdateDynamicBrushes();
        else
          Application.Current.Dispatcher.Invoke(UpdateDynamicBrushes);
      }
      OnPropertyChanged(nameof(CurrentHue));
      OnPropertyChanged(nameof(Current));
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
