using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using OsuMate.Utils;
using OsuMate.ViewModels;

namespace OsuMate.Views
{
  public partial class InGameOverlayWindow : Window
  {
    private bool _isDraggable = false;
    public bool IsDragging { get; private set; } = false;
    public event Action<double, double>? PositionChanged;

    private Point _dragStartMouse;
    private double _dragStartLeft;
    private double _dragStartTop;

    public InGameOverlayWindow(InGameOverlayViewModel vm)
    {
      InitializeComponent();
      DataContext = vm;
    }

    public void SetDraggable(bool draggable)
    {
      _isDraggable = draggable;
      OuterBorder.BorderThickness = draggable ? new Thickness(1) : new Thickness(0);
      BackgroundBorder.Visibility = draggable ? Visibility.Visible : Visibility.Collapsed;
      OuterBorder.BorderBrush = draggable
        ? new SolidColorBrush(Colors.White)
        : new SolidColorBrush(Colors.Transparent);
      this.SetClickThrough(!draggable);
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
      if (!_isDraggable)
        return;
      _dragStartMouse = PointToScreen(e.GetPosition(this));
      _dragStartLeft = Left;
      _dragStartTop = Top;
      IsDragging = true;
      CaptureMouse();
      e.Handled = true;
    }

    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
      if (!IsDragging)
        return;
      var current = PointToScreen(e.GetPosition(this));
      Left = _dragStartLeft + (current.X - _dragStartMouse.X);
      Top = _dragStartTop + (current.Y - _dragStartMouse.Y);
    }

    private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
      if (!IsDragging)
        return;
      IsDragging = false;
      ReleaseMouseCapture();
      PositionChanged?.Invoke(Left, Top);
    }
  }
}
