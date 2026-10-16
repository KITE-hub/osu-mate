using OsuMate.Models;
using OsuMemoryDataProvider;
using OsuMemoryDataProvider.OsuMemoryModels;

namespace OsuMate.Services.Osu
{

  internal sealed class HitErrorSnapshotStore
  {
    private const int MaxPlausibleHitErrorMs = 1000;
    private const int MaxPlausibleJudgementCount = 20000;
    private const int MaxPlausibleCombo = 20000;

    private readonly object _lock = new();
    private readonly OsuBaseAddresses _baseAddresses;

    private int[] _hitErrorArray = [];
    private int _hitErrorCount;
    private int _sourceIndex;

    private HitsResult _lastSafeHits = new();
    private double _lastSafeAccuracy;

    private int _cachedGeneration = -1;
    private (HitsResult Hits, double Accuracy) _cachedHitsAndAccuracy;

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

        if (srcCount < _sourceIndex)
        {
          _hitErrorArray = new int[Math.Max(16, srcCount)];
          _hitErrorCount = 0;
          _sourceIndex = 0;
        }

        if (srcCount == _sourceIndex)
          return (_hitErrorArray, _hitErrorCount);

        if (srcCount > _hitErrorArray.Length)
        {
          var expanded = new int[Math.Max(srcCount, _hitErrorArray.Length * 2)];
          Array.Copy(_hitErrorArray, expanded, _hitErrorCount);
          _hitErrorArray = expanded;
        }

        int stored = _hitErrorCount;
        int index = _sourceIndex;
        try
        {
          for (; index < srcCount; index++)
          {
            int v = src[index];
            if (v > MaxPlausibleHitErrorMs || v < -MaxPlausibleHitErrorMs)
              continue;
            _hitErrorArray[stored++] = v;
          }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
        }

        _sourceIndex = index;
        _hitErrorCount = stored;
        return (_hitErrorArray, _hitErrorCount);
      }
    }

    internal (HitsResult Hits, double Accuracy) ReadHitsAndAccuracy(
      OsuMemoryStatus currentStatus,
      bool isPlaying,
      int generation
    )
    {
      lock (_lock)
      {
        if (generation == _cachedGeneration)
          return _cachedHitsAndAccuracy;

        var hits = new HitsResult();
        hits.SetValueFromMemory(currentStatus, _baseAddresses, isPlaying);
        double accuracy = _baseAddresses.Player.Accuracy;

        if (IsPlausibleHits(hits, accuracy))
        {
          _lastSafeHits = hits;
          _lastSafeAccuracy = accuracy;
        }

        _cachedHitsAndAccuracy = (_lastSafeHits, _lastSafeAccuracy);
        _cachedGeneration = generation;
        return _cachedHitsAndAccuracy;
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
}
