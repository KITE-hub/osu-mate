using System;
using System.Collections.Generic;
using System.Diagnostics;
using OsuMate.Models;
using OsuMate.Rendering;
using OsuMate.Services.Key;
using OsuMate.Utils;
using OsuMate.ViewModels;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace OsuMate.Views.Controls;

public enum ResizeEdge
{
  None,
  Top,
  Bottom,
  Left,
  Right
}

internal sealed class Direct2DKeyOverlayRenderer : IDisposable
{
  private sealed class BarState
  {
    public bool IsHeld { get; set; }
    public long OpenTicks { get; set; }
    public long CloseTicks { get; set; }
    public double ClosedLength { get; set; }
    public BeatmapNoteType NoteType { get; set; }
  }

  internal const double KeyLength = 48;
  internal const double Gap = 4;
  private const double Margin = 4;
  private const int MaxKeyBarsPerLane = 40;
  private const int MaxMapBarsPerLane = 128;

  private readonly Stack<BarState> _barPool = new();
  private readonly List<List<BarState>> _bars = [];
  private readonly List<List<KeyOverlayTransition>> _laneEventBuckets = [];
  private readonly List<int> _pressCounts = [];
  private readonly List<Queue<long>> _recentPressTicks = [];

  private readonly ID2D1SolidColorBrush _keyPressedBrush;
  private readonly ID2D1SolidColorBrush _keyIdleBrush;
  private readonly ID2D1SolidColorBrush _labelIdleBrush;
  private readonly ID2D1SolidColorBrush _subLabelIdleBrush;
  private readonly ID2D1SolidColorBrush _labelPressedBrush;
  private readonly ID2D1SolidColorBrush _barBrush;
  private readonly ID2D1SolidColorBrush _borderBrush;
  private readonly ID2D1SolidColorBrush _donBorderBrush;
  private readonly ID2D1SolidColorBrush _katBorderBrush;
  private readonly ID2D1SolidColorBrush _dragBackgroundBrush;
  private readonly ID2D1SolidColorBrush _dragBorderBrush;
  private readonly ID2D1SolidColorBrush _taikoDonBrush;
  private readonly ID2D1SolidColorBrush _taikoKatBrush;
  private readonly ID2D1SolidColorBrush _standardMapBrush;
  private readonly ID2D1SolidColorBrush _maniaBeatmapBrush;
  private readonly Direct2DContext _context;
  private string _fontFamily = "Oxanium";
  private IDWriteTextFormat _textFormat;
  private IDWriteTextFormat _countFormat;
  private IDWriteTextFormat _kpsFormat;
  private readonly BeatmapNoteTracker _beatmapNoteTracker = new();
  private readonly List<List<BarState>> _mapBars = [];
  private readonly List<List<BeatmapNoteTransition>> _mapEventBuckets = [];
  private readonly List<BeatmapNoteTransition> _mapTransitionBuffer = [];

  private int _rotation;
  private double _speed = 600;
  private double _round = 4;
  private double _laneWidth = 64;
  private double _beatmapTapLengthMs = 25;
  private bool _disposed;

  public Direct2DKeyOverlayRenderer(Direct2DContext context)
  {
    _context = context;
    var dc = context.DeviceContext;
    _keyPressedBrush = dc.CreateSolidColorBrush(new Color4(1f, 1f, 1f, 110f / 255f));
    _keyIdleBrush = dc.CreateSolidColorBrush(new Color4(1f, 1f, 1f, 28f / 255f));
    _labelIdleBrush = dc.CreateSolidColorBrush(new Color4(1f, 1f, 1f, 180f / 255f));
    _subLabelIdleBrush = dc.CreateSolidColorBrush(new Color4(1f, 1f, 1f, 140f / 255f));
    _labelPressedBrush = dc.CreateSolidColorBrush(new Color4(1f, 1f, 1f, 1f));
    _barBrush = dc.CreateSolidColorBrush(new Color4(1f, 1f, 1f, 0.5f));
    _borderBrush = dc.CreateSolidColorBrush(new Color4(1f, 1f, 1f, 180f / 255f));
    _donBorderBrush = dc.CreateSolidColorBrush(new Color4(235f / 255f, 65f / 255f, 60f / 255f, 180f / 255f));
    _katBorderBrush = dc.CreateSolidColorBrush(new Color4(55f / 255f, 150f / 255f, 240f / 255f, 180f / 255f));
    _dragBackgroundBrush = dc.CreateSolidColorBrush(new Color4(0f, 0f, 0f, 204f / 255f));
    _dragBorderBrush = dc.CreateSolidColorBrush(new Color4(1f, 1f, 1f, 1f));
    _taikoDonBrush = dc.CreateSolidColorBrush(new Color4(235f / 255f, 65f / 255f, 60f / 255f, 0.5f));
    _taikoKatBrush = dc.CreateSolidColorBrush(new Color4(55f / 255f, 150f / 255f, 240f / 255f, 0.5f));
    _standardMapBrush = dc.CreateSolidColorBrush(new Color4(255f / 255f, 205f / 255f, 60f / 255f, 0.5f));
    _maniaBeatmapBrush = dc.CreateSolidColorBrush(new Color4(60f / 255f, 185f / 255f, 245f / 255f, 0.5f));
    _textFormat = context.CreateKeyTextFormat(_fontFamily, 14f);
    _countFormat = context.CreateKeyTextFormat(_fontFamily, 10f);
    _kpsFormat = context.CreateKeyTextFormat(_fontFamily, 9f);
  }

  public void UpdateSettings(
    int rotation,
    double speed,
    double round,
    double laneWidth,
    string? fontFamily = null,
    double inputBarOpacity = 0.5,
    double beatmapBarOpacity = 0.5,
    double beatmapTapLengthMs = 25
  )
  {
    _rotation = ((rotation % 360) + 360) % 360;
    _speed = Math.Clamp(speed, 50, 1500);
    _round = Math.Clamp(round, 0, 32);
    _laneWidth = Math.Clamp(laneWidth, 25, 100);
    _beatmapTapLengthMs = Math.Clamp(beatmapTapLengthMs, 10, 50);

    var inAlpha = (float)Math.Clamp(inputBarOpacity, 0.0, 1.0);
    var bmAlpha = (float)Math.Clamp(beatmapBarOpacity, 0.0, 1.0);
    _barBrush.Color = new Color4(1f, 1f, 1f, inAlpha);
    _taikoDonBrush.Color = new Color4(235f / 255f, 65f / 255f, 60f / 255f, bmAlpha);
    _taikoKatBrush.Color = new Color4(55f / 255f, 150f / 255f, 240f / 255f, bmAlpha);
    _standardMapBrush.Color = new Color4(255f / 255f, 205f / 255f, 60f / 255f, bmAlpha);
    _maniaBeatmapBrush.Color = new Color4(60f / 255f, 185f / 255f, 245f / 255f, bmAlpha);

    if (!string.IsNullOrWhiteSpace(fontFamily) && !string.Equals(_fontFamily, fontFamily, StringComparison.OrdinalIgnoreCase))
    {
      _fontFamily = fontFamily.Trim();
      _textFormat.Dispose();
      _countFormat.Dispose();
      _kpsFormat.Dispose();
      _textFormat = _context.CreateKeyTextFormat(_fontFamily, 14f);
      _countFormat = _context.CreateKeyTextFormat(_fontFamily, 10f);
      _kpsFormat = _context.CreateKeyTextFormat(_fontFamily, 9f);
    }
  }

  public (double Width, double Height) GetRequiredSize(int laneCount, double flowLength)
  {
    var crossLength = Margin * 2 + laneCount * _laneWidth + Math.Max(0, laneCount - 1) * Gap;
    return IsHorizontal ? (flowLength, crossLength) : (crossLength, flowLength);
  }

  public ResizeEdge HitTestResizeHandle(float x, float y, float width, float height)
  {
    if (IsHorizontal)
    {
      if (x <= 24 && x >= -8 && y >= -8 && y <= height + 8)
        return ResizeEdge.Left;
      if (x >= width - 24 && x <= width + 8 && y >= -8 && y <= height + 8)
        return ResizeEdge.Right;
    }
    else
    {
      if (y <= 24 && y >= -8 && x >= -8 && x <= width + 8)
        return ResizeEdge.Top;
      if (y >= height - 24 && y <= height + 8 && x >= -8 && x <= width + 8)
        return ResizeEdge.Bottom;
    }
    return ResizeEdge.None;
  }

  public void Render(
    ID2D1DeviceContext dc,
    KeyOverlaySnapshot snapshot,
    IReadOnlyList<KeyOverlayTransition> transitions,
    long nowTicks,
    double actualWidth,
    double actualHeight,
    bool isDraggable,
    bool isPlayActive,
    bool resetCounts,
    BeatmapOverlayState beatmapState
  )
  {
    dc.BeginDraw();
    dc.Clear(new Color4(0f, 0f, 0f, 0f));

    if (isDraggable)
    {
      var winRect = new Rect(0.5f, 0.5f, Math.Max(1f, (float)actualWidth - 1f), Math.Max(1f, (float)actualHeight - 1f));
      dc.FillRoundedRectangle(CreateRoundedRect(winRect, 7.5f, 7.5f), _dragBackgroundBrush);
      dc.DrawRoundedRectangle(CreateRoundedRect(winRect, 7.5f, 7.5f), _dragBorderBrush, 1.0f);
    }

    if (snapshot.Keys.Length > 0)
    {
      EnsureLanes(snapshot.Keys.Length);

      if (resetCounts)
      {
        for (var i = 0; i < _pressCounts.Count; i++)
          _pressCounts[i] = 0;
      }

      var flowLength = FlowLength(actualWidth, actualHeight);
      if (flowLength > 0)
      {
        EnsureEventBuckets(snapshot.Keys.Length);
        for (var i = 0; i < _laneEventBuckets.Count; i++)
          _laneEventBuckets[i].Clear();

        for (var i = 0; i < transitions.Count; i++)
        {
          var tr = transitions[i];
          if ((uint)tr.LaneIndex < (uint)_laneEventBuckets.Count)
          {
            _laneEventBuckets[tr.LaneIndex].Add(tr);
            if (tr.IsPressed)
            {
              _recentPressTicks[tr.LaneIndex].Enqueue(tr.TimestampTicks);
              if (isPlayActive)
                _pressCounts[tr.LaneIndex]++;
            }
          }
        }

        var spawnFlow = SpawnFlow(flowLength);
        var isMania = beatmapState.Mode == 3;

        if (beatmapState.ShowBeatmapBars && isPlayActive && beatmapState.Notes.Length > 0)
        {
          EnsureMapLanes(snapshot.Keys.Length);
          _mapTransitionBuffer.Clear();
          _beatmapNoteTracker.Update(
            beatmapState.Notes,
            beatmapState.AudioTime,
            nowTicks,
            snapshot.Keys.Length,
            isMania,
            beatmapState.BeatmapLaneIndex,
            _beatmapTapLengthMs,
            resetCounts,
            _mapTransitionBuffer
          );

          for (var i = 0; i < _mapEventBuckets.Count; i++)
            _mapEventBuckets[i].Clear();
          for (var i = 0; i < _mapTransitionBuffer.Count; i++)
          {
            var tr = _mapTransitionBuffer[i];
            if ((uint)tr.LaneIndex < (uint)_mapEventBuckets.Count)
              _mapEventBuckets[tr.LaneIndex].Add(tr);
          }

          if (isMania)
          {
            for (var lane = 0; lane < snapshot.Keys.Length; lane++)
              RenderMapLaneBars(dc, lane, isMania, _mapEventBuckets[lane], nowTicks, spawnFlow, flowLength);
          }
          else if ((uint)beatmapState.BeatmapLaneIndex < (uint)snapshot.Keys.Length)
          {
            RenderMapLaneBars(dc, beatmapState.BeatmapLaneIndex, isMania, _mapEventBuckets[beatmapState.BeatmapLaneIndex], nowTicks, spawnFlow, flowLength);
          }
        }
        else
        {
          _beatmapNoteTracker.Reset();
          CloseAllHeldMapBars(nowTicks);
        }

        for (var lane = 0; lane < snapshot.Keys.Length; lane++)
        {
          if (beatmapState.ShowBeatmapBars && lane == beatmapState.BeatmapLaneIndex)
            continue;
          RenderLaneBars(dc, lane, snapshot.Keys[lane].IsPressed, _laneEventBuckets[lane], nowTicks, spawnFlow, flowLength);
        }

        for (var lane = 0; lane < snapshot.Keys.Length; lane++)
          RenderKey(dc, lane, snapshot.Keys[lane].Label, snapshot.Keys[lane].IsPressed, snapshot.Keys[lane].Role, flowLength, nowTicks, beatmapState.ShowBeatmapBars && lane == beatmapState.BeatmapLaneIndex);
      }
    }

    dc.EndDraw();
  }

  private bool IsHorizontal => _rotation is 90 or 270;
  private bool IsReversed => _rotation is 180 or 270;
  private double FlowLength(double width, double height) => IsHorizontal ? width : height;
  private double SpawnFlow(double flowLength) => IsReversed ? flowLength - KeyLength - Gap : KeyLength + Gap;
  private double CrossPosition(int lane) => Margin + lane * (_laneWidth + Gap);

  private void RenderLaneBars(
    ID2D1DeviceContext dc,
    int lane,
    bool isPressed,
    List<KeyOverlayTransition> events,
    long nowTicks,
    double spawnFlow,
    double flowLength
  )
  {
    var bars = _bars[lane];

    for (var i = 0; i < events.Count; i++)
      ApplyTransition(bars, events[i]);

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

    var excess = bars.Count - MaxKeyBarsPerLane;
    if (excess > 0)
    {
      for (var i = 0; i < excess; i++)
        ReturnBar(bars[i]);
      bars.RemoveRange(0, excess);
    }

    var cross = (float)CrossPosition(lane);
    var maxR = (float)(_laneWidth * 0.5);

    for (var i = 0; i < bars.Count; i++)
    {
      var bar = bars[i];
      var length = (float)(bar.IsHeld
        ? Math.Max(1.0, _speed * TicksToSeconds(nowTicks - bar.OpenTicks))
        : bar.ClosedLength);
      var offset = (float)(bar.IsHeld
        ? 0.0
        : Math.Max(0.0, _speed * TicksToSeconds(nowTicks - bar.CloseTicks)));
      var flow = (float)(IsReversed
        ? spawnFlow - offset - length
        : spawnFlow + offset);

      var barRect = IsHorizontal
        ? new Rect(flow, cross, length, (float)_laneWidth)
        : new Rect(cross, flow, (float)_laneWidth, length);

      var r = (float)Math.Min(_round, Math.Min(maxR, length * 0.5));
      if (r < 1.0f)
        dc.FillRectangle(barRect, _barBrush);
      else
        dc.FillRoundedRectangle(CreateRoundedRect(barRect, r, r), _barBrush);
    }
  }

  private void CloseAllHeldMapBars(long nowTicks)
  {
    for (var lane = 0; lane < _mapBars.Count; lane++)
    {
      var bars = _mapBars[lane];
      if (bars.Count > 0 && bars[^1].IsHeld)
        CloseBar(bars[^1], nowTicks);
    }
  }

  private void ApplyMapTransition(List<BarState> bars, BeatmapNoteTransition transition)
  {
    if (transition.IsPressed)
    {
      var bar = RentBar(transition.TimestampTicks);
      bar.NoteType = transition.NoteType;
      bars.Add(bar);
    }
    else if (bars.Count > 0 && bars[^1].IsHeld)
    {
      CloseBar(bars[^1], transition.TimestampTicks);
    }
  }

  private void RenderMapLaneBars(
    ID2D1DeviceContext dc,
    int lane,
    bool isMania,
    List<BeatmapNoteTransition> events,
    long nowTicks,
    double spawnFlow,
    double flowLength
  )
  {
    var bars = _mapBars[lane];

    for (var i = 0; i < events.Count; i++)
      ApplyMapTransition(bars, events[i]);

    var outsideCount = 0;
    while (outsideCount < bars.Count && IsOutside(bars[outsideCount], spawnFlow, flowLength, nowTicks))
      outsideCount++;

    if (outsideCount > 0)
    {
      for (var i = 0; i < outsideCount; i++)
        ReturnBar(bars[i]);
      bars.RemoveRange(0, outsideCount);
    }

    var excess = bars.Count - MaxMapBarsPerLane;
    if (excess > 0)
    {
      for (var i = 0; i < excess; i++)
        ReturnBar(bars[i]);
      bars.RemoveRange(0, excess);
    }

    var cross = (float)CrossPosition(lane);
    var maxR = (float)(_laneWidth * 0.5);

    for (var i = 0; i < bars.Count; i++)
    {
      var bar = bars[i];
      var length = (float)(bar.IsHeld
        ? Math.Max(2.0, _speed * TicksToSeconds(nowTicks - bar.OpenTicks))
        : bar.ClosedLength);
      var offset = (float)(bar.IsHeld
        ? 0.0
        : Math.Max(0.0, _speed * TicksToSeconds(nowTicks - bar.CloseTicks)));
      var flow = (float)(IsReversed
        ? spawnFlow - offset - length
        : spawnFlow + offset);

      if (flow + length < 0 || flow > flowLength)
        continue;

      var barRect = IsHorizontal
        ? new Rect(flow, cross, length, (float)_laneWidth)
        : new Rect(cross, flow, (float)_laneWidth, length);

      var brush = bar.NoteType switch
      {
        BeatmapNoteType.TaikoDon => _taikoDonBrush,
        BeatmapNoteType.TaikoKat => _taikoKatBrush,
        _ => isMania ? _maniaBeatmapBrush : _standardMapBrush
      };

      var r = (float)Math.Min(_round, Math.Min(maxR, length * 0.5));
      if (r < 1.0f)
        dc.FillRectangle(barRect, brush);
      else
        dc.FillRoundedRectangle(CreateRoundedRect(barRect, r, r), brush);
    }
  }

  private void RenderKey(
    ID2D1DeviceContext dc,
    int lane,
    string label,
    bool isPressed,
    BeatmapNoteType role,
    double flowLength,
    long nowTicks,
    bool isMapLane = false
  )
  {
    var keyFlow = (float)(IsReversed ? Math.Max(0.0, flowLength - KeyLength) : 0.0);
    var cross = (float)CrossPosition(lane);

    var keyRect = IsHorizontal
      ? new Rect(keyFlow + 0.5f, cross + 0.5f, (float)(KeyLength - 1), (float)(_laneWidth - 1))
      : new Rect(cross + 0.5f, keyFlow + 0.5f, (float)(_laneWidth - 1), (float)(KeyLength - 1));

    var fill = isPressed && !isMapLane ? _keyPressedBrush : _keyIdleBrush;
    var border = role switch
    {
      BeatmapNoteType.TaikoDon => _donBorderBrush,
      BeatmapNoteType.TaikoKat => _katBorderBrush,
      _ => _borderBrush
    };
    var rounded = CreateRoundedRect(keyRect, 4, 4);
    dc.FillRoundedRectangle(rounded, fill);
    dc.DrawRoundedRectangle(rounded, border, 1.0f);

    var w = keyRect.Right - keyRect.Left;
    var h = keyRect.Bottom - keyRect.Top;

    if (isMapLane)
    {
      var mapLabelRect = new Rect(keyRect.Left, keyRect.Top + h * 0.42f - 8f, w, 16f);
      dc.DrawText("MAP", _textFormat, mapLabelRect, _labelIdleBrush);
      return;
    }

    var keyCenterY = h * 0.18f;
    var countCenterY = h * 0.58f;
    var kpsCenterY = h * 0.85f;

    var labelRect = new Rect(keyRect.Left, keyRect.Top + keyCenterY - 8f, w, 16f);
    var countRect = new Rect(keyRect.Left, keyRect.Top + countCenterY - 6f, w, 12f);
    var kpsRect = new Rect(keyRect.Left, keyRect.Top + kpsCenterY - 6f, w, 12f);

    var displayLabel = KeyLabelUtils.FormatKeyLabel(label);
    dc.DrawText(displayLabel, _textFormat, labelRect, isPressed ? _labelPressedBrush : _labelIdleBrush);

    var count = (uint)lane < (uint)_pressCounts.Count ? _pressCounts[lane] : 0;
    var countText = count.ToString();
    dc.DrawText(countText, _countFormat, countRect, isPressed ? _labelPressedBrush : _labelIdleBrush);

    var kps = CalculateKps(lane, nowTicks);
    var kpsText = w >= 36 ? $"{kps} KPS" : kps.ToString();
    dc.DrawText(kpsText, _kpsFormat, kpsRect, isPressed ? _labelPressedBrush : _subLabelIdleBrush);
  }

  private int CalculateKps(int lane, long nowTicks)
  {
    if ((uint)lane >= (uint)_recentPressTicks.Count)
      return 0;

    var queue = _recentPressTicks[lane];
    var cutoff = nowTicks - Stopwatch.Frequency;
    while (queue.Count > 0 && queue.Peek() < cutoff)
      queue.Dequeue();

    return queue.Count;
  }

  private static RoundedRectangle CreateRoundedRect(Rect rect, float radiusX, float radiusY) => new()
  {
    Rect = rect,
    RadiusX = radiusX,
    RadiusY = radiusY
  };

  private void ApplyTransition(List<BarState> bars, KeyOverlayTransition transition)
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

  private void OpenBar(List<BarState> bars, long openTicks) => bars.Add(RentBar(openTicks));

  private void CloseBar(BarState bar, long closeTicks)
  {
    bar.IsHeld = false;
    bar.CloseTicks = closeTicks;
    bar.ClosedLength = Math.Max(1.0, _speed * Math.Max(0.0, TicksToSeconds(closeTicks - bar.OpenTicks)));
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

  private void EnsureLanes(int laneCount)
  {
    while (_bars.Count < laneCount)
      _bars.Add([]);
    while (_pressCounts.Count < laneCount)
      _pressCounts.Add(0);
    while (_recentPressTicks.Count < laneCount)
      _recentPressTicks.Add(new Queue<long>());
  }

  private void EnsureEventBuckets(int laneCount)
  {
    while (_laneEventBuckets.Count < laneCount)
      _laneEventBuckets.Add([]);
  }

  private void EnsureMapLanes(int laneCount)
  {
    while (_mapBars.Count < laneCount)
      _mapBars.Add([]);
    while (_mapEventBuckets.Count < laneCount)
      _mapEventBuckets.Add([]);
  }

  public void Dispose()
  {
    if (_disposed)
      return;

    _disposed = true;

    _keyPressedBrush.Dispose();
    _keyIdleBrush.Dispose();
    _labelIdleBrush.Dispose();
    _subLabelIdleBrush.Dispose();
    _labelPressedBrush.Dispose();
    _barBrush.Dispose();
    _borderBrush.Dispose();
    _donBorderBrush.Dispose();
    _katBorderBrush.Dispose();
    _dragBackgroundBrush.Dispose();
    _dragBorderBrush.Dispose();
    _taikoDonBrush.Dispose();
    _taikoKatBrush.Dispose();
    _standardMapBrush.Dispose();
    _maniaBeatmapBrush.Dispose();
    _textFormat.Dispose();
    _countFormat.Dispose();
    _kpsFormat.Dispose();

    for (var i = 0; i < _bars.Count; i++)
    {
      for (var j = 0; j < _bars[i].Count; j++)
        ReturnBar(_bars[i][j]);
      _bars[i].Clear();
    }
    _bars.Clear();
    _pressCounts.Clear();
    _recentPressTicks.Clear();

    for (var i = 0; i < _mapBars.Count; i++)
    {
      for (var j = 0; j < _mapBars[i].Count; j++)
        ReturnBar(_mapBars[i][j]);
      _mapBars[i].Clear();
    }
    _mapBars.Clear();
    _mapEventBuckets.Clear();
  }
}
