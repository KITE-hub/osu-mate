using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace OsuMate.Utils
{
  public class ShowValueFirstToTextAlignmentConverter : IValueConverter
  {
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
      return value is bool b && b ? TextAlignment.Left : TextAlignment.Right;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
      throw new NotSupportedException();
    }
  }
}
