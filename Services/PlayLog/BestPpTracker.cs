using System.Collections.ObjectModel;
using System.Collections.Specialized;
using OsuMate.Models;

namespace OsuMate.Services.PlayLog
{
  public class BestPpTracker
  {
    private readonly PlayLogService _playLogService;
    private readonly ObservableCollection<string> _targetPlayerNames;
    private readonly HashSet<PlayLogEntry> _trackedEntries = [];

    private string _lastBeatmapMd5 = "";

    public double? CachedBestPp { get; private set; }

    public event Action<double?>? BestPpChanged;

    public BestPpTracker(
      PlayLogService playLogService,
      ObservableCollection<string> targetPlayerNames
    )
    {
      _playLogService = playLogService;
      _targetPlayerNames = targetPlayerNames;

      _targetPlayerNames.CollectionChanged += (_, _) => Refresh(_lastBeatmapMd5);

      _playLogService.Entries.CollectionChanged += OnEntriesChanged;
      foreach (var entry in _playLogService.Entries)
        TrackEntry(entry);
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
      if (e.NewItems != null)
        foreach (PlayLogEntry entry in e.NewItems)
          TrackEntry(entry);

      Refresh(_lastBeatmapMd5);
    }

    private void TrackEntry(PlayLogEntry entry)
    {
      if (!_trackedEntries.Add(entry))
        return;

      entry.PropertyChanged += (_, args) =>
      {
        if (
          args.PropertyName == nameof(PlayLogEntry.Pp)
          || args.PropertyName == nameof(PlayLogEntry.IsCompleted)
        )
          Refresh(_lastBeatmapMd5);
      };
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
      var bestPp = BestPpCalculator.GetBestPp(
        _playLogService.Entries,
        beatmapMd5,
        _targetPlayerNames
      );
      CachedBestPp = bestPp;
      BestPpChanged?.Invoke(bestPp);
    }
  }
}
