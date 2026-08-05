using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace OsuMate.Utils
{
    /// <summary>
    /// InGameOverlayWindow の Value TextBlock 用。Value が行内で先頭列か末尾列かによって、
    /// 数字をどちら寄せにするかを切り替える。
    ///
    /// IsShowValueFirst = false（既定, Label→Valueの並び順）: Value は末尾列。
    ///   右寄せにすることで、桁数が変わっても行の右端が固定され続ける。
    /// IsShowValueFirst = true（Value→Labelの並び順）: Value は先頭列。
    ///   左寄せにすることで、桁数が変わっても行の左端が固定され続ける。
    /// </summary>
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
