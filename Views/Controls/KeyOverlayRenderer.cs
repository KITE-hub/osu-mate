using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using OsuMate.Models;

namespace OsuMate.Views.Controls;

internal sealed class KeyOverlayRenderer
{
  private sealed class BarState(Rectangle element)
  {
    public Rectangle Element { get; } = element;
    public bool IsHeld { get; set; }
  }

  private const double KeyLength = 44;
  private const double Gap = 4;
  private const double Margin = 4;
  private readonly Canvas _canvas;
  private readonly List<List<BarState>> _bars = [];
  private readonly List<Stack<Rectangle>> _freeBars = [];
  private readonly List<Rectangle> _keyBackgrounds = [];
  private readonly List<TextBlock> _keyLabels = [];
  private readonly List<bool?> _lastPressedState = [];
  private KeyOverlaySnapshot _layout = KeyOverlaySnapshot.Empty;
  private int _rotation;
  private double _speed = 600;
  private double _round = 4;
  private double _laneWidth = 64;
  private int _lastLayoutRotation = int.MinValue;
  private double _lastLayoutLaneWidth = double.NaN;
  private double _lastLayoutFlowLength = double.NaN;
  private int _styleVersion;
  private int _appliedStyleVersion = -1;

  private static readonly Brush KeyPressedBrush = CreateFrozenBrush(110, 255, 255, 255);
  private static readonly Brush KeyIdleBrush = CreateFrozenBrush(28, 255, 255, 255);
  private static readonly Brush LabelIdleBrush = CreateFrozenBrush(180, 255, 255, 255);
  private static readonly Brush BarBrush = CreateFrozenBrush(145, 255, 255, 255);
  private static readonly Brush BorderBrush = CreateFrozenBrush(180, 255, 255, 255);

  private static SolidColorBrush CreateFrozenBrush(byte a, byte r, byte g, byte b)
  {
    var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
    brush.Freeze();
    return brush;
  }

  public KeyOverlayRenderer(Canvas canvas) => _canvas = canvas;

  public void UpdateSettings(int rotation, double speed, double round, double laneWidth)
  {
    _rotation = ((rotation % 360) + 360) % 360;
    _speed = Math.Clamp(speed, 50, 3000);
    _round = Math.Clamp(round, 0, 32);
    _laneWidth = Math.Clamp(laneWidth, 24, 160);
    _styleVersion++;
  }

  public Size GetRequiredSize(int laneCount, double flowLength)
  {
    var crossLength = Margin * 2 + laneCount * _laneWidth + Math.Max(0, laneCount - 1) * Gap;
    return IsHorizontal ? new Size(flowLength, crossLength) : new Size(crossLength, flowLength);
  }

  public void Render(KeyOverlaySnapshot snapshot, double dt)
  {
    EnsureLayout(snapshot);
    if (snapshot.Keys.Length == 0)
      return;

    LayoutStaticElements();
    var styleChanged = _appliedStyleVersion != _styleVersion;
    var movement = _speed * dt;
    for (var i = 0; i < snapshot.Keys.Length; i++)
      RenderLane(i, snapshot.Keys[i].IsPressed, movement, styleChanged);
    if (styleChanged)
      _appliedStyleVersion = _styleVersion;
  }

  private bool IsHorizontal => _rotation is 90 or 270;
  private bool IsReversed => _rotation is 180 or 270;
  private double FlowLength => IsHorizontal ? _canvas.ActualWidth : _canvas.ActualHeight;

  private void RenderLane(int lane, bool isPressed, double movement, bool styleChanged)
  {
    var bars = _bars[lane];
    if (!isPressed && bars.Count == 0)
    {
      if (_lastPressedState[lane] != false)
      {
        _keyBackgrounds[lane].Fill = KeyIdleBrush;
        _keyLabels[lane].Foreground = LabelIdleBrush;
        _lastPressedState[lane] = false;
      }
      return;
    }

    var held = bars.Count > 0 && bars[^1].IsHeld;
    if (isPressed && !held)
    {
      var element = RentBar(lane);
      SetCross(element, lane);
      SetFlow(element, IsReversed ? FlowLength - KeyLength - Gap - 1 : KeyLength + Gap);
      SetFlowSize(element, 1);
      SetCrossSize(element, _laneWidth);
      element.RadiusX = _round;
      element.RadiusY = _round;
      bars.Add(new BarState(element) { IsHeld = true });
    }
    else if (!isPressed && held)
      bars[^1].IsHeld = false;

    if (_lastPressedState[lane] != isPressed)
    {
      _keyBackgrounds[lane].Fill = isPressed ? KeyPressedBrush : KeyIdleBrush;
      _keyLabels[lane].Foreground = isPressed ? Brushes.White : LabelIdleBrush;
      _lastPressedState[lane] = isPressed;
    }

    foreach (var bar in bars)
    {
      if (bar.IsHeld)
      {
        var size = GetFlowSize(bar.Element) + movement;
        SetFlowSize(bar.Element, Math.Max(1, size));
      }
      else
      {
        var flow = GetFlow(bar.Element) + (IsReversed ? -movement : movement);
        SetFlow(bar.Element, flow);
      }
      if (styleChanged)
      {
        SetCrossSize(bar.Element, _laneWidth);
        bar.Element.RadiusX = _round;
        bar.Element.RadiusY = _round;
      }
    }

    while (bars.Count > 0 && IsOutside(bars[0].Element))
    {
      ReturnBar(lane, bars[0].Element);
      bars.RemoveAt(0);
    }
  }

  private Rectangle RentBar(int lane)
  {
    var pool = _freeBars[lane];
    if (pool.Count > 0)
    {
      var reused = pool.Pop();
      reused.Visibility = Visibility.Visible;
      return reused;
    }

    var element = new Rectangle { Fill = BarBrush, RadiusX = _round, RadiusY = _round };
    _canvas.Children.Add(element);
    return element;
  }

  private void ReturnBar(int lane, Rectangle element)
  {
    element.Visibility = Visibility.Collapsed;
    _freeBars[lane].Push(element);
  }

  private bool IsOutside(Rectangle element) => IsReversed
    ? GetFlow(element) + GetFlowSize(element) < 0
    : GetFlow(element) > FlowLength;

  private void EnsureLayout(KeyOverlaySnapshot snapshot)
  {
    if (HasSameLabels(snapshot))
      return;

    _canvas.Children.Clear();
    _bars.Clear();
    _freeBars.Clear();
    _keyBackgrounds.Clear();
    _keyLabels.Clear();
    _lastPressedState.Clear();
    _lastLayoutRotation = int.MinValue;
    for (var i = 0; i < snapshot.Keys.Length; i++)
    {
      var background = new Rectangle
      {
        Stroke = BorderBrush,
        StrokeThickness = 1,
        RadiusX = 4,
        RadiusY = 4,
        CacheMode = new BitmapCache(),
      };
      var label = new TextBlock
      {
        Text = snapshot.Keys[i].Label,
        FontSize = 14,
        FontWeight = FontWeights.SemiBold,
        FontFamily = new FontFamily("pack://application:,,,/Resources/Fonts/Oxanium/#Oxanium"),
        TextAlignment = TextAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        CacheMode = new BitmapCache(),
      };
      _canvas.Children.Add(background);
      _canvas.Children.Add(label);
      _keyBackgrounds.Add(background);
      _keyLabels.Add(label);
      _bars.Add([]);
      _freeBars.Add(new Stack<Rectangle>());
      _lastPressedState.Add(null);
    }
    _layout = snapshot;
  }

  private bool HasSameLabels(KeyOverlaySnapshot snapshot)
  {
    if (_layout.Keys.Length != snapshot.Keys.Length)
      return false;
    for (var i = 0; i < snapshot.Keys.Length; i++)
    {
      if (!string.Equals(_layout.Keys[i].Label, snapshot.Keys[i].Label, StringComparison.Ordinal))
        return false;
    }
    return true;
  }

  private void LayoutStaticElements()
  {
    var flowLength = FlowLength;
    if (_lastLayoutRotation == _rotation && _lastLayoutLaneWidth == _laneWidth && _lastLayoutFlowLength == flowLength)
      return;

    for (var i = 0; i < _keyBackgrounds.Count; i++)
    {
      var flow = IsReversed ? Math.Max(0, flowLength - KeyLength) : 0;
      SetCross(_keyBackgrounds[i], i);
      SetCross(_keyLabels[i], i);
      SetFlow(_keyBackgrounds[i], flow);
      SetFlow(_keyLabels[i], flow);
      SetCrossSize(_keyBackgrounds[i], _laneWidth);
      SetCrossSize(_keyLabels[i], _laneWidth);
      SetFlowSize(_keyBackgrounds[i], KeyLength);
      SetFlowSize(_keyLabels[i], KeyLength);
    }

    _lastLayoutRotation = _rotation;
    _lastLayoutLaneWidth = _laneWidth;
    _lastLayoutFlowLength = flowLength;
  }

  private double CrossPosition(int lane) => Margin + lane * (_laneWidth + Gap);
  private void SetCross(FrameworkElement element, int lane)
  {
    if (IsHorizontal)
      Canvas.SetTop(element, CrossPosition(lane));
    else
      Canvas.SetLeft(element, CrossPosition(lane));
  }

  private void SetFlow(FrameworkElement element, double value)
  {
    if (IsHorizontal)
      Canvas.SetLeft(element, value);
    else
      Canvas.SetTop(element, value);
  }

  private double GetFlow(FrameworkElement element) => IsHorizontal ? Canvas.GetLeft(element) : Canvas.GetTop(element);
  private void SetCrossSize(FrameworkElement element, double value)
  {
    if (IsHorizontal)
      element.Height = value;
    else
      element.Width = value;
  }

  private void SetFlowSize(FrameworkElement element, double value)
  {
    if (IsHorizontal)
      element.Width = value;
    else
      element.Height = value;
  }

  private double GetFlowSize(FrameworkElement element) => IsHorizontal ? element.Width : element.Height;
}
