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

    private readonly record struct LaneBinding(Keys Key, Keys MouseFallback);

    private sealed class ResolvedKeyLayout
    {
      public static readonly ResolvedKeyLayout Empty = new([], []);

      public string[] Labels { get; }
      public LaneBinding[] Bindings { get; }
      public KeyOverlaySnapshot BlankSnapshot { get; }

      public ResolvedKeyLayout(string[] labels, LaneBinding[] bindings)
      {
        Labels = labels;
        Bindings = bindings;
        if (labels.Length == 0)
        {
          BlankSnapshot = KeyOverlaySnapshot.Empty;
          return;
        }
        var keys = new KeyOverlayKeyState[labels.Length];
        for (var i = 0; i < labels.Length; i++)
          keys[i] = new KeyOverlayKeyState(labels[i], false);
        BlankSnapshot = new KeyOverlaySnapshot(keys);
      }
    }

    private readonly object _layoutLock = new();
    private ResolvedKeyLayout _resolvedLayout = ResolvedKeyLayout.Empty;
    private int _resolvedGamemode = int.MinValue;
    private int _resolvedManiaKeyCount = int.MinValue;

    private readonly object _liveTransitionLock = new();
    private readonly List<RawInputService.KeyTransition> _rawTransitionBuffer = [];
    private LaneBinding[] _liveTransitionBindings = [];
    private int[] _liveTransitionHoldCount = [];
    private bool[] _liveTransitionPressed = [];
    private KeyOverlaySnapshot _cachedLiveSnapshot = KeyOverlaySnapshot.Empty;

    internal KeyOverlaySnapshot DrainKeyOverlayUpdate(
      int gamemode,
      int? maniaKeyCount,
      double audioTime,
      bool isReplay,
      List<KeyOverlayTransition> transitions
    )
    {
      var layout = ResolveLayout(gamemode, gamemode == 3 ? maniaKeyCount : null);
      if (layout.Labels.Length == 0)
      {
        DiscardRawInputTransitions();
        return KeyOverlaySnapshot.Empty;
      }

      if (isReplay)
      {
        DiscardRawInputTransitions();
        return _replayKeyInput.DrainTransitions(layout.BlankSnapshot, gamemode, audioTime, transitions);
      }

      return DrainLiveKeyOverlaySnapshot(layout, transitions);
    }

    private void DiscardRawInputTransitions()
    {
      lock (_liveTransitionLock)
      {
        _rawTransitionBuffer.Clear();
        _rawInput.DrainTransitions(_rawTransitionBuffer);
      }
    }

    private void EnsureLiveTransitionState(ResolvedKeyLayout layout)
    {
      if (ReferenceEquals(_liveTransitionBindings, layout.Bindings))
        return;
      _liveTransitionBindings = layout.Bindings;
      _liveTransitionHoldCount = new int[layout.Bindings.Length];
      _liveTransitionPressed = new bool[layout.Bindings.Length];
      _cachedLiveSnapshot = layout.BlankSnapshot;

      var activeKeys = new List<Keys>(layout.Bindings.Length * 2);
      foreach (var b in layout.Bindings)
      {
        activeKeys.Add(b.Key);
        activeKeys.Add(b.MouseFallback);
      }
      _rawInput.SetActiveKeys(activeKeys);
    }

    private KeyOverlaySnapshot DrainLiveKeyOverlaySnapshot(ResolvedKeyLayout layout, List<KeyOverlayTransition> transitions)
    {
      lock (_liveTransitionLock)
      {
        EnsureLiveTransitionState(layout);

        _rawTransitionBuffer.Clear();
        _rawInput.DrainTransitions(_rawTransitionBuffer);

        var bindings = _liveTransitionBindings;
        var holdCount = _liveTransitionHoldCount;
        var pressed = _liveTransitionPressed;

        foreach (var raw in _rawTransitionBuffer)
        {
          for (var lane = 0; lane < bindings.Length; lane++)
          {
            var binding = bindings[lane];
            if (raw.Key != binding.Key && raw.Key != binding.MouseFallback)
              continue;

            holdCount[lane] = Math.Max(0, holdCount[lane] + (raw.IsDown ? 1 : -1));
            var nowPressed = holdCount[lane] > 0;
            if (nowPressed == pressed[lane])
              continue;

            pressed[lane] = nowPressed;
            transitions.Add(new KeyOverlayTransition(lane, nowPressed, raw.TimestampTicks));
          }
        }

        if (transitions.Count == 0 && _cachedLiveSnapshot.Keys.Length == layout.Labels.Length)
          return _cachedLiveSnapshot;

        var keys = new KeyOverlayKeyState[layout.Labels.Length];
        for (var i = 0; i < keys.Length; i++)
          keys[i] = new KeyOverlayKeyState(layout.Labels[i], pressed[i]);
        _cachedLiveSnapshot = new KeyOverlaySnapshot(keys);
        return _cachedLiveSnapshot;
      }
    }

    private ResolvedKeyLayout ResolveLayout(int gamemode, int? maniaKeyCount)
    {
      var configChanged = EnsureKeyConfigCache();
      var maniaCount = maniaKeyCount ?? -1;

      lock (_layoutLock)
      {
        if (!configChanged && _resolvedGamemode == gamemode && _resolvedManiaKeyCount == maniaCount)
          return _resolvedLayout;

        _resolvedGamemode = gamemode;
        _resolvedManiaKeyCount = maniaCount;
        _resolvedLayout = BuildLayout(gamemode, maniaKeyCount);
        return _resolvedLayout;
      }
    }

    private ResolvedKeyLayout BuildLayout(int gamemode, int? maniaKeyCount) =>
      gamemode switch
      {
        0 => BuildFixedLayout(
          ("keyOsuLeft", "Z", Keys.LButton),
          ("keyOsuRight", "X", Keys.RButton)
        ),
        1 => BuildFixedLayout(
          ("keyTaikoInnerLeft", "X", Keys.None),
          ("keyTaikoInnerRight", "C", Keys.None),
          ("keyTaikoOuterLeft", "Z", Keys.None),
          ("keyTaikoOuterRight", "V", Keys.None)
        ),
        2 => BuildFixedLayout(
          ("keyFruitsDash", "LeftShift", Keys.None),
          ("keyFruitsLeft", "Left", Keys.None),
          ("keyFruitsRight", "Right", Keys.None)
        ),
        3 when maniaKeyCount is >= 1 and <= 18 => BuildManiaLayout(maniaKeyCount.Value),
        _ => ResolvedKeyLayout.Empty,
      };

    private ResolvedKeyLayout BuildFixedLayout(params (string ConfigKey, string Fallback, Keys MouseFallback)[] specs)
    {
      var labels = new string[specs.Length];
      var bindings = new LaneBinding[specs.Length];
      for (var i = 0; i < specs.Length; i++)
      {
        var name = GetConfiguredKeyNameLocked(specs[i].ConfigKey, specs[i].Fallback);
        labels[i] = name;
        bindings[i] = new LaneBinding(
          TryParseKey(name, out var key) ? key : Keys.None,
          specs[i].MouseFallback
        );
      }
      return new ResolvedKeyLayout(labels, bindings);
    }

    private ResolvedKeyLayout BuildManiaLayout(int keyCount)
    {
      var keys = ReadManiaLayoutLocked(keyCount);
      if (keys.Length < keyCount)
        keys = GetFallbackManiaLayout(keyCount);
      else if (keys.Length > keyCount)
        keys = keys[..keyCount];

      var bindings = new LaneBinding[keyCount];
      for (var i = 0; i < keyCount; i++)
        bindings[i] = new LaneBinding(TryParseKey(keys[i], out var key) ? key : Keys.None, Keys.None);

      return new ResolvedKeyLayout(keys, bindings);
    }

    private string[] ReadManiaLayoutLocked(int keyCount)
    {
      var prefix = $"ManiaLayouts{keyCount}K";
      lock (_keyConfigLock)
      {
        return _keyConfigValues.TryGetValue(prefix, out var value)
          ? value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
          : [];
      }
    }

    private string GetConfiguredKeyNameLocked(string keyName, string fallback)
    {
      lock (_keyConfigLock)
        return _keyConfigValues.TryGetValue(keyName, out var value) && !string.IsNullOrWhiteSpace(value)
          ? value
          : fallback;
    }

    private static string[] GetFallbackManiaLayout(int keyCount)
    {
      var fallback = new[]
      {
        "D1", "D2", "D3", "D4", "D5", "D6", "D7", "D8", "D9",
        "Q", "W", "E", "R", "T", "Y", "U", "I", "O"
      };
      return fallback[..Math.Min(keyCount, fallback.Length)];
    }

    private bool EnsureKeyConfigCache()
    {
      var now = DateTime.UtcNow;
      if (now - _keyConfigLastCheckedUtc < KeyConfigRecheckInterval)
        return false;

      lock (_keyConfigLock)
      {
        if (now - _keyConfigLastCheckedUtc < KeyConfigRecheckInterval)
          return false;
        _keyConfigLastCheckedUtc = now;
      }

      var path = FindKeyConfigPath();
      if (!File.Exists(path))
      {
        lock (_keyConfigLock)
        {
          var hadValues = _keyConfigValues.Count > 0 || !string.IsNullOrEmpty(_keyConfigPath);
          _keyConfigPath = string.Empty;
          _keyConfigLastWriteTimeUtc = default;
          _keyConfigValues = [];
          return hadValues;
        }
      }

      DateTime lastWriteTimeUtc;
      try
      {
        lastWriteTimeUtc = File.GetLastWriteTimeUtc(path);
      }
      catch (Exception e)
      {
        LogUtils.DebugLogger($"OsuMemoryService.EnsureKeyConfigCache failed: {e.Message}", true);
        return false;
      }

      lock (_keyConfigLock)
      {
        if (string.Equals(_keyConfigPath, path, StringComparison.Ordinal)
            && _keyConfigLastWriteTimeUtc == lastWriteTimeUtc)
          return false;

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
          return true;
        }
        catch (Exception e)
        {
          LogUtils.DebugLogger($"OsuMemoryService.EnsureKeyConfigCache failed: {e.Message}", true);
          return false;
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
