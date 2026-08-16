using OsuMate.Models;
using OsuMemoryDataProvider;
using OsuMemoryDataProvider.OsuMemoryModels;

namespace OsuMate.Services.Osu;

internal sealed class HitErrorSnapshotStore
{
  private const int MaxPlausibleHitErrorMs = 1000;
  private const int MaxPlausibleJudgementCount = 20000;
  private const int MaxPlausibleCombo = 20000;

  private readonly object _lock = new();
  private readonly OsuBaseAddresses _baseAddresses;

  private int[] _hitErrorArray = [];
  private int _hitErrorCount;

  private HitsResult _lastSafeHits = new();
  private double _lastSafeAccuracy;

  internal HitErrorSnapshotStore(OsuBaseAddresses baseAddresses)
  {
    _baseAddresses = baseAddresses;
  }

  internal void ReadPlayerMemory(StructuredOsuMemoryReader reader)
  {
    lock (_lock)
    {
      reader.TryRead(_baseAddresses.Player);
    }
  }

  internal (int[] Array, int Count) GetHitErrorsSnapshot()
  {
    lock (_lock)
    {
      if (_baseAddresses.Player.HitErrors is not { } src)
        return (_hitErrorArray, 0);

      int srcCount;
      try
      {
        srcCount = src.Count;
      }
      catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
      {
        return (_hitErrorArray, _hitErrorCount);
      }

      if (srcCount < _hitErrorCount)
      {
        _hitErrorArray = new int[Math.Max(16, srcCount)];
        _hitErrorCount = 0;
      }

      if (srcCount == _hitErrorCount)
        return (_hitErrorArray, _hitErrorCount);

      if (srcCount > _hitErrorArray.Length)
      {
        var expanded = new int[Math.Max(srcCount, _hitErrorArray.Length * 2)];
        Array.Copy(_hitErrorArray, expanded, _hitErrorCount);
        _hitErrorArray = expanded;
      }

      try
      {
        for (int i = _hitErrorCount; i < srcCount; i++)
        {
          int v = src[i];
          if (v > MaxPlausibleHitErrorMs || v < -MaxPlausibleHitErrorMs)
            return (_hitErrorArray, _hitErrorCount);
          _hitErrorArray[i] = v;
        }
      }
      catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
      {
        return (_hitErrorArray, _hitErrorCount);
      }

      _hitErrorCount = srcCount;
      return (_hitErrorArray, _hitErrorCount);
    }
  }

  internal (HitsResult Hits, double Accuracy) ReadHitsAndAccuracy(
    OsuMemoryStatus currentStatus,
    bool isPlaying
  )
  {
    lock (_lock)
    {
      var hits = new HitsResult();
      hits.SetValueFromMemory(currentStatus, _baseAddresses, isPlaying);
      double accuracy = _baseAddresses.Player.Accuracy;

      if (!IsPlausibleHits(hits, accuracy))
        return (_lastSafeHits.Clone(), _lastSafeAccuracy);

      _lastSafeHits = hits.Clone();
      _lastSafeAccuracy = accuracy;
      return (hits.Clone(), accuracy);
    }
  }

  private static bool IsPlausibleHits(HitsResult hits, double accuracy)
  {
    if (hits.HitGeki < 0 || hits.HitGeki > MaxPlausibleJudgementCount)
      return false;
    if (hits.Hit300 < 0 || hits.Hit300 > MaxPlausibleJudgementCount)
      return false;
    if (hits.HitKatu < 0 || hits.HitKatu > MaxPlausibleJudgementCount)
      return false;
    if (hits.Hit100 < 0 || hits.Hit100 > MaxPlausibleJudgementCount)
      return false;
    if (hits.Hit50 < 0 || hits.Hit50 > MaxPlausibleJudgementCount)
      return false;
    if (hits.HitMiss < 0 || hits.HitMiss > MaxPlausibleJudgementCount)
      return false;
    if (hits.Combo < 0 || hits.Combo > MaxPlausibleCombo)
      return false;
    if (hits.Score < 0)
      return false;
    if (double.IsNaN(accuracy) || accuracy < 0 || accuracy > 100.5)
      return false;
    return true;
  }
}
