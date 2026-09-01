using System;
using System.Globalization;
using System.Windows.Data;
using OsuMate.Models;

namespace OsuMate.Views.Controls
{
  public sealed class PlayLogHitsValueConverter : IValueConverter
  {
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
      if (value is not PlayLogEntry e)
        return "-";
      return $"{e.CountGeki} / {e.Count300} / {e.CountKatu} / {e.Count100} / {e.Count50} / {e.CountMiss}";
    }

    public object ConvertBack(
      object value,
      Type targetType,
      object parameter,
      CultureInfo culture
    ) => throw new NotSupportedException();
  }

  public sealed class ModsDisplayValueConverter : IValueConverter
  {
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
      if (value is not string s)
        return "";
      if (string.IsNullOrEmpty(s) || s == "NM")
        return "";
      return s.Replace(",", ", ");
    }

    public object ConvertBack(
      object value,
      Type targetType,
      object parameter,
      CultureInfo culture
    ) => throw new NotSupportedException();
  }

  public sealed class NullableDoubleValueConverter : IValueConverter
  {
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
      if (value is null)
        return "-";
      double d = System.Convert.ToDouble(value);
      string fmt = parameter as string ?? "F2";
      return d.ToString(fmt, CultureInfo.InvariantCulture);
    }

    public object ConvertBack(
      object value,
      Type targetType,
      object parameter,
      CultureInfo culture
    ) => throw new NotSupportedException();
  }

  public sealed class BoolToStatusValueConverter : IValueConverter
  {
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
      value is bool b && b ? "✓ Done" : "✗ Quit";

    public object ConvertBack(
      object value,
      Type targetType,
      object parameter,
      CultureInfo culture
    ) => throw new NotSupportedException();
  }
}
