using System.Globalization;
using System.Windows.Data;

namespace OsuMate.Views.Controls
{
    /// <summary>
    /// DateOnly（月の1日を表す値）を "MMM yyyy" 形式の英語文字列（例: "Jul 2026"）に変換するコンバーター(ComboBoxのWidthを抑えるため、月名を3文字略記)
    /// </summary>
    [ValueConversion(typeof(DateOnly), typeof(string))]
    public sealed class MonthDateOnlyConverter : IValueConverter
    {
        /// <summary>XAML の x:Static で参照するためのシングルトンインスタンス。</summary>
        public static readonly MonthDateOnlyConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is DateOnly d)
                return d.ToString("MMM yyyy", CultureInfo.InvariantCulture);
            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
