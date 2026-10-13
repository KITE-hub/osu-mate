using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Threading;
using System.Windows.Threading;
using OsuMate.Models;

namespace OsuMate.Services.PlayLog
{
  public class BestPpTracker
  {
    private readonly PlayLogService _playLogService;
    private readonly ObservableCollection<string> _targetPlayerNames;
    private readonly HashSet<PlayLogEntry> _trackedEntries = [];
    private readonly PropertyChangedEventHandler _entryChangedHandler;

    private string _lastBeatmapMd5 = "";
    private int _refreshPending;

    public BeatmapPlayStats CachedStats { get; private set; }

    public event Action<BeatmapPlayStats>? BestPpChanged;

    public BestPpTracker(
      PlayLogService playLogService,
      ObservableCollection<string> targetPlayerNames
    )
    {
      _playLogService = playLogService;
      _targetPlayerNames = targetPlayerNames;
      _entryChangedHandler = OnEntryPropertyChanged;

      _targetPlayerNames.CollectionChanged += (_, _) => RequestRefresh();

      _playLogService.Entries.CollectionChanged += OnEntriesChanged;
      foreach (var entry in _playLogService.Entries)
        TrackEntry(entry);
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
      if (e.Action == NotifyCollectionChangedAction.Reset)
      {
        foreach (var tracked in _trackedEntries)
          tracked.PropertyChanged -= _entryChangedHandler;
        _trackedEntries.Clear();
        foreach (var entry in _playLogService.Entries)
          TrackEntry(entry);
      }
      else
      {
        if (e.OldItems != null)
          foreach (PlayLogEntry entry in e.OldItems)
            UntrackEntry(entry);
        if (e.NewItems != null)
          foreach (PlayLogEntry entry in e.NewItems)
            TrackEntry(entry);
      }

      RequestRefresh();
    }

    private void TrackEntry(PlayLogEntry entry)
    {
      if (_trackedEntries.Add(entry))
        entry.PropertyChanged += _entryChangedHandler;
    }

    private void UntrackEntry(PlayLogEntry entry)
    {
      if (_trackedEntries.Remove(entry))
        entry.PropertyChanged -= _entryChangedHandler;
    }

    private void OnEntryPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
      if (
        args.PropertyName == nameof(PlayLogEntry.Pp)
        || args.PropertyName == nameof(PlayLogEntry.IsCompleted)
      )
        RequestRefresh();
    }

    public void RefreshIfChanged(string beatmapMd5)
    {
      if (beatmapMd5 == _lastBeatmapMd5)
        return;
      Refresh(beatmapMd5);
    }

    public void Refresh(string beatmapMd5)
    {
      _lastBeatmapMd5 = beatmapMd5;
      RequestRefresh();
    }

    private void RequestRefresh()
    {
      if (Interlocked.Exchange(ref _refreshPending, 1) == 1)
        return;

      _playLogService.UiDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(RunRefresh));
    }

    private void RunRefresh()
    {
      Interlocked.Exchange(ref _refreshPending, 0);
      var stats = BestPpCalculator.GetStats(
        _playLogService.Entries,
        _lastBeatmapMd5,
        _targetPlayerNames
      );
      CachedStats = stats;
      BestPpChanged?.Invoke(stats);
    }
  }
}
