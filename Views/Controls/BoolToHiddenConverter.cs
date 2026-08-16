using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace OsuMate.Views.Controls
{
  [ValueConversion(typeof(bool), typeof(Visibility))]
  public sealed class BoolToHiddenConverter : IValueConverter
  {
    public static readonly BoolToHiddenConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
      value is true ? Visibility.Visible : Visibility.Hidden;

    public object ConvertBack(
      object value,
      Type targetType,
      object parameter,
      CultureInfo culture
    ) => throw new NotSupportedException();
  }
}
