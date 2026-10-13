using System.IO;
using System.Windows.Forms;
using OsuMate.Models;
using OsuMate.Services.Key;
using OsuMate.Services.Osu;
using OsuMate.Utils;
using OsuMemoryDataProvider;
using OsuMemoryDataProvider.OsuMemoryModels;

namespace OsuMate.Services
{
  public class OsuMemoryService
  {
    private readonly StructuredOsuMemoryReader _sreader = StructuredOsuMemoryReader.GetInstance(
      new(ProcessUtils.OsuProcessName)
    );

    private readonly OsuBaseAddresses _baseAddresses = new();

    private readonly OsuDirectoryResolver _directoryResolver = new();
    private readonly HitErrorSnapshotStore _hitErrorStore;
    private readonly UrTimelineStore _urTimelineStore;

    public event Action? OnMemoryRead;
    public event Action<IntPtr>? OnOsuWindowFound;

    public event Action<OsuMemoryStatus, OsuMemoryStatus>? OnStatusChanged;

    public event Action<string>? OnOsuDirectoryLoaded
    {
      add => _directoryResolver.OnDirectoryLoaded += value;
      remove => _directoryResolver.OnDirectoryLoaded -= value;
    }

    internal bool IsOsuRunning { get; private set; }
    internal bool IsDirectoryLoaded => _directoryResolver.IsDirectoryLoaded;
    internal bool IsPlaying { get; private set; }
    internal bool IsResultScreen { get; private set; }
    internal int CurrentOsuGamemode { get; private set; }
    internal OsuMemoryStatus CurrentStatus { get; private set; }
    private OsuMemoryStatus _prevStatus = OsuMemoryStatus.Unknown;
    internal string OsuDirectory => _directoryResolver.OsuDirectory;
    internal string SongsPath => _directoryResolver.SongsPath;

    internal string ManualOsuDirectory => _directoryResolver.ManualOsuDirectory;

    internal string[] PrevMods { get; set; } = [];
    internal double FirstObjectTimeModified
    {
      get => _urTimelineStore.FirstObjectTimeModified;
      set => _urTimelineStore.FirstObjectTimeModified = value;
    }

    private string _cachedOsuPath = string.Empty;
    private IntPtr _lastMainWindowHandle = IntPtr.Zero;

    private const int MemoryReadWarmupMs = 6000;
    private int _lastSeenOsuPid = -1;
    private DateTime? _osuWindowSeenAt = null;
    private volatile bool _isMemoryReadReady = false;

    internal bool IsMemoryReadReady => _isMemoryReadReady;

    public OsuMemoryService(RawInputService rawInput)
    {
      _keyOverlay = new KeyOverlayInputService(rawInput, () => OsuDirectory, () => IsPlaying);
      _hitErrorStore = new HitErrorSnapshotStore(_baseAddresses);
      _urTimelineStore = new UrTimelineStore(_baseAddresses);
    }

    internal OsuBaseAddresses GetBaseAddressSnapshot() => _baseAddresses;

    private sealed record BeatmapPathSnapshot(string FolderName, string OsuFileName, string Md5);

    private volatile BeatmapPathSnapshot? _beatmapSnapshot;

    private void PublishBeatmapSnapshot()
    {
      var beatmap = _baseAddresses.Beatmap;
      var next = new BeatmapPathSnapshot(
        beatmap.FolderName?.Trim() ?? string.Empty,
        beatmap.OsuFileName?.Trim() ?? string.Empty,
        beatmap.Md5?.Trim() ?? string.Empty
      );

      if (!next.Equals(_beatmapSnapshot))
        _beatmapSnapshot = next;
    }

    internal (string BeatmapPath, string BeatmapMd5) GetCurrentBeatmap()
    {
      var snapshot = _beatmapSnapshot;
      if (snapshot == null)
        return (string.Empty, string.Empty);

      try
      {
        return (
          SafePath.CombineWithin(SongsPath, snapshot.FolderName, snapshot.OsuFileName)
            ?? string.Empty,
          snapshot.Md5
        );
      }
      catch (Exception e)
      {
        LogUtils.DebugLogger($"OsuMemoryService.GetCurrentBeatmap failed: {e}", true);
        return (string.Empty, string.Empty);
      }
    }

    internal void SetManualOsuDirectory(string directory) =>
      _directoryResolver.SetManualDirectory(directory);

    internal (int[] Array, int Count) GetHitErrorsSnapshot() =>
      _hitErrorStore.GetHitErrorsSnapshot();

    internal (HitsResult Hits, double Accuracy) ReadHitsAndAccuracy() =>
      _hitErrorStore.ReadHitsAndAccuracy(CurrentStatus, IsPlaying);

    internal (bool Reset, List<(double timeSec, double offsetMs)> NewItems) GetURTimelineSnapshot() =>
      _urTimelineStore.GetSnapshot();

    private readonly KeyOverlayInputService _keyOverlay;

    internal KeyOverlaySnapshot DrainKeyOverlayUpdate(
      int gamemode,
      int? maniaKeyCount,
      List<KeyOverlayTransition> transitions
    ) => _keyOverlay.DrainKeyOverlayUpdate(gamemode, maniaKeyCount, transitions);

    internal void SyncURTimeline(IReadOnlyList<int> hitErrors, double speedMultiplier) =>
      _urTimelineStore.Sync(hitErrors, speedMultiplier);

    private Task? _processMonitorTask;
    private Task? _memoryReaderTask;

    internal void StartProcessMonitor(CancellationToken ct = default)
    {
      _processMonitorTask = Task.Run(
        async () =>
        {
          while (!ct.IsCancellationRequested)
          {
            try
            {
              await Task.Delay(1000, ct).ConfigureAwait(false);
              var (running, path, handle, pid) = ProcessUtils.GetOsuProcess();
              IsOsuRunning = running;

              if (!running)
              {
                _lastMainWindowHandle = IntPtr.Zero;

                _lastSeenOsuPid = -1;
                _osuWindowSeenAt = null;
                _isMemoryReadReady = false;
              }
              else
              {
                if (pid != _lastSeenOsuPid)
                {
                  _lastSeenOsuPid = pid;
                  _osuWindowSeenAt = null;
                  _isMemoryReadReady = false;
                }

                if (handle != IntPtr.Zero)
                {
                  if (handle != _lastMainWindowHandle)
                  {
                    _lastMainWindowHandle = handle;
                    OnOsuWindowFound?.Invoke(handle);
                  }

                  _osuWindowSeenAt ??= DateTime.UtcNow;
                }

                if (
                  !_isMemoryReadReady
                  && _osuWindowSeenAt.HasValue
                  && (DateTime.UtcNow - _osuWindowSeenAt.Value).TotalMilliseconds
                    >= MemoryReadWarmupMs
                )
                {
                  _isMemoryReadReady = true;
                }
              }

              if (!string.IsNullOrEmpty(path))
                _cachedOsuPath = path;
            }
            catch (TaskCanceledException)
            {
              break;
            }
            catch (Exception e)
            {
              LogUtils.DebugLogger(e.ToString(), true);
            }
          }
        },
        ct
      );
    }

    internal void StartMemoryReader(
      Func<int>? intervalMsProvider = null,
      CancellationToken ct = default
    )
    {
      var getIntervalMs = intervalMsProvider ?? (() => 16);

      _memoryReaderTask = Task.Run(
        async () =>
        {
          while (!ct.IsCancellationRequested)
          {
            try
            {
              await Task.Delay(Math.Max(1, getIntervalMs()), ct).ConfigureAwait(false);

              _directoryResolver.TryResolve(_cachedOsuPath);

              if (!IsOsuRunning || !_isMemoryReadReady)
                continue;
              if (!IsDirectoryLoaded || !_sreader.CanRead)
                continue;

              _sreader.TryRead(_baseAddresses.Beatmap);
              PublishBeatmapSnapshot();
              _hitErrorStore.ReadPlayerMemory(_sreader);
              _sreader.TryRead(_baseAddresses.GeneralData);
              _sreader.TryRead(_baseAddresses.ResultsScreen);

              var newStatus = _baseAddresses.GeneralData.OsuStatus;
              CurrentStatus = newStatus;
              IsPlaying = CurrentStatus == OsuMemoryStatus.Playing;
              IsResultScreen = CurrentStatus == OsuMemoryStatus.ResultsScreen;
              CurrentOsuGamemode = CurrentStatus switch
              {
                OsuMemoryStatus.Playing => _baseAddresses.Player.Mode,
                OsuMemoryStatus.ResultsScreen => _baseAddresses.ResultsScreen.Mode,
                _ => _baseAddresses.GeneralData.GameMode,
              };

              if (newStatus != _prevStatus)
              {
                var prev = _prevStatus;
                _prevStatus = newStatus;
                InvokeIsolated(OnStatusChanged, prev, newStatus);
              }

              InvokeIsolated(OnMemoryRead);
            }
            catch (TaskCanceledException)
            {
              break;
            }
            catch (Exception e)
            {
              LogUtils.DebugLogger("OsuMemoryService memory reader failed: " + e, true);
            }
          }
        },
        ct
      );
    }

    private static void InvokeIsolated(Action? handlers)
    {
      if (handlers == null)
        return;

      foreach (var handler in handlers.GetInvocationList().Cast<Action>())
      {
        try
        {
          handler();
        }
        catch (Exception e)
        {
          LogUtils.DebugLogger("OsuMemoryService.OnMemoryRead handler failed: " + e, true);
        }
      }
    }

    private static void InvokeIsolated(
      Action<OsuMemoryStatus, OsuMemoryStatus>? handlers,
      OsuMemoryStatus prev,
      OsuMemoryStatus current
    )
    {
      if (handlers == null)
        return;

      foreach (var handler in handlers.GetInvocationList().Cast<Action<OsuMemoryStatus, OsuMemoryStatus>>())
      {
        try
        {
          handler(prev, current);
        }
        catch (Exception e)
        {
          LogUtils.DebugLogger("OsuMemoryService.OnStatusChanged handler failed: " + e, true);
        }
      }
    }

    internal Task StopAsync()
    {
      var tasks = new List<Task>(2);
      if (_processMonitorTask != null)
        tasks.Add(_processMonitorTask);
      if (_memoryReaderTask != null)
        tasks.Add(_memoryReaderTask);
      return tasks.Count > 0 ? Task.WhenAll(tasks) : Task.CompletedTask;
    }
  }
}
