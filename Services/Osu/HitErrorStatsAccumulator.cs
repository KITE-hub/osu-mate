using System;
using System.Collections.Generic;

namespace OsuMate.Services.Osu;

internal sealed class HitErrorStatsAccumulator
{
  internal readonly record struct Result(
    double RawAvg,
    double ModifiedAvg,
    double ModifiedStdev,
    double RawUR,
    double ModifiedUR,
    double RobustAvg
  );

  private static readonly Result Empty = new(0, 0, 0, 0, 0, 0);

  private const double OutlierModifiedZThreshold = 3.5;

  private const double MadZScale = 0.6745;

  private const int OffsetMin = -1000;
  private const int OffsetMax = 1000;
  private const int BucketCount = OffsetMax - OffsetMin + 1;

  private readonly object _lock = new();

  private int _count;
  private double _mean;
  private double _m2;
  private int _lastValue;

  private readonly long[] _bitCount = new long[BucketCount + 1];
  private readonly long[] _bitSum = new long[BucketCount + 1];

  internal Result Sync(IReadOnlyList<int> hitErrors, double speedMultiplier)
  {
    lock (_lock)
    {
      int newCount = hitErrors.Count;

      if (HitErrorHelper.IsDiscontinuous(hitErrors, _count, _lastValue))
        Reset();

      for (int i = _count; i < newCount; i++)
        Add(hitErrors[i]);

      return ComputeResult(speedMultiplier);
    }
  }

  internal Result GetCurrent(double speedMultiplier)
  {
    lock (_lock)
    {
      return ComputeResult(speedMultiplier);
    }
  }

  private Result ComputeResult(double speedMultiplier)
  {
    if (_count == 0)
      return Empty;

    double rawVariance = _m2 / _count;
    double rawStdev = Math.Sqrt(rawVariance);

    double rawAvg = _mean;
    double modifiedAvg = rawAvg * speedMultiplier;
    double modifiedStdev = rawStdev * speedMultiplier;

    double rawUR = rawStdev * 10;
    double modifiedUR = rawUR * speedMultiplier;

    if (rawUR > 10000)
      rawUR = double.NaN;
    if (modifiedUR > 10000)
      modifiedUR = double.NaN;

    double robustAvg = ComputeRobustAverage();

    return new Result(rawAvg, modifiedAvg, modifiedStdev, rawUR, modifiedUR, robustAvg);
  }

  private double ComputeRobustAverage()
  {
    int n = _count;
    if (n == 0)
      return 0;

    if (n <= 2)
      return _mean;

    double median = GetMedian(n);
    double mad = GetMad(n, median);

    if (mad == 0)
      return median;

    double radius = OutlierModifiedZThreshold * mad / MadZScale;

    int loVal = (int)Math.Ceiling(median - radius);
    int hiVal = (int)Math.Floor(median + radius);
    int loIdx = ToIndex(loVal);
    int hiIdx = ToIndex(hiVal);

    long countIn = BitRangeSum(_bitCount, loIdx, hiIdx);
    long sumIn = BitRangeSum(_bitSum, loIdx, hiIdx);

    return countIn > 0 ? (double)sumIn / countIn : median;
  }

  private double GetMedian(int n)
  {
    if ((n & 1) == 1)
    {
      int idx = FindKth((n + 1) / 2);
      return ToValue(idx);
    }

    int idxLo = FindKth(n / 2);
    int idxHi = FindKth(n / 2 + 1);
    return (ToValue(idxLo) + ToValue(idxHi)) / 2.0;
  }

  private double GetMad(int n, double median)
  {
    long median2 = (long)Math.Round(median * 2.0);

    if ((n & 1) == 1)
    {
      long k = (n + 1) / 2;
      long radius2 = FindDeviationRadius(median2, k);
      return radius2 / 2.0;
    }

    long k1 = n / 2;
    long k2 = n / 2 + 1;
    long r1 = FindDeviationRadius(median2, k1);
    long r2 = FindDeviationRadius(median2, k2);
    return (r1 + r2) / 2.0 / 2.0;
  }

  private long FindDeviationRadius(long median2, long k)
  {
    long lo = 0,
      hi = 2L * (OffsetMax - OffsetMin);
    while (lo < hi)
    {
      long mid = (lo + hi) / 2;
      if (CountWithinDoubledRadius(median2, mid) >= k)
        hi = mid;
      else
        lo = mid + 1;
    }
    return lo;
  }

  private long CountWithinDoubledRadius(long median2, long radius2)
  {
    int xLow = (int)Math.Ceiling((median2 - radius2) / 2.0);
    int xHigh = (int)Math.Floor((median2 + radius2) / 2.0);
    int loIdx = ToIndex(xLow);
    int hiIdx = ToIndex(xHigh);
    return BitRangeSum(_bitCount, loIdx, hiIdx);
  }

  private static int ToIndex(int value) => Math.Clamp(value, OffsetMin, OffsetMax) - OffsetMin + 1;

  private static int ToValue(int index) => index - 1 + OffsetMin;

  private static void BitAdd(long[] bit, int idx, long delta)
  {
    for (; idx < bit.Length; idx += idx & (-idx))
      bit[idx] += delta;
  }

  private static long BitQuery(long[] bit, int idx)
  {
    long sum = 0;
    for (; idx > 0; idx -= idx & (-idx))
      sum += bit[idx];
    return sum;
  }

  private static long BitRangeSum(long[] bit, int loIdx, int hiIdx)
  {
    if (hiIdx < loIdx)
      return 0;
    return BitQuery(bit, hiIdx) - BitQuery(bit, loIdx - 1);
  }

  private int FindKth(long k)
  {
    int lo = 1,
      hi = BucketCount;
    while (lo < hi)
    {
      int mid = (lo + hi) / 2;
      if (BitQuery(_bitCount, mid) >= k)
        hi = mid;
      else
        lo = mid + 1;
    }
    return lo;
  }

  private void Add(int value)
  {
    _count++;
    double delta = value - _mean;
    _mean += delta / _count;
    double delta2 = value - _mean;
    _m2 += delta * delta2;
    _lastValue = value;

    int idx = ToIndex(value);
    BitAdd(_bitCount, idx, 1);
    BitAdd(_bitSum, idx, value);
  }

  private void Reset()
  {
    _count = 0;
    _mean = 0;
    _m2 = 0;
    _lastValue = 0;
    Array.Clear(_bitCount);
    Array.Clear(_bitSum);
  }
}
