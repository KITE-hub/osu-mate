using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace OsuMate.Utils
{
  public static class ComboBoxWheelBehavior
  {
    public static void Register()
    {
      EventManager.RegisterClassHandler(
        typeof(ComboBox),
        UIElement.PreviewMouseWheelEvent,
        new MouseWheelEventHandler(OnComboBoxPreviewMouseWheel)
      );
    }

    private static void OnComboBoxPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
      if (sender is not ComboBox comboBox)
        return;
      if (comboBox.IsDropDownOpen)
        return;

      e.Handled = true;

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
