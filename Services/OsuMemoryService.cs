using System.IO;
using System.Windows.Forms;
using OsuMate.Models;
using OsuMate.Services.Osu;
using OsuMate.Utils;
using OsuMemoryDataProvider;
using OsuMemoryDataProvider.OsuMemoryModels;

namespace OsuMate.Services
{
  public class OsuMemoryService
  {
    private readonly StructuredOsuMemoryReader _sreader = StructuredOsuMemoryReader.GetInstance(
      new("osu!")
    );

    private readonly OsuBaseAddresses _baseAddresses = new();

    private readonly OsuDirectoryResolver _directoryResolver = new();
    private readonly RawInputService _rawInput;
    private readonly ReplayKeyInputSource _replayKeyInput;
    private readonly HitErrorSnapshotStore _hitErrorStore;
    private readonly UrTimelineStore _urTimelineStore;
    private static readonly TimeSpan KeyConfigRecheckInterval = TimeSpan.FromSeconds(1);
    private readonly object _keyConfigLock = new();
    private string _keyConfigPath = string.Empty;
    private DateTime _keyConfigLastWriteTimeUtc;
    private DateTime _keyConfigLastCheckedUtc = DateTime.MinValue;
    private Dictionary<string, string> _keyConfigValues = [];

    public event Action? OnMemoryRead;
    public event Action? OnKeyInputChanged;
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
      _rawInput = rawInput;
      _replayKeyInput = new ReplayKeyInputSource(() => OsuDirectory, GetCurrentBeatmap);
      _rawInput.InputChanged += () => OnKeyInputChanged?.Invoke();
      _hitErrorStore = new HitErrorSnapshotStore(_baseAddresses);
      _urTimelineStore = new UrTimelineStore(_baseAddresses);
    }

    internal OsuBaseAddresses GetBaseAddressSnapshot() => _baseAddresses;

    private (string BeatmapPath, string BeatmapMd5) GetCurrentBeatmap()
    {
      var beatmap = _baseAddresses.Beatmap;
      try
      {
        return (
          Path.Combine(SongsPath, beatmap.FolderName?.Trim() ?? string.Empty, beatmap.OsuFileName?.Trim() ?? string.Empty),
          beatmap.Md5?.Trim() ?? string.Empty
        );
      }
      catch (Exception e)
      {
        LogUtils.DebugLogger($"OsuMemoryService.GetCurrentBeatmap failed: {e.Message}", true);
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

    internal KeyOverlaySnapshot GetKeyOverlaySnapshot(
      int gamemode,
      int? maniaKeyCount,
      double audioTime,
      double speedMultiplier,
      bool isReplay
    )
    {
      var snapshot = gamemode switch
      {
        0 => CreateSnapshot(
          [GetConfiguredKeyName("keyOsuLeft", "Z"), GetConfiguredKeyName("keyOsuRight", "X")],
          [
            IsConfiguredKeyPressed("keyOsuLeft", "Z") || _rawInput.IsPressed(Keys.LButton),
            IsConfiguredKeyPressed("keyOsuRight", "X") || _rawInput.IsPressed(Keys.RButton)
          ]
        ),
        1 => CreateSnapshot(
          [
            GetConfiguredKeyName("keyTaikoInnerLeft", "X"),
            GetConfiguredKeyName("keyTaikoInnerRight", "C"),
            GetConfiguredKeyName("keyTaikoOuterLeft", "Z"),
            GetConfiguredKeyName("keyTaikoOuterRight", "V")
          ],
          [
            IsConfiguredKeyPressed("keyTaikoInnerLeft", "X"),
            IsConfiguredKeyPressed("keyTaikoInnerRight", "C"),
            IsConfiguredKeyPressed("keyTaikoOuterLeft", "Z"),
            IsConfiguredKeyPressed("keyTaikoOuterRight", "V")
          ]
        ),
        2 => CreateSnapshot(
          [
            GetConfiguredKeyName("keyFruitsDash", "LeftShift"),
            GetConfiguredKeyName("keyFruitsLeft", "Left"),
            GetConfiguredKeyName("keyFruitsRight", "Right")
          ],
          [
            IsConfiguredKeyPressed("keyFruitsDash", "LeftShift"),
            IsConfiguredKeyPressed("keyFruitsLeft", "Left"),
            IsConfiguredKeyPressed("keyFruitsRight", "Right")
          ]
        ),
        3 when maniaKeyCount is >= 1 and <= 18 => GetManiaKeyOverlaySnapshot(maniaKeyCount.Value),
        _ => KeyOverlaySnapshot.Empty,
      };
      return isReplay
        ? _replayKeyInput.GetSnapshot(snapshot, gamemode, audioTime, speedMultiplier)
        : snapshot;
    }

    private KeyOverlaySnapshot GetManiaKeyOverlaySnapshot(int keyCount)
    {
      var keys = ReadManiaLayout(keyCount);
      if (keys.Length < keyCount)
        keys = GetFallbackManiaLayout(keyCount);
      else if (keys.Length > keyCount)
        keys = keys[..keyCount];

      var states = new bool[keyCount];
      for (int i = 0; i < keyCount; i++)
      {
        states[i] = TryParseKey(keys[i], out var key) && _rawInput.IsPressed(key);
      }

      return CreateSnapshot(keys, states);
    }

    private string[] ReadManiaLayout(int keyCount)
    {
      EnsureKeyConfigCache();
      var prefix = $"ManiaLayouts{keyCount}K";
      lock (_keyConfigLock)
      {
        return _keyConfigValues.TryGetValue(prefix, out var value)
          ? value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
          : [];
      }
    }

    private string GetConfiguredKeyName(string keyName, string fallback)
    {
      EnsureKeyConfigCache();
      lock (_keyConfigLock)
        return _keyConfigValues.TryGetValue(keyName, out var value) && !string.IsNullOrWhiteSpace(value)
          ? value
          : fallback;
    }

    private bool IsConfiguredKeyPressed(string keyName, string fallback) =>
      TryParseKey(GetConfiguredKeyName(keyName, fallback), out var key) && _rawInput.IsPressed(key);

    private static string[] GetFallbackManiaLayout(int keyCount)
    {
      var fallback = new[]
      {
        "D1", "D2", "D3", "D4", "D5", "D6", "D7", "D8", "D9",
        "Q", "W", "E", "R", "T", "Y", "U", "I", "O"
      };
      return fallback[..Math.Min(keyCount, fallback.Length)];
    }

    private void EnsureKeyConfigCache()
    {
      var now = DateTime.UtcNow;
      lock (_keyConfigLock)
      {
        if (now - _keyConfigLastCheckedUtc < KeyConfigRecheckInterval)
          return;
        _keyConfigLastCheckedUtc = now;
      }

      var path = FindKeyConfigPath();
      if (!File.Exists(path))
      {
        lock (_keyConfigLock)
        {
          _keyConfigPath = string.Empty;
          _keyConfigLastWriteTimeUtc = default;
          _keyConfigValues = [];
        }
        return;
      }

      DateTime lastWriteTimeUtc;
      try
      {
        lastWriteTimeUtc = File.GetLastWriteTimeUtc(path);
      }
      catch (Exception e)
      {
        LogUtils.DebugLogger($"OsuMemoryService.EnsureKeyConfigCache failed: {e.Message}", true);
        return;
      }

      lock (_keyConfigLock)
      {
        if (string.Equals(_keyConfigPath, path, StringComparison.Ordinal)
            && _keyConfigLastWriteTimeUtc == lastWriteTimeUtc)
          return;

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
          foreach (var line in File.ReadLines(path))
          {
            var separator = line.IndexOf('=');
            if (separator <= 0)
              continue;

            var key = line[..separator].Trim();
            if (key.StartsWith("ManiaLayouts", StringComparison.Ordinal)
                || key.StartsWith("keyOsu", StringComparison.Ordinal)
                || key.StartsWith("keyTaiko", StringComparison.Ordinal)
                || key.StartsWith("keyFruits", StringComparison.Ordinal))
              values[key] = line[(separator + 1)..].Trim();
          }

          _keyConfigPath = path;
          _keyConfigLastWriteTimeUtc = lastWriteTimeUtc;
          _keyConfigValues = values;
        }
        catch (Exception e)
        {
          LogUtils.DebugLogger($"OsuMemoryService.EnsureKeyConfigCache failed: {e.Message}", true);
        }
      }
    }

    private string FindKeyConfigPath()
    {
      try
      {
        return Directory
          .EnumerateFiles(OsuDirectory, "osu!.*.cfg", SearchOption.TopDirectoryOnly)
          .Where(path => !string.Equals(Path.GetFileName(path), "osu.cfg", StringComparison.OrdinalIgnoreCase))
          .OrderByDescending(File.GetLastWriteTimeUtc)
          .FirstOrDefault() ?? string.Empty;
      }
      catch (Exception e)
      {
        LogUtils.DebugLogger($"OsuMemoryService.FindKeyConfigPath failed: {e.Message}", true);
        return string.Empty;
      }
    }

    private static KeyOverlaySnapshot CreateSnapshot(IReadOnlyList<string> labels, IReadOnlyList<bool> pressed)
    {
      if (labels.Count == 0 || labels.Count != pressed.Count)
        return KeyOverlaySnapshot.Empty;

      var items = new KeyOverlayKeyState[labels.Count];
      for (int i = 0; i < labels.Count; i++)
        items[i] = new KeyOverlayKeyState(labels[i], pressed[i]);
      return new KeyOverlaySnapshot(items);
    }

    private static bool TryParseKey(string name, out Keys key)
    {
      if (Enum.TryParse<Keys>(name, true, out key) && key != Keys.None)
        return true;

      var normalized = name switch
      {
        "LeftControl" => "LControlKey",
        "RightControl" => "RControlKey",
        "LeftShift" => "LShiftKey",
        "RightShift" => "RShiftKey",
        "LeftWindows" => "LWin",
        "RightWindows" => "RWin",
        "Backspace" => "Back",
        "Enter" => "Return",
        _ => name,
      };

      return Enum.TryParse(normalized, true, out key) && key != Keys.None;
    }

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
              LogUtils.DebugLogger(e.Message, true);
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
              _hitErrorStore.ReadPlayerMemory(_sreader);
              _sreader.TryRead(_baseAddresses.GeneralData);
              _sreader.TryRead(_baseAddresses.ResultsScreen);
              _sreader.TryRead(_baseAddresses.KeyOverlay);

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
                OnStatusChanged?.Invoke(prev, newStatus);
              }

              OnMemoryRead?.Invoke();
            }
            catch (TaskCanceledException)
            {
              break;
            }
            catch (Exception e)
            {
              LogUtils.DebugLogger(e.Message, true);
            }
          }
        },
        ct
      );
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
