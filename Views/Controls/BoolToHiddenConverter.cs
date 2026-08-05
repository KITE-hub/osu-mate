using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace OsuMate.Views.Controls
{
    /// <summary>
    /// bool を Visibility.Visible / Visibility.Hidden に変換するコンバーター。
    /// 標準の <see cref="System.Windows.Controls.BooleanToVisibilityConverter"/> は
    /// false を Collapsed（レイアウト上の領域も消える）にしか変換できない。
    /// 月ナビゲーション行（前月/次月ボタン）のように「非表示にはしたいが、
    /// ボタンが消えることで残りの要素（月選択ComboBoxなど）の中央位置がずれてほしくない」
    /// 場合は、領域だけ確保して見た目とヒットテストだけ消す Hidden を使う必要がある。
    /// </summary>
    [ValueConversion(typeof(bool), typeof(Visibility))]
    public sealed class BoolToHiddenConverter : IValueConverter
    {
        /// <summary>XAML の x:Static で参照するためのシングルトンインスタンス。</summary>
        public static readonly BoolToHiddenConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is true ? Visibility.Visible : Visibility.Hidden;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
