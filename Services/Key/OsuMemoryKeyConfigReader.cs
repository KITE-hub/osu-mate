using System.Windows.Forms;
using OsuMate.Services.Key.Memory;
using OsuMate.Utils;

namespace OsuMate.Services.Key
{

  internal sealed class OsuMemoryKeyConfigReader(
    Func<int> osuProcessId,
    Func<bool> isMemoryReadReady
  ) : IOsuKeyConfigProvider
  {
    private const long RecheckIntervalMs = 1000;
    private const long ErrorLogIntervalMs = 30_000;
    private static readonly TimeSpan InitialScanInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxScanInterval = TimeSpan.FromMinutes(10);

    private readonly object _reloadLock = new();
    private volatile KeyConfigSnapshot _current = KeyConfigSnapshot.Empty;
    private long _nextCheckTick;
    private long _nextErrorLogTick;
    private bool _reloading;
    private volatile bool _dirty;

    private RemoteProcessMemory? _memory;
    private int _memoryProcessId = -1;
    private BindingDictionaryTable? _bindingTable;
    private ManiaLayoutTable? _maniaLayoutTable;
    private int _tablesProcessId = -1;
    private DateTime _lastScanUtc = DateTime.MinValue;
    private TimeSpan _scanInterval = InitialScanInterval;

    public KeyConfigSnapshot Current => _current;

    public bool Refresh(bool isActiveSession)
    {
      if (!isActiveSession)
        TryScheduleReload();

      return ConsumeDirty();
    }

    private bool ConsumeDirty()
    {
      if (!_dirty)
        return false;

      _dirty = false;
      return true;
    }

    private void TryScheduleReload()
    {
      if (Environment.TickCount64 < Volatile.Read(ref _nextCheckTick))
        return;

      lock (_reloadLock)
      {
        var now = Environment.TickCount64;
        if (_reloading || now < _nextCheckTick)
          return;

        _reloading = true;
        Volatile.Write(ref _nextCheckTick, now + RecheckIntervalMs);
      }

      Task.Run(Reload);
    }

    private void Reload()
    {
      try
      {
        if (TryReadSnapshot(out var snapshot))
          Publish(snapshot);
      }
      catch (Exception e)
      {
        LogReloadFailure(e);
      }
      finally
      {
        lock (_reloadLock)
          _reloading = false;
      }
    }

    private void LogReloadFailure(Exception e)
    {
      var now = Environment.TickCount64;
      if (now < _nextErrorLogTick)
        return;

      _nextErrorLogTick = now + ErrorLogIntervalMs;
      LogUtils.DebugLogger($"OsuMemoryKeyConfigReader.Reload failed: {e}", true);
    }

    private bool TryReadSnapshot(out KeyConfigSnapshot snapshot)
    {
      snapshot = KeyConfigSnapshot.Empty;
      var processId = osuProcessId();
      if (processId <= 0 || !isMemoryReadReady())
      {
        ResetTables();
        ReleaseMemory();
        return false;
      }

      var memory = AcquireMemory(processId);
      if (memory == null)
        return false;

      if (_tablesProcessId != processId)
      {
        ResetTables();
        _tablesProcessId = processId;
      }

      if (_bindingTable == null || _maniaLayoutTable == null)
        TryScanMissingTables(memory);

      snapshot = new KeyConfigSnapshot(ReadBindings(memory), ReadManiaLayouts(memory));
      return true;
    }

    private RemoteProcessMemory? AcquireMemory(int processId)
    {
      if (_memory != null && _memoryProcessId == processId)
        return _memory;

      ReleaseMemory();
      _memory = RemoteProcessMemory.TryOpen(processId);
      _memoryProcessId = _memory == null ? -1 : processId;
      return _memory;
    }

    private void ReleaseMemory()
    {
      _memory?.Dispose();
      _memory = null;
      _memoryProcessId = -1;
    }

    private void ResetTables()
    {
      _bindingTable = null;
      _maniaLayoutTable = null;
      _tablesProcessId = -1;
      _lastScanUtc = DateTime.MinValue;
      _scanInterval = InitialScanInterval;
    }

    private TimeSpan NextScanInterval() =>
      _bindingTable != null && _maniaLayoutTable != null
        ? InitialScanInterval
        : TimeSpan.FromTicks(Math.Min(_scanInterval.Ticks * 2, MaxScanInterval.Ticks));

    private void TryScanMissingTables(RemoteProcessMemory memory)
    {
      var now = DateTime.UtcNow;
      if (now - _lastScanUtc < _scanInterval)
        return;

      _lastScanUtc = now;
      var hadBindings = _bindingTable != null;
      var hadManiaLayouts = _maniaLayoutTable != null;
      OsuInputMemoryScanner.Scan(memory, ref _bindingTable, ref _maniaLayoutTable);
      _scanInterval = NextScanInterval();

      LogScanResult("binding dictionary", hadBindings, _bindingTable?.Address);
      LogScanResult("mania layout array", hadManiaLayouts, _maniaLayoutTable?.Address);
    }

    private static void LogScanResult(string target, bool alreadyLocated, uint? address)
    {
      if (alreadyLocated)
        return;

      LogUtils.DebugLogger(
        address.HasValue
          ? $"OsuMemoryKeyConfigReader: {target} located at 0x{address.Value:X8}"
          : $"OsuMemoryKeyConfigReader: {target} not found, previous or default key bindings are used",
        !address.HasValue
      );
    }

    private Dictionary<OsuBinding, Keys> ReadBindings(RemoteProcessMemory memory)
    {
      var bindings = new Dictionary<OsuBinding, Keys>(_current.Bindings);
      if (_bindingTable == null)
        return bindings;

      if (_bindingTable.TryRead(memory, out var entries))
        OsuBindingCatalog.AddBindings(entries, bindings);
      else
        _bindingTable = null;

      return bindings;
    }

    private Dictionary<int, Keys[]> ReadManiaLayouts(RemoteProcessMemory memory)
    {
      var maniaLayouts = new Dictionary<int, Keys[]>(_current.ManiaLayouts);
      if (_maniaLayoutTable == null)
        return maniaLayouts;

      if (!_maniaLayoutTable.TryRead(memory, out var layouts))
      {
        _maniaLayoutTable = null;
        return maniaLayouts;
      }

      foreach (var (keyCount, keys) in layouts)
        maniaLayouts[keyCount] = keys;

      return maniaLayouts;
    }

    private void Publish(KeyConfigSnapshot next)
    {
      if (_current.ContentEquals(next))
        return;

      _current = next;
      _dirty = true;
    }
  }
}
