using System.Collections.Concurrent;
using System.Threading;
using OsuMate.Models;

namespace OsuMate.ViewModels
{
  public sealed record BeatmapOverlayState(
    BeatmapOverlayNote[] Notes,
    double AudioTime,
    int Mode,
    int BeatmapLaneIndex,
    bool ShowBeatmapBars
  )
  {
    public static BeatmapOverlayState Empty { get; } = new([], 0, -1, -1, false);
  }

  internal sealed record KeyOverlayPublishedState(
    KeyOverlaySnapshot Layout,
    BeatmapOverlayState BeatmapState,
    bool IsPlayActive
  )
  {
    public static KeyOverlayPublishedState Empty { get; } = new(KeyOverlaySnapshot.Empty, BeatmapOverlayState.Empty, false);
  }

  public sealed class KeyOverlayViewModel
  {
    private readonly ConcurrentQueue<KeyOverlayTransition> _transitions = new();
    private KeyOverlayPublishedState _state = KeyOverlayPublishedState.Empty;
    private volatile bool _resetRequested;

    internal KeyOverlayPublishedState Snapshot => Volatile.Read(ref _state);
    internal Action? RequestUpdate { get; set; }

    internal void Publish(
      KeyOverlaySnapshot layout,
      List<KeyOverlayTransition> transitions,
      bool isPlayActive,
      bool resetCounts,
      BeatmapOverlayState beatmapState
    )
    {
      Volatile.Write(ref _state, new KeyOverlayPublishedState(layout, beatmapState, isPlayActive));
      if (resetCounts)
        _resetRequested = true;
      foreach (var transition in transitions)
        _transitions.Enqueue(transition);
    }

    internal bool DrainReset()
    {
      if (!_resetRequested)
        return false;
      _resetRequested = false;
      return true;
    }

    internal void DrainTransitions(List<KeyOverlayTransition> destination)
    {
      while (_transitions.TryDequeue(out var transition))
        destination.Add(transition);
    }
  }
}
