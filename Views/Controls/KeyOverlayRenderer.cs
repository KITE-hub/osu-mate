using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OsuMate.Models;

namespace OsuMate.Views.Controls;

internal sealed class KeyOverlayRenderer
{
  private sealed class VisualHost : FrameworkElement
  {
    private readonly DrawingVisual _visual = new();

    public VisualHost()
    {
      IsHitTestVisible = false;
      AddVisualChild(_visual);
    }

    protected override int VisualChildrenCount => 1;
    protected override Visual GetVisualChild(int index) =>
      index == 0 ? _visual : throw new ArgumentOutOfRangeException(nameof(index));

    public DrawingContext RenderOpen() => _visual.RenderOpen();
  }

  private sealed class BarState
  {
    public bool IsHeld { get; set; }
    public long OpenTicks { get; set; }
    public long CloseTicks { get; set; }
    public double ClosedLength { get; set; }
  }

  private const double KeyLength = 44;
  private const double Gap = 4;
  private const double Margin = 4;
  private readonly Canvas _canvas;
  private readonly VisualHost _host = new();
  private readonly Stack<BarState> _barPool = new();
  private readonly List<List<BarState>> _bars = [];
  private readonly List<List<KeyOverlayTransition>> _laneEventBuckets = [];
  private readonly List<FormattedText> _idleLabels = [];
  private readonly List<FormattedText> _pressedLabels = [];
  private KeyOverlaySnapshot _layout = KeyOverlaySnapshot.Empty;
  private double _lastDpi = double.NaN;
  private int _rotation;
  private double _speed = 600;
  private double _round = 4;
  private double _laneWidth = 64;

  private static readonly Brush KeyPressedBrush = CreateFrozenBrush(110, 255, 255, 255);
  private static readonly Brush KeyIdleBrush = CreateFrozenBrush(28, 255, 255, 255);
  private static readonly Brush LabelIdleBrush = CreateFrozenBrush(180, 255, 255, 255);
  private static readonly Brush BarBrush = CreateFrozenBrush(145, 255, 255, 255);
  private static readonly Brush BorderBrush = CreateFrozenBrush(180, 255, 255, 255);
  private static readonly Pen BorderPen = CreateFrozenPen(BorderBrush, 1);
  private static readonly Typeface KeyTypeface = new(
    new FontFamily("pack://application:,,,/Resources/Fonts/Oxanium/#Oxanium"),
    FontStyles.Normal,
    FontWeights.SemiBold,
    FontStretches.Normal
  );

  private static SolidColorBrush CreateFrozenBrush(byte a, byte r, byte g, byte b)
  {
    var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
    brush.Freeze();
    return brush;
  }

  private static Pen CreateFrozenPen(Brush brush, double thickness)
  {
    var pen = new Pen(brush, thickness);
    pen.Freeze();
    return pen;
  }

  public KeyOverlayRenderer(Canvas canvas)
  {
    _canvas = canvas;
    _canvas.Children.Clear();
    _canvas.Children.Add(_host);
  }

  public void UpdateSettings(int rotation, double speed, double round, double laneWidth)
  {
    _rotation = ((rotation % 360) + 360) % 360;
    _speed = Math.Clamp(speed, 50, 3000);
    _round = Math.Clamp(round, 0, 32);
    _laneWidth = Math.Clamp(laneWidth, 24, 160);
  }

  public Size GetRequiredSize(int laneCount, double flowLength)
  {
    var crossLength = Margin * 2 + laneCount * _laneWidth + Math.Max(0, laneCount - 1) * Gap;
    return IsHorizontal ? new Size(flowLength, crossLength) : new Size(crossLength, flowLength);
  }

  public void Render(KeyOverlaySnapshot snapshot, IReadOnlyList<KeyOverlayTransition> transitions, long nowTicks, double dpi)
  {
    EnsureLayout(snapshot, dpi);
    if (snapshot.Keys.Length == 0)
      return;

    var flowLength = FlowLength;
    if (flowLength <= 0)
      return;

    EnsureEventBuckets(snapshot.Keys.Length);
    foreach (var bucket in _laneEventBuckets)
      bucket.Clear();
    foreach (var transition in transitions)
    {
      if ((uint)transition.LaneIndex < (uint)_laneEventBuckets.Count)
        _laneEventBuckets[transition.LaneIndex].Add(transition);
    }

    var spawnFlow = SpawnFlow;

    using var dc = _host.RenderOpen();

    for (var lane = 0; lane < snapshot.Keys.Length; lane++)
      RenderLaneBars(dc, lane, snapshot.Keys[lane].IsPressed, _laneEventBuckets[lane], nowTicks, spawnFlow, flowLength);

    for (var lane = 0; lane < snapshot.Keys.Length; lane++)
      RenderKey(dc, lane, snapshot.Keys[lane].IsPressed, flowLength);
  }

  private bool IsHorizontal => _rotation is 90 or 270;
  private bool IsReversed => _rotation is 180 or 270;
  private double FlowLength => IsHorizontal ? _canvas.ActualWidth : _canvas.ActualHeight;
  private double SpawnFlow => IsReversed ? FlowLength - KeyLength - Gap : KeyLength + Gap;

  private void RenderLaneBars(
    DrawingContext dc,
    int lane,
    bool isPressed,
    List<KeyOverlayTransition> events,
    long nowTicks,
    double spawnFlow,
    double flowLength
  )
  {
    var bars = _bars[lane];

    foreach (var transition in events)
      ApplyTransition(lane, bars, transition);

    if (isPressed && (bars.Count == 0 || !bars[^1].IsHeld))
      OpenBar(bars, nowTicks);
    else if (!isPressed && bars.Count > 0 && bars[^1].IsHeld)
      CloseBar(bars[^1], nowTicks);

    var outsideCount = 0;
    while (outsideCount < bars.Count && IsOutside(bars[outsideCount], spawnFlow, flowLength, nowTicks))
      outsideCount++;

    if (outsideCount > 0)
    {
      for (var i = 0; i < outsideCount; i++)
        ReturnBar(bars[i]);
      bars.RemoveRange(0, outsideCount);
    }

    var excess = bars.Count - 40;
    if (excess > 0)
    {
      for (var i = 0; i < excess; i++)
        ReturnBar(bars[i]);
      bars.RemoveRange(0, excess);
    }

    var cross = CrossPosition(lane);
    var maxR = _laneWidth * 0.5;

    for (var i = 0; i < bars.Count; i++)
    {
      var bar = bars[i];
      var length = bar.IsHeld
        ? Math.Max(1, _speed * TicksToSeconds(nowTicks - bar.OpenTicks))
        : bar.ClosedLength;
      var offset = bar.IsHeld
        ? 0.0
        : Math.Max(0.0, _speed * TicksToSeconds(nowTicks - bar.CloseTicks));
      var flow = IsReversed
        ? spawnFlow - offset - length
        : spawnFlow + offset;

      var barRect = IsHorizontal
        ? new Rect(flow, cross, length, _laneWidth)
        : new Rect(cross, flow, _laneWidth, length);

      var r = Math.Min(_round, Math.Min(maxR, length * 0.5));
      if (r < 1.0)
        dc.DrawRectangle(BarBrush, null, barRect);
      else
        dc.DrawRoundedRectangle(BarBrush, null, barRect, r, r);
    }
  }

  private void RenderKey(DrawingContext dc, int lane, bool isPressed, double flowLength)
  {
    var keyFlow = IsReversed ? Math.Max(0, flowLength - KeyLength) : 0;
    var cross = CrossPosition(lane);

    var keyRect = IsHorizontal
      ? new Rect(keyFlow + 0.5, cross + 0.5, KeyLength - 1, _laneWidth - 1)
      : new Rect(cross + 0.5, keyFlow + 0.5, _laneWidth - 1, KeyLength - 1);

    var fill = isPressed ? KeyPressedBrush : KeyIdleBrush;
    dc.DrawRoundedRectangle(fill, BorderPen, keyRect, 4, 4);

    var label = isPressed ? _pressedLabels[lane] : _idleLabels[lane];
    var textOrigin = IsHorizontal
      ? new Point(keyFlow + (KeyLength - label.Width) / 2, cross + (_laneWidth - label.Height) / 2)
      : new Point(cross + (_laneWidth - label.Width) / 2, keyFlow + (KeyLength - label.Height) / 2);

    dc.DrawText(label, textOrigin);
  }

  private void ApplyTransition(int lane, List<BarState> bars, KeyOverlayTransition transition)
  {
    if (transition.IsPressed)
    {
      if (bars.Count > 0 && !bars[^1].IsHeld)
      {
        var gapTicks = transition.TimestampTicks - bars[^1].CloseTicks;
        if (gapTicks <= 0 || TicksToSeconds(gapTicks) * _speed < 1.5)
        {
          bars[^1].IsHeld = true;
          bars[^1].CloseTicks = 0;
          bars[^1].ClosedLength = 0;
          return;
        }
      }
      if (bars.Count == 0 || !bars[^1].IsHeld)
        OpenBar(bars, transition.TimestampTicks);
    }
    else if (bars.Count > 0 && bars[^1].IsHeld)
    {
      CloseBar(bars[^1], transition.TimestampTicks);
    }
  }

  private void OpenBar(List<BarState> bars, long openTicks)
  {
    bars.Add(RentBar(openTicks));
  }

  private void CloseBar(BarState bar, long closeTicks)
  {
    bar.IsHeld = false;
    bar.CloseTicks = closeTicks;
    bar.ClosedLength = Math.Max(1, _speed * Math.Max(0, TicksToSeconds(closeTicks - bar.OpenTicks)));
  }

  private static double TicksToSeconds(long ticks) => ticks / (double)Stopwatch.Frequency;

  private BarState RentBar(long openTicks)
  {
    if (_barPool.TryPop(out var bar))
    {
      bar.IsHeld = true;
      bar.OpenTicks = openTicks;
      bar.CloseTicks = 0;
      bar.ClosedLength = 0;
      return bar;
    }
    return new BarState { IsHeld = true, OpenTicks = openTicks };
  }

  private void ReturnBar(BarState bar) => _barPool.Push(bar);

  private bool IsOutside(BarState bar, double spawnFlow, double flowLength, long nowTicks)
  {
    if (bar.IsHeld)
      return false;
    var offset = _speed * TicksToSeconds(nowTicks - bar.CloseTicks);
    return IsReversed ? spawnFlow - offset < 0 : spawnFlow + offset > flowLength;
  }

  private void EnsureEventBuckets(int laneCount)
  {
    while (_laneEventBuckets.Count < laneCount)
      _laneEventBuckets.Add([]);
  }

  private void EnsureLayout(KeyOverlaySnapshot snapshot, double dpi)
  {
    if (HasSameLabels(snapshot) && Math.Abs(_lastDpi - dpi) < 0.001)
      return;

    foreach (var laneBars in _bars)
    {
      foreach (var bar in laneBars)
        ReturnBar(bar);
      laneBars.Clear();
    }
    _bars.Clear();
    _idleLabels.Clear();
    _pressedLabels.Clear();

    for (var i = 0; i < snapshot.Keys.Length; i++)
    {
      var text = snapshot.Keys[i].Label;
      _idleLabels.Add(new FormattedText(
        text,
        CultureInfo.InvariantCulture,
        FlowDirection.LeftToRight,
        KeyTypeface,
        14,
        LabelIdleBrush,
        dpi
      ));
      _pressedLabels.Add(new FormattedText(
        text,
        CultureInfo.InvariantCulture,
        FlowDirection.LeftToRight,
        KeyTypeface,
        14,
        Brushes.White,
        dpi
      ));
      _bars.Add([]);
    }
    _layout = snapshot;
    _lastDpi = dpi;
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

  private double CrossPosition(int lane) => Margin + lane * (_laneWidth + Gap);
}
