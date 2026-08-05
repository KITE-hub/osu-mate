using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace OsuMate.Utils
{
    /// <summary>
    /// フォント名（文字列）を、実際にプレビュー表示できる <see cref="FontFamily"/> に変換する。
    /// SettingsView のフォント選択ComboBoxで、埋め込みフォント（Oxanium/Roboto）を
    /// 一覧上でも実際のグリフでプレビューできるようにするために使用する。
    /// </summary>
    public class FontNameToFontFamilyConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string fontName)
                return AppFonts.Resolve(fontName);

            return Binding.DoNothing;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
