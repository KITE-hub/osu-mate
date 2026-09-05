using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using osu.Game.Beatmaps;
using osu.Game.Rulesets;
using osu.Game.Scoring.Legacy;
using OsuMate.Models;
using OsuMate.PPCalculation;
using OsuMate.Services.Osu;
using OsuMate.Utils;

namespace OsuMate.Services.Key;

internal sealed class ReplayKeyInputSource
{
  private sealed class ReplayDecoder : LegacyScoreDecoder
  {
    private readonly WorkingBeatmap _beatmap;

    public ReplayDecoder(string beatmapPath)
    {
      _beatmap = ProcessorWorkingBeatmap.FromFile(beatmapPath);
    }

    protected override Ruleset GetRuleset(int rulesetId) => RulesetHelper.GetRuleset(rulesetId);

    protected override WorkingBeatmap GetBeatmap(string md5Hash) => _beatmap;
  }

  private sealed record FrameState(double Time, bool[] Pressed);

  private static readonly TimeSpan ReloadInterval = TimeSpan.FromSeconds(1);
  private static readonly FrameState[] EmptyFrames = [];
  private const double MaxBridgeReplayTimeMs = 2000;

  private readonly Func<string> _osuDirectory;
  private readonly Func<(string BeatmapPath, string BeatmapMd5)> _currentBeatmap;

  private readonly object _reloadLock = new();
  private bool _isReloading;
  private DateTime _lastReloadAttemptUtc = DateTime.MinValue;
  private int _sessionGeneration;

  private FrameState[] _frames = EmptyFrames;

  private static readonly string[] DefaultTaikoActionOrder = ["LeftCentre", "RightCentre", "LeftRim", "RightRim"];

  private string _loadedPath = string.Empty;
  private string _loadedBeatmapMd5 = string.Empty;
  private DateTime _loadedLastWriteTimeUtc;
  private int _loadedMode = -1;
  private int _loadedLaneCount;
  private int _loadedPlayerLaneOffset;
  private string _loadedTaikoActionOrderKey = string.Empty;

  private double? _lastReplayTimeMs;
  private long _lastRealTicks;
  private bool[] _lastPressed = [];
  private FrameState[] _lastFrames = EmptyFrames;

  public ReplayKeyInputSource(
    Func<string> osuDirectory,
    Func<(string BeatmapPath, string BeatmapMd5)> currentBeatmap
  )
  {
    _osuDirectory = osuDirectory;
    _currentBeatmap = currentBeatmap;
  }

  public KeyOverlaySnapshot DrainTransitions(
    KeyOverlaySnapshot layout,
    int mode,
    double audioTime,
    List<KeyOverlayTransition> transitions,
    int playerLaneOffset = 0,
    string[]? taikoActionOrder = null
  )
  {
    if (layout.Keys.Length == 0)
      return layout;

    RequestReloadIfDue(mode, layout.Keys.Length, playerLaneOffset, taikoActionOrder);

    var laneCount = layout.Keys.Length;
    if (_lastPressed.Length != laneCount)
      _lastPressed = new bool[laneCount];

    var nowTicks = Stopwatch.GetTimestamp();
    var frames = Volatile.Read(ref _frames);
    if (!ReferenceEquals(frames, _lastFrames))
    {
      _lastFrames = frames;
      _lastReplayTimeMs = null;
    }

    if (frames.Length == 0)
    {
      ResetTo(new bool[laneCount], 0, nowTicks);
      return WithPressed(layout, _lastPressed);
    }

    var replayTime = audioTime;

    if (
      _lastReplayTimeMs is not { } previousReplayTime
      || replayTime < previousReplayTime
      || replayTime - previousReplayTime > MaxBridgeReplayTimeMs
    )
    {
      var index = FindFrameIndex(frames, replayTime);
      ResetTo(index < 0 ? new bool[laneCount] : frames[index].Pressed, replayTime, nowTicks);
      return WithPressed(layout, _lastPressed);
    }

    if (replayTime == previousReplayTime)
      return WithPressed(layout, _lastPressed);

    var startIndex = FindFrameIndex(frames, previousReplayTime);
    var endIndex = FindFrameIndex(frames, replayTime);
    var previousRealTicks = _lastRealTicks;

    for (var i = startIndex < 0 ? 0 : startIndex + 1; i <= endIndex; i++)
    {
      var frame = frames[i];
      var eventTicks = InterpolateTicks(previousReplayTime, replayTime, previousRealTicks, nowTicks, frame.Time);
      ApplyFramePressed(frame.Pressed, laneCount, eventTicks, transitions);
    }

    _lastReplayTimeMs = replayTime;
    _lastRealTicks = nowTicks;
    return WithPressed(layout, _lastPressed);
  }

  private void ApplyFramePressed(bool[] framePressed, int laneCount, long eventTicks, List<KeyOverlayTransition> transitions)
  {
    for (var lane = 0; lane < laneCount; lane++)
    {
      var isPressed = lane < framePressed.Length && framePressed[lane];
      if (isPressed == _lastPressed[lane])
        continue;
      _lastPressed[lane] = isPressed;
      transitions.Add(new KeyOverlayTransition(lane, isPressed, eventTicks));
    }
  }

  private void ResetTo(bool[] pressed, double replayTime, long nowTicks)
  {
    var copyLength = Math.Min(pressed.Length, _lastPressed.Length);
    Array.Copy(pressed, _lastPressed, copyLength);
    if (_lastPressed.Length > copyLength)
      Array.Clear(_lastPressed, copyLength, _lastPressed.Length - copyLength);
    _lastReplayTimeMs = replayTime;
    _lastRealTicks = nowTicks;
  }

  private static long InterpolateTicks(double fromReplayTime, double toReplayTime, long fromTicks, long toTicks, double frameTime)
  {
    var span = toReplayTime - fromReplayTime;
    if (span <= 0)
      return toTicks;
    var t = Math.Clamp((frameTime - fromReplayTime) / span, 0, 1);
    return fromTicks + (long)((toTicks - fromTicks) * t);
  }

  private void RequestReloadIfDue(int mode, int laneCount, int playerLaneOffset, string[]? taikoActionOrder)
  {
    int generation;
    lock (_reloadLock)
    {
      if (_isReloading || DateTime.UtcNow - _lastReloadAttemptUtc < ReloadInterval)
        return;
      _isReloading = true;
      _lastReloadAttemptUtc = DateTime.UtcNow;
      generation = _sessionGeneration;
    }

    var beatmap = _currentBeatmap();
    var actionOrderKey = string.Join(',', taikoActionOrder ?? DefaultTaikoActionOrder);
    Task.Run(() => ReloadIfChanged(generation, beatmap.BeatmapPath, beatmap.BeatmapMd5, mode, laneCount, playerLaneOffset, taikoActionOrder, actionOrderKey));
  }

  private void ReloadIfChanged(
    int generation,
    string beatmapPath,
    string beatmapMd5,
    int mode,
    int laneCount,
    int playerLaneOffset,
    string[]? taikoActionOrder,
    string actionOrderKey
  )
  {
    try
    {
      if (string.IsNullOrWhiteSpace(beatmapMd5) || !File.Exists(beatmapPath))
      {
        CommitEmpty(generation);
        return;
      }

      var path = _loadedPath;
      var needsSearch = string.IsNullOrEmpty(path) || beatmapMd5 != _loadedBeatmapMd5;
      if (needsSearch)
      {
        path = FindReplayPath(beatmapMd5);
        if (string.IsNullOrEmpty(path))
        {
          CommitEmpty(generation);
          return;
        }
      }

      var lastWriteTimeUtc = File.GetLastWriteTimeUtc(path);
      if (
        path == _loadedPath
        && lastWriteTimeUtc == _loadedLastWriteTimeUtc
        && mode == _loadedMode
        && laneCount == _loadedLaneCount
        && playerLaneOffset == _loadedPlayerLaneOffset
        && actionOrderKey == _loadedTaikoActionOrderKey
      )
        return;

      var frames = LoadFrames(path, beatmapPath, mode, laneCount, playerLaneOffset, taikoActionOrder);
      lock (_reloadLock)
      {
        if (generation != _sessionGeneration)
          return;
        Volatile.Write(ref _frames, frames);
        _loadedPath = path;
        _loadedBeatmapMd5 = beatmapMd5;
        _loadedLastWriteTimeUtc = lastWriteTimeUtc;
        _loadedMode = mode;
        _loadedLaneCount = laneCount;
        _loadedPlayerLaneOffset = playerLaneOffset;
        _loadedTaikoActionOrderKey = actionOrderKey;
      }
    }
    catch (Exception e)
    {
      CommitEmpty(generation);
      LogUtils.DebugLogger($"ReplayKeyInputSource.ReloadIfChanged failed: {e.Message}", true);
    }
    finally
    {
      lock (_reloadLock)
        _isReloading = false;
    }
  }

  private void CommitEmpty(int generation)
  {
    lock (_reloadLock)
    {
      if (generation != _sessionGeneration)
        return;
      Volatile.Write(ref _frames, EmptyFrames);
      _loadedPath = string.Empty;
      _loadedBeatmapMd5 = string.Empty;
    }
  }

  internal void NotifySessionStart()
  {
    lock (_reloadLock)
    {
      _loadedPath = string.Empty;
      _loadedBeatmapMd5 = string.Empty;
      _lastReloadAttemptUtc = DateTime.MinValue;
      _sessionGeneration++;
    }
  }

  private static FrameState[] LoadFrames(string replayPath, string beatmapPath, int mode, int laneCount, int playerLaneOffset, string[]? taikoActionOrder)
  {
    using var stream = File.OpenRead(replayPath);
    var score = new ReplayDecoder(beatmapPath).Parse(stream);
    var replay = GetMemberValue(score, "Replay");
    var frames = GetMemberValue(replay, "Frames") as IEnumerable
      ?? throw new InvalidDataException("Replay frames were not found.");

    var result = new List<FrameState>();
    foreach (var frame in frames)
    {
      if (frame == null)
        continue;
      result.Add(new FrameState(ReadDouble(frame, "Time"), ReadPressed(frame, mode, laneCount, playerLaneOffset, taikoActionOrder)));
    }

    return [.. result.OrderBy(frame => frame.Time)];
  }

  private readonly Dictionary<string, (DateTime WriteTimeUtc, string Md5)> _replayMd5Cache = [];

  private string FindReplayPath(string beatmapMd5)
  {
    try
    {
      if (string.IsNullOrWhiteSpace(beatmapMd5))
        return string.Empty;
      var root = _osuDirectory();
      var candidates = new[] { Path.Combine(root, "Data", "r"), Path.Combine(root, "Replays") };
      return candidates
        .Where(Directory.Exists)
        .SelectMany(path => Directory.EnumerateFiles(path, "*.osr", SearchOption.TopDirectoryOnly))
        .OrderByDescending(File.GetLastWriteTimeUtc)
        .FirstOrDefault(path => string.Equals(GetCachedReplayMd5(path), beatmapMd5, StringComparison.OrdinalIgnoreCase))
        ?? string.Empty;
    }
    catch (Exception e)
    {
      LogUtils.DebugLogger($"ReplayKeyInputSource.FindReplayPath failed: {e.Message}", true);
      return string.Empty;
    }
  }

  private string GetCachedReplayMd5(string path)
  {
    var writeTimeUtc = File.GetLastWriteTimeUtc(path);
    if (_replayMd5Cache.TryGetValue(path, out var cached) && cached.WriteTimeUtc == writeTimeUtc)
      return cached.Md5;

    var md5 = ReadReplayBeatmapMd5(path) ?? string.Empty;
    _replayMd5Cache[path] = (writeTimeUtc, md5);
    return md5;
  }

  private static string? ReadReplayBeatmapMd5(string path)
  {
    try
    {
      using var stream = File.OpenRead(path);
      if (stream.ReadByte() < 0)
        return null;
      Span<byte> version = stackalloc byte[4];
      return stream.Read(version) != version.Length ? null : ReadOsuString(stream);
    }
    catch
    {
      return null;
    }
  }

  private static string? ReadOsuString(Stream stream)
  {
    var marker = stream.ReadByte();
    if (marker == 0)
      return null;
    if (marker != 0x0b)
      return null;

    var length = 0;
    var shift = 0;
    while (shift <= 28)
    {
      var value = stream.ReadByte();
      if (value < 0)
        return null;
      length |= (value & 0x7f) << shift;
      if ((value & 0x80) == 0)
        break;
      shift += 7;
    }

    if (length < 0 || length > stream.Length - stream.Position)
      return null;
    var bytes = new byte[length];
    return stream.Read(bytes, 0, bytes.Length) == bytes.Length
      ? System.Text.Encoding.UTF8.GetString(bytes)
      : null;
  }

  private static int FindFrameIndex(FrameState[] frames, double time)
  {
    var low = 0;
    var high = frames.Length - 1;
    var result = -1;
    while (low <= high)
    {
      var middle = low + (high - low) / 2;
      if (frames[middle].Time <= time)
      {
        result = middle;
        low = middle + 1;
      }
      else
      {
        high = middle - 1;
      }
    }
    return result;
  }

  private static bool[] ReadPressed(object frame, int mode, int laneCount, int playerLaneOffset, string[]? taikoActionOrder)
  {
    var pressed = new bool[laneCount];
    switch (mode)
    {
      case 0:
        if (playerLaneOffset < laneCount)
          pressed[playerLaneOffset] = HasAction(frame, "LeftButton");
        if (playerLaneOffset + 1 < laneCount)
          pressed[playerLaneOffset + 1] = HasAction(frame, "RightButton");
        break;
      case 1:
        var actionOrder = taikoActionOrder ?? DefaultTaikoActionOrder;
        for (var i = 0; i < actionOrder.Length; i++)
        {
          var laneIndex = playerLaneOffset + i;
          if (laneIndex < laneCount)
            pressed[laneIndex] = HasAction(frame, actionOrder[i]);
        }
        break;
      case 2:
        pressed[0] = HasAction(frame, "Dash");
        if (laneCount > 1)
          pressed[1] = HasAction(frame, "MoveLeft");
        if (laneCount > 2)
          pressed[2] = HasAction(frame, "MoveRight");
        break;
      case 3:
        if (GetMemberValue(frame, "Actions") is IEnumerable actions)
        {
          foreach (var action in actions)
          {
            var name = action?.ToString();
            if (name?.StartsWith("Key", StringComparison.Ordinal) != true)
              continue;
            if (int.TryParse(name[3..], out var key) && key is >= 1 and <= 18 && key <= laneCount)
              pressed[key - 1] = true;
          }
        }
        break;
    }
    return pressed;
  }

  private static KeyOverlaySnapshot WithPressed(KeyOverlaySnapshot layout, bool[] pressed)
  {
    var keys = new KeyOverlayKeyState[layout.Keys.Length];
    for (var i = 0; i < keys.Length; i++)
      keys[i] = new KeyOverlayKeyState(layout.Keys[i].Label, i < pressed.Length && pressed[i], layout.Keys[i].Role);
    return new KeyOverlaySnapshot(keys);
  }

  private static object? GetMemberValue(object? source, string name)
  {
    if (source == null)
      return null;
    var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    var type = source.GetType();
    return type.GetProperty(name, flags)?.GetValue(source) ?? type.GetField(name, flags)?.GetValue(source);
  }

  private static bool HasAction(object frame, string actionName)
  {
    if (GetMemberValue(frame, "Actions") is not IEnumerable actions)
      return false;
    foreach (var action in actions)
    {
      if (string.Equals(action?.ToString(), actionName, StringComparison.Ordinal))
        return true;
    }
    return false;
  }

  private static double ReadDouble(object source, string name) => Convert.ToDouble(GetMemberValue(source, name) ?? 0d);
}
