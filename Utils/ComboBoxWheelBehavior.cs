using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace OsuMate.Utils
{
    /// <summary>
    /// ComboBoxが閉じている状態でマウスホイールを回すと選択候補が切り替わる
    /// WPF標準（既定）の挙動を無効化してスクロールを継続するグローバルビヘイビア
    /// </summary>
    public static class ComboBoxWheelBehavior
    {
        public static void Register()
        {
            EventManager.RegisterClassHandler(
                typeof(ComboBox),
                UIElement.PreviewMouseWheelEvent,
                new MouseWheelEventHandler(OnComboBoxPreviewMouseWheel));
        }

        private static void OnComboBoxPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is not ComboBox comboBox) return;
            if (comboBox.IsDropDownOpen) return;

            // ComboBox自身の「選択候補切り替え」は握り潰す
            e.Handled = true;

            // 外側（親のScrollViewer等）へは、あらためてホイール操作を伝える。
            // 新しいRoutedEventArgsとしてMouseWheelEvent（バブル）を親からRaiseすることで、
            // このComboBoxの位置で本来届くはずだった「ページ全体のスクロール」を復元する。
            if (VisualTreeHelper.GetParent(comboBox) is UIElement parent)
            {
                var forwardedArgs = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
                {
                    RoutedEvent = UIElement.MouseWheelEvent,
                    Source = comboBox,
                };
                parent.RaiseEvent(forwardedArgs);
            }
        }
    }
}
