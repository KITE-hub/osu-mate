using System;
using System.Collections.Generic;
using System.Diagnostics;
using OsuMate.Models;

namespace OsuMate.Services.Key;

internal sealed class BeatmapNoteTracker
{
  private const double ForwardResetGapMs = 2000.0;

  private BeatmapOverlayNote[] _notesRef = [];
  private int _cursor;
  private double? _lastAudioTime;
  private long _lastTicks;

  private bool[] _pendingIsHold = [];
  private double[] _pendingHoldEndTime = [];
  private long[] _pendingTapCloseTicks = [];

  public void Reset()
  {
    _lastAudioTime = null;
    _cursor = 0;
    Array.Fill(_pendingIsHold, false);
    Array.Fill(_pendingHoldEndTime, double.NaN);
    Array.Fill(_pendingTapCloseTicks, long.MinValue);
  }

  public void Update(
    BeatmapOverlayNote[] notes,
    double audioTime,
    long nowTicks,
    int laneCount,
    bool isMania,
    int beatmapLaneIndex,
    double tapLengthMs,
    bool forceReset,
    List<BeatmapNoteTransition> output
  )
  {
    EnsureLanes(laneCount);

    if (!ReferenceEquals(_notesRef, notes))
    {
      EmitPendingCloses(nowTicks, output);
      _notesRef = notes;
      Reset();
    }

    if (
      forceReset
      || _lastAudioTime is not { } previousAudioTime
      || audioTime < previousAudioTime
      || audioTime - previousAudioTime > ForwardResetGapMs
    )
    {
      EmitPendingCloses(nowTicks, output);
      Array.Fill(_pendingIsHold, false);
      Array.Fill(_pendingHoldEndTime, double.NaN);
      Array.Fill(_pendingTapCloseTicks, long.MinValue);
      _cursor = FindFirstIndexAfter(notes, audioTime);
      _lastAudioTime = audioTime;
      _lastTicks = nowTicks;
      return;
    }

    EmitDueTapCloses(nowTicks, output);

    if (audioTime == previousAudioTime)
      return;

    EmitDueHoldCloses(previousAudioTime, audioTime, nowTicks, output);

    var tapLengthTicks = (long)(tapLengthMs / 1000.0 * Stopwatch.Frequency);

    while (_cursor < notes.Length && notes[_cursor].StartTime <= audioTime)
    {
      var note = notes[_cursor];
      _cursor++;

      var targetLane = isMania ? note.Lane : beatmapLaneIndex;
      if ((uint)targetLane >= (uint)laneCount)
        continue;

      var pressTicks = InterpolateTicks(previousAudioTime, audioTime, _lastTicks, nowTicks, note.StartTime);
      CloseExistingPending(targetLane, pressTicks, output);
      output.Add(new BeatmapNoteTransition(targetLane, true, pressTicks, note.Type));

      var isHold = note.Type == BeatmapNoteType.Hold && note.EndTime > note.StartTime;
      _pendingIsHold[targetLane] = isHold;
      if (isHold)
        _pendingHoldEndTime[targetLane] = note.EndTime;
      else
        _pendingTapCloseTicks[targetLane] = pressTicks + tapLengthTicks;
    }

    _lastAudioTime = audioTime;
    _lastTicks = nowTicks;
  }

  private void EmitPendingCloses(long nowTicks, List<BeatmapNoteTransition> output)
  {
    for (var lane = 0; lane < _pendingIsHold.Length; lane++)
    {
      if (_pendingIsHold[lane] || _pendingTapCloseTicks[lane] != long.MinValue)
        output.Add(new BeatmapNoteTransition(lane, false, nowTicks, BeatmapNoteType.Normal));
    }
  }

  private void EmitDueTapCloses(long nowTicks, List<BeatmapNoteTransition> output)
  {
    for (var lane = 0; lane < _pendingTapCloseTicks.Length; lane++)
    {
      if (_pendingIsHold[lane] || _pendingTapCloseTicks[lane] == long.MinValue || nowTicks < _pendingTapCloseTicks[lane])
        continue;
      output.Add(new BeatmapNoteTransition(lane, false, _pendingTapCloseTicks[lane], BeatmapNoteType.Normal));
      _pendingTapCloseTicks[lane] = long.MinValue;
    }
  }

  private void EmitDueHoldCloses(double previousAudioTime, double audioTime, long nowTicks, List<BeatmapNoteTransition> output)
  {
    for (var lane = 0; lane < _pendingHoldEndTime.Length; lane++)
    {
      if (!_pendingIsHold[lane] || _pendingHoldEndTime[lane] > audioTime)
        continue;
      var closeTicks = InterpolateTicks(previousAudioTime, audioTime, _lastTicks, nowTicks, _pendingHoldEndTime[lane]);
      output.Add(new BeatmapNoteTransition(lane, false, closeTicks, BeatmapNoteType.Normal));
      _pendingIsHold[lane] = false;
      _pendingHoldEndTime[lane] = double.NaN;
    }
  }

  private void CloseExistingPending(int lane, long atTicks, List<BeatmapNoteTransition> output)
  {
    if (_pendingIsHold[lane])
    {
      output.Add(new BeatmapNoteTransition(lane, false, atTicks, BeatmapNoteType.Normal));
      _pendingIsHold[lane] = false;
      _pendingHoldEndTime[lane] = double.NaN;
    }
    else if (_pendingTapCloseTicks[lane] != long.MinValue)
    {
      output.Add(new BeatmapNoteTransition(lane, false, Math.Min(atTicks, _pendingTapCloseTicks[lane]), BeatmapNoteType.Normal));
      _pendingTapCloseTicks[lane] = long.MinValue;
    }
  }

  private void EnsureLanes(int laneCount)
  {
    if (_pendingIsHold.Length >= laneCount)
      return;
    _pendingIsHold = new bool[laneCount];
    _pendingHoldEndTime = new double[laneCount];
    _pendingTapCloseTicks = new long[laneCount];
    Array.Fill(_pendingHoldEndTime, double.NaN);
    Array.Fill(_pendingTapCloseTicks, long.MinValue);
  }

  private static long InterpolateTicks(double fromAudioTime, double toAudioTime, long fromTicks, long toTicks, double targetAudioTime)
  {
    var span = toAudioTime - fromAudioTime;
    if (span <= 0)
      return toTicks;
    var t = Math.Clamp((targetAudioTime - fromAudioTime) / span, 0.0, 1.0);
    return fromTicks + (long)((toTicks - fromTicks) * t);
  }

  private static int FindFirstIndexAfter(BeatmapOverlayNote[] notes, double audioTime)
  {
    var low = 0;
    var high = notes.Length;
    while (low < high)
    {
      var mid = low + (high - low) / 2;
      if (notes[mid].StartTime <= audioTime)
        low = mid + 1;
      else
        high = mid;
    }
    return low;
  }
}
