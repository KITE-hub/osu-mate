using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using OsuMate.Models;
using OsuMate.Utils;
using OsuMate.ViewModels;
using OsuMate.Views.Controls;

namespace OsuMate.Views
{
  public partial class KeyOverlayWindow : Window
  {
    private readonly KeyOverlayViewModel _vm;
    private readonly KeyOverlayRenderer _renderer;
    private readonly DispatcherTimer _renderTimer;
    private readonly List<KeyOverlayTransition> _transitionBuffer = [];
    private const int DefaultRenderIntervalMs = 33;
    private bool _isDraggable;
    private int _rotation;
    private double _flowLength = 700;
    private int _laneCount = -1;

    public bool IsDragging { get; private set; }
    public bool IsResizing { get; private set; }
    public event Action<double, double>? PositionChanged;
    public event Action<double>? FlowLengthChanged;

    private Point _dragStartMouse;
    private double _dragStartLeft;
    private double _dragStartTop;
    private Point _resizeStartMouse;
    private double _resizeStartLength;

    public KeyOverlayWindow(KeyOverlayViewModel vm)
    {
      InitializeComponent();
      _vm = vm;
      _renderer = new KeyOverlayRenderer(BarsCanvas);
      _renderTimer = new DispatcherTimer(DispatcherPriority.Render)
      {
        Interval = TimeSpan.FromMilliseconds(DefaultRenderIntervalMs),
      };
      _renderTimer.Tick += OnRenderTick;
      _renderTimer.Start();
    }

    public void SetDraggable(bool draggable)
    {
      _isDraggable = draggable;
      OuterBorder.BorderThickness = draggable ? new Thickness(1) : new Thickness(0);
      BackgroundBorder.Visibility = draggable ? Visibility.Visible : Visibility.Collapsed;
      ResizeHandle.Visibility = draggable ? Visibility.Visible : Visibility.Collapsed;
      UpdateResizeHandle();
      this.SetClickThrough(!draggable);
    }

    public void UpdateSettings(
      int rotation,
      double flowLength,
      double speed,
      double round,
      double laneWidth,
      int renderIntervalMs
    )
    {
      _rotation = (int)Math.Round((((rotation % 360) + 360) % 360) / 90.0) * 90 % 360;
      _flowLength = Math.Max(120, flowLength);
      _renderer.UpdateSettings(_rotation, speed, round, laneWidth);
      _renderTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, renderIntervalMs));
      ApplySize(_vm.Layout.Keys.Length);
      UpdateResizeHandle();
    }

    private void OnRenderTick(object? sender, EventArgs e)
    {
      var layout = _vm.Layout;
      _transitionBuffer.Clear();
      _vm.DrainTransitions(_transitionBuffer);

      if (!IsLoaded || !IsVisible)
        return;

      if (_laneCount != layout.Keys.Length)
      {
        _laneCount = layout.Keys.Length;
        ApplySize(_laneCount);
      }
      _renderer.Render(layout, _transitionBuffer, Stopwatch.GetTimestamp());
    }

    private void ApplySize(int laneCount)
    {
      var size = _renderer.GetRequiredSize(Math.Max(1, laneCount), _flowLength);
      Width = size.Width;
      Height = size.Height;
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
      if (IsResizing)
      {
        var resizeCurrent = PointToScreen(e.GetPosition(this));
        var delta = _rotation switch
        {
          0 => resizeCurrent.Y - _resizeStartMouse.Y,
          180 => _resizeStartMouse.Y - resizeCurrent.Y,
          90 => resizeCurrent.X - _resizeStartMouse.X,
          _ => _resizeStartMouse.X - resizeCurrent.X,
        };
        _flowLength = Math.Max(120, _resizeStartLength + delta);
        ApplySize(_laneCount < 0 ? _vm.Layout.Keys.Length : _laneCount);
        return;
      }
      if (!IsDragging)
        return;

      var current = PointToScreen(e.GetPosition(this));
      Left = _dragStartLeft + current.X - _dragStartMouse.X;
      Top = _dragStartTop + current.Y - _dragStartMouse.Y;
    }

    private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
      if (!IsDragging)
        return;

      IsDragging = false;
      ReleaseMouseCapture();
      PositionChanged?.Invoke(Left, Top);
    }

    private void ResizeHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
      if (!_isDraggable)
        return;
      _resizeStartMouse = PointToScreen(e.GetPosition(this));
      _resizeStartLength = _flowLength;
      IsResizing = true;
      CaptureMouse();
      e.Handled = true;
    }

    private void ResizeHandle_MouseMove(object sender, MouseEventArgs e)
    {
      if (!IsResizing)
        return;
      Window_MouseMove(sender, e);
      e.Handled = true;
    }

    private void ResizeHandle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
      if (!IsResizing)
        return;
      IsResizing = false;
      ReleaseMouseCapture();
      FlowLengthChanged?.Invoke(_flowLength);
      e.Handled = true;
    }

    private void UpdateResizeHandle()
    {
      var alignment = _rotation switch
      {
        0 => (HorizontalAlignment.Right, VerticalAlignment.Bottom, Cursors.SizeNS),
        180 => (HorizontalAlignment.Left, VerticalAlignment.Top, Cursors.SizeNS),
        90 => (HorizontalAlignment.Right, VerticalAlignment.Bottom, Cursors.SizeWE),
        _ => (HorizontalAlignment.Left, VerticalAlignment.Top, Cursors.SizeWE),
      };
      ResizeHandle.HorizontalAlignment = alignment.Item1;
      ResizeHandle.VerticalAlignment = alignment.Item2;
      ResizeHandle.Cursor = alignment.Item3;
    }

    protected override void OnClosed(EventArgs e)
    {
      _renderTimer.Stop();
      _renderTimer.Tick -= OnRenderTick;
      base.OnClosed(e);
    }
  }
}
