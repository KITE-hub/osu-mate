using OsuMemoryDataProvider;
using OsuMemoryDataProvider.OsuMemoryModels;

namespace OsuMate.Services.Osu;

internal sealed class UrTimelineStore
{
  private readonly object _lock = new();
  private readonly OsuBaseAddresses _baseAddresses;

  private readonly List<(double timeSec, double offsetMs)> _urTimelineData = [];
  private int _urTimelineEpoch = 0;
  private int _urTimelineSnapshotCursor = 0;
  private int _urTimelineSnapshotEpoch = 0;
  private int _lastHitErrorValue = 0;
  private int _previousHitCount = 0;

  internal double FirstObjectTimeModified { get; set; } = 0;

  internal UrTimelineStore(OsuBaseAddresses baseAddresses)
  {
    _baseAddresses = baseAddresses;
  }

  internal (bool Reset, List<(double timeSec, double offsetMs)> NewItems) GetSnapshot()
  {
    lock (_lock)
    {
      bool reset =
        _urTimelineSnapshotEpoch != _urTimelineEpoch
        || _urTimelineData.Count < _urTimelineSnapshotCursor;
      int fromIndex = reset ? 0 : _urTimelineSnapshotCursor;
      var newItems = _urTimelineData.GetRange(fromIndex, _urTimelineData.Count - fromIndex);
      _urTimelineSnapshotCursor = _urTimelineData.Count;
      _urTimelineSnapshotEpoch = _urTimelineEpoch;
      return (reset, newItems);
    }
  }

  internal void Sync(IReadOnlyList<int> hitErrors, double speedMultiplier)
  {
    int currentCount = hitErrors.Count;
    double audioTime = _baseAddresses.GeneralData.AudioTime;

    lock (_lock)
    {
      bool discontinuous = HitErrorHelper.IsDiscontinuous(
        hitErrors,
        _previousHitCount,
        _lastHitErrorValue
      );

      if (discontinuous)
      {
        _urTimelineData.Clear();
        _urTimelineEpoch++;
        for (int i = 0; i < currentCount; i++)
        {
          _urTimelineData.Add((0, HitErrorHelper.ToModified(hitErrors[i], speedMultiplier)));
        }
      }
      else if (currentCount > _previousHitCount)
      {
        for (int i = _previousHitCount; i < currentCount; i++)
        {
          double timeSec = (audioTime * speedMultiplier - FirstObjectTimeModified) / 1000.0;
          _urTimelineData.Add((timeSec, HitErrorHelper.ToModified(hitErrors[i], speedMultiplier)));
        }
      }

      _lastHitErrorValue = currentCount > 0 ? hitErrors[currentCount - 1] : 0;
      _previousHitCount = currentCount;
    }
  }
}
