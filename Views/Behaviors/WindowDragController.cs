using System.Windows;
using System.Windows.Input;

namespace OsuMate.Views.Behaviors
{
  internal sealed class WindowDragController(Window window, Action<double, double> onCompleted)
  {
    private Point _startMouse;
    private double _startLeft;
    private double _startTop;

    public bool IsDragging { get; private set; }

    public void Begin(MouseButtonEventArgs e)
    {
      _startMouse = window.PointToScreen(e.GetPosition(window));
      _startLeft = window.Left;
      _startTop = window.Top;
      IsDragging = true;
      window.CaptureMouse();
      e.Handled = true;
    }

    public void Move(MouseEventArgs e)
    {
      if (!IsDragging)
        return;

      var current = window.PointToScreen(e.GetPosition(window));
      window.Left = _startLeft + (current.X - _startMouse.X);
      window.Top = _startTop + (current.Y - _startMouse.Y);
    }

    public void End()
    {
      if (!IsDragging)
        return;

      IsDragging = false;
      window.ReleaseMouseCapture();
      onCompleted(window.Left, window.Top);
    }
  }
}
