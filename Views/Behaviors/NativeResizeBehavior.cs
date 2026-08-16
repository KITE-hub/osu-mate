using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace OsuMate.Views.Behaviors
{
  public enum NativeResizeDirection
  {
    Left = 1,
    Right = 2,
    Top = 3,
    TopLeft = 4,
    TopRight = 5,
    Bottom = 6,
    BottomLeft = 7,
    BottomRight = 8,
  }

  public interface INativeResizeHost
  {
    void OnNativeResizeStarted();

    void OnNativeResizeCompleted();
  }

  public static class NativeResizeBehavior
  {
    private const int WM_SYSCOMMAND = 0x112;
    private const int SC_SIZE = 0xF000;

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    public static readonly DependencyProperty DirectionProperty =
      DependencyProperty.RegisterAttached(
        "Direction",
        typeof(NativeResizeDirection?),
        typeof(NativeResizeBehavior),
        new PropertyMetadata(null, OnDirectionChanged)
      );

    public static void SetDirection(UIElement element, NativeResizeDirection? value) =>
      element.SetValue(DirectionProperty, value);

    public static NativeResizeDirection? GetDirection(UIElement element) =>
      (NativeResizeDirection?)element.GetValue(DirectionProperty);

    private static void OnDirectionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
      if (d is not UIElement element)
        return;

      element.MouseLeftButtonDown -= OnMouseLeftButtonDown;
      if (e.NewValue != null)
        element.MouseLeftButtonDown += OnMouseLeftButtonDown;
    }

    private static void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
      if (sender is not UIElement element)
        return;
      var direction = GetDirection(element);
      if (direction == null)
        return;

      var window = Window.GetWindow(element);
      if (window == null)
        return;

      e.Handled = true;
      BeginResize(window, direction.Value);
    }

    public static void BeginResize(Window window, NativeResizeDirection direction)
    {
      var host = window as INativeResizeHost;
      host?.OnNativeResizeStarted();

      ReleaseCapture();

      var handle = new WindowInteropHelper(window).Handle;
      SendMessage(handle, WM_SYSCOMMAND, (IntPtr)(SC_SIZE + (int)direction), IntPtr.Zero);

      host?.OnNativeResizeCompleted();
    }
  }
}
