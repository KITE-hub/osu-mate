using System;
using System.Globalization;
using System.Windows.Data;

namespace OsuMate.Utils
{
    /// <summary>
    /// InGameOverlayWindow の「Show Value First」設定
    /// IsShowValueFirst = false（既定）: Label→Value の並び順（Label列=0, Value列=2）。
    /// IsShowValueFirst = true: Value→Label の並び順（Value列=0, Label列=2）。
    /// </summary>
    public class BoolToColumnIndexConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isShowValueFirst = value is bool b && b;
            bool isLabelColumn = string.Equals(parameter as string, "Label", StringComparison.OrdinalIgnoreCase);

            if (isLabelColumn)
                return isShowValueFirst ? 2 : 0;
            else
                return isShowValueFirst ? 0 : 2;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
