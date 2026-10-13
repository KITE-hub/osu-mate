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
    public bool IsDragging => _drag.IsDragging;
    public event Action<double, double>? PositionChanged;

    private readonly Behaviors.WindowDragController _drag;

    public InGameOverlayWindow(InGameOverlayViewModel vm)
    {
      InitializeComponent();
      _drag = new Behaviors.WindowDragController(this, (l, t) => PositionChanged?.Invoke(l, t));
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
      _drag.Begin(e);
    }

    private void Window_MouseMove(object sender, MouseEventArgs e) => _drag.Move(e);

    private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => _drag.End();
  }
}
