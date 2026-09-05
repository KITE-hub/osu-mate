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

  public sealed class KeyOverlayViewModel
  {
    private readonly ConcurrentQueue<KeyOverlayTransition> _transitions = new();
    private KeyOverlaySnapshot _layout = KeyOverlaySnapshot.Empty;
    private BeatmapOverlayState _beatmapState = BeatmapOverlayState.Empty;
    private volatile bool _resetRequested;
    private volatile bool _isPlayActive;

    internal KeyOverlaySnapshot Layout => Volatile.Read(ref _layout);
    internal BeatmapOverlayState BeatmapState => Volatile.Read(ref _beatmapState);
    internal Action? RequestUpdate { get; set; }
    internal bool IsPlayActive => _isPlayActive;

    internal void Publish(
      KeyOverlaySnapshot layout,
      List<KeyOverlayTransition> transitions,
      bool isPlayActive,
      bool resetCounts,
      BeatmapOverlayState beatmapState
    )
    {
      Volatile.Write(ref _layout, layout);
      Volatile.Write(ref _beatmapState, beatmapState);
      _isPlayActive = isPlayActive;
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
