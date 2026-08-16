using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OsuMate.Utils;
using OsuMate.ViewModels;
using OsuMate.Views.Behaviors;
using OsuMate.Views.Controls;

namespace OsuMate.Views
{
  public partial class URBarWindow : Window, INativeResizeHost
  {
    private URBarViewModel _vm = null!;
    private bool _isDraggable = false;
    public bool IsDragging { get; private set; } = false;
    public bool IsResizing { get; private set; } = false;
    public event Action<double, double>? PositionChanged;
    public event Action<double, double>? OnSizeChanged;

    private Point _dragStartMouse;
    private double _dragStartLeft;
    private double _dragStartTop;

    private int _rotation = 0;
    private double _baseWidth;
    private double _baseHeight;

    private readonly ScaleTransform _resizePreviewScale = new(1, 1);
    private double _resizeStartVisualWidth;
    private double _resizeStartVisualHeight;

    private URBarRenderer _renderer = null!;

    private double _valueLabelFontSize = 1;

    public URBarWindow()
    {
      InitializeComponent();

      _renderer = new URBarRenderer(BarsCanvas);

      BarsCanvas.RenderTransformOrigin = new Point(0, 0);
      BarsCanvas.RenderTransform = _resizePreviewScale;

      SizeChanged += URBarWindow_SizeChanged;
      LocationChanged += URBarWindow_LocationChanged;
    }

    private void URBarWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
      if (!IsResizing)
        return;
      bool sideways = _rotation == 90 || _rotation == 270;
      _baseWidth = sideways ? e.NewSize.Height : e.NewSize.Width;
      _baseHeight = sideways ? e.NewSize.Width : e.NewSize.Height;
      OnSizeChanged?.Invoke(_baseWidth, _baseHeight);

      if (_resizeStartVisualWidth > 0)
        _resizePreviewScale.ScaleX = e.NewSize.Width / _resizeStartVisualWidth;
      if (_resizeStartVisualHeight > 0)
        _resizePreviewScale.ScaleY = e.NewSize.Height / _resizeStartVisualHeight;
    }

    private void URBarWindow_LocationChanged(object? sender, EventArgs e)
    {
      if (!IsResizing)
        return;
      PositionChanged?.Invoke(Left, Top);
    }

    public void SetViewModel(URBarViewModel vm)
    {
      _vm = vm;

      CompositionTarget.Rendering += OnRendering;
    }

    public void UpdateAnimationSettings(double avgLineFollowStrength, double avgLineAnimMs)
    {
      _renderer.AvgLineFollowStrength = avgLineFollowStrength;
      _renderer.AvgLineAnimDuration = TimeSpan.FromMilliseconds(avgLineAnimMs);
    }

    public void UpdateOpacitySettings(
      double labelOpacity,
      double segmentOpacity,
      double markerOpacity,
      double hitErrorOpacity
    )
    {
      _renderer.LabelOpacity = labelOpacity;
      _renderer.SegmentOpacity = segmentOpacity;
      _renderer.MarkerOpacity = markerOpacity;
      _renderer.HitErrorOpacity = hitErrorOpacity;

      double clampedLabelOpacity = Math.Clamp(labelOpacity, 0, 1);
      LabelEarlyH.Opacity = clampedLabelOpacity;
      LabelLateH.Opacity = clampedLabelOpacity;
      LabelEarlyV.Opacity = clampedLabelOpacity;
      LabelLateV.Opacity = clampedLabelOpacity;

      Render();
    }

    private void OnRendering(object? sender, EventArgs e)
    {
      if (!IsLoaded || !IsVisible)
        return;
      if (_vm != null && _vm.ConsumeIsDirty())
      {
        Render();
      }
    }

    protected override void OnClosed(EventArgs e)
    {
      CompositionTarget.Rendering -= OnRendering;
      base.OnClosed(e);
    }

    public void SetSettingsMode(bool enabled, double width, double height)
    {
      _isDraggable = enabled;
      this.SetClickThrough(!enabled);
      OuterBorder.BorderThickness = enabled ? new Thickness(1) : new Thickness(0);
      BackgroundBorder.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
      ResizeRight.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
      ResizeLeft.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
      ResizeBottom.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
      ResizeTop.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
      ResizeCorner.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
      ResizeCornerTopLeft.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
      ResizeCornerTopRight.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
      ResizeCornerBottomLeft.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;

      _baseWidth = width;
      _baseHeight = height;
      ApplyWindowSize();

      UpdateLayout();
      UpdateLabels();
      Render();
    }

    public void SetRotation(int degrees)
    {
      _rotation = ((degrees % 360) + 360) % 360;
      BarsCanvas.LayoutTransform = Transform.Identity;
      ApplyWindowSize();

      UpdateLayout();
      UpdateLabels();
      Render();
    }

    private void ApplyWindowSize()
    {
      bool sideways = _rotation == 90 || _rotation == 270;
      Width = sideways ? _baseHeight : _baseWidth;
      Height = sideways ? _baseWidth : _baseHeight;
    }

    private void UpdateLabels()
    {
      bool sideways = _rotation == 90 || _rotation == 270;
      bool flipped = _rotation == 180 || _rotation == 270;

      LabelEarlyH.Visibility = !sideways ? Visibility.Visible : Visibility.Collapsed;
      LabelLateH.Visibility = !sideways ? Visibility.Visible : Visibility.Collapsed;
      LabelEarlyV.Visibility = sideways ? Visibility.Visible : Visibility.Collapsed;
      LabelLateV.Visibility = sideways ? Visibility.Visible : Visibility.Collapsed;

      LabelEarlyH.Text = flipped ? "LATE" : "EARLY";
      LabelLateH.Text = flipped ? "EARLY" : "LATE";
      LabelEarlyV.Text = flipped ? "LATE" : "EARLY";
      LabelLateV.Text = flipped ? "EARLY" : "LATE";

      double longSide = Math.Max(Width, Height);
      double shortSide = Math.Min(Width, Height);

      double fontSize = Math.Max(1, Math.Min(longSide * 0.04, shortSide * 0.3));
      LabelEarlyH.FontSize =
        LabelLateH.FontSize =
        LabelEarlyV.FontSize =
        LabelLateV.FontSize =
          fontSize;

      _valueLabelFontSize = Math.Max(1, Math.Min(longSide * 0.033, shortSide * 0.25));
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
      if (!_isDraggable)
        return;
      if (
        e.OriginalSource is Border b
        && (
          b == ResizeRight
          || b == ResizeLeft
          || b == ResizeBottom
          || b == ResizeTop
          || b == ResizeCorner
          || b == ResizeCornerTopLeft
          || b == ResizeCornerTopRight
          || b == ResizeCornerBottomLeft
        )
      )
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

    public void OnNativeResizeStarted()
    {
      IsResizing = true;

      _resizeStartVisualWidth = Width;
      _resizeStartVisualHeight = Height;
      _resizePreviewScale.ScaleX = 1;
      _resizePreviewScale.ScaleY = 1;
    }

    public void OnNativeResizeCompleted()
    {
      IsResizing = false;

      _resizePreviewScale.ScaleX = 1;
      _resizePreviewScale.ScaleY = 1;
      UpdateLayout();
      UpdateLabels();
      Render();

      PositionChanged?.Invoke(Left, Top);
    }

    private void Render()
    {
      if (!IsLoaded)
        return;

      UpdateLabels();
      _renderer.Render(_vm, _rotation, _valueLabelFontSize);
    }
  }
}
