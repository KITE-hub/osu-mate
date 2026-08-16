using System.Globalization;
using System.Windows.Data;

namespace OsuMate.Views.Controls
{
  [ValueConversion(typeof(DateOnly), typeof(string))]
  public sealed class MonthDateOnlyConverter : IValueConverter
  {
    public static readonly MonthDateOnlyConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
      if (value is DateOnly d)
        return d.ToString("MMM yyyy", CultureInfo.InvariantCulture);
      return string.Empty;
    }

    public object ConvertBack(
      object value,
      Type targetType,
      object parameter,
      CultureInfo culture
    ) => throw new NotSupportedException();
  }
}
