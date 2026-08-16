using System.Collections.Generic;

namespace OsuMate.Services.Osu;

internal sealed class ModifiedHitErrorCache
{
  internal const int Capacity = 64;

  private readonly int[] _ring = new int[Capacity];
  private readonly List<int> _ordered = new(Capacity);
  private int _head;
  private int _size;
  private int _totalCount;
  private int _lastValue;
  private double _speedMultiplier;

  internal (List<int> Values, int TotalCount) Sync(
    IReadOnlyList<int> hitErrors,
    double speedMultiplier
  )
  {
    bool discontinuous =
      speedMultiplier != _speedMultiplier
      || HitErrorHelper.IsDiscontinuous(hitErrors, _totalCount, _lastValue);
    bool changed = discontinuous || hitErrors.Count > _totalCount;

    if (discontinuous)
    {
      _head = 0;
      _size = 0;
      _totalCount = 0;
      _speedMultiplier = speedMultiplier;
    }

    for (int i = _totalCount; i < hitErrors.Count; i++)
      Push(HitErrorHelper.ToModifiedRounded(hitErrors[i], speedMultiplier));

    _totalCount = hitErrors.Count;
    _lastValue = _totalCount > 0 ? hitErrors[_totalCount - 1] : 0;

    if (changed)
      RebuildOrdered();

    return (_ordered, _totalCount);
  }

  private void Push(int value)
  {
    _ring[_head] = value;
    _head = (_head + 1) % Capacity;
    if (_size < Capacity)
      _size++;
  }

  private void RebuildOrdered()
  {
    _ordered.Clear();
    int start = _size < Capacity ? 0 : _head;
    for (int i = 0; i < _size; i++)
      _ordered.Add(_ring[(start + i) % Capacity]);
  }
}
