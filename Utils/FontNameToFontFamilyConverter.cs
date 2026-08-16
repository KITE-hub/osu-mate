using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace OsuMate.Utils
{
  public class FontNameToFontFamilyConverter : IValueConverter
  {
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
      if (value is string fontName)
        return AppFonts.Resolve(fontName);

      return Binding.DoNothing;
    }

    public object ConvertBack(
      object value,
      Type targetType,
      object parameter,
      CultureInfo culture
    ) => throw new NotSupportedException();
  }
}
