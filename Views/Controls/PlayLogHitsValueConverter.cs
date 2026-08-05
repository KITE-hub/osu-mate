using System;
using System.Globalization;
using System.Windows.Data;
using OsuMate.Models;

namespace OsuMate.Views.Controls
{
    /// <summary>PlayLogEntry → "MAX / 300 / 200 / 100 / 50 / Miss" 形式の文字列に変換。</summary>
    public sealed class PlayLogHitsValueConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not PlayLogEntry e) return "-";
            return $"{e.CountGeki} / {e.Count300} / {e.CountKatu} / {e.Count100} / {e.Count50} / {e.CountMiss}";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>ModsString → NM を空文字に、カンマの後にスペースを追加して表示。</summary>
    public sealed class ModsDisplayValueConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not string s) return "";
            if (string.IsNullOrEmpty(s) || s == "NM") return "";
            return s.Replace(",", ", ");
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>nullable double → フォーマット済み文字列。null のときは "-" を返す。</summary>
    public sealed class NullableDoubleValueConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is null) return "-";
            double d = System.Convert.ToDouble(value);
            string fmt = parameter as string ?? "F2";
            return d.ToString(fmt, CultureInfo.InvariantCulture);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>bool → "✓ Done" / "✗ Quit" に変換。</summary>
    public sealed class BoolToStatusValueConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b && b ? "✓ Done" : "✗ Quit";

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
