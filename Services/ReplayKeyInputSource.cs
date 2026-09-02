using System.Collections;
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

namespace OsuMate.Services;

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

  private readonly Func<string> _osuDirectory;
  private readonly Func<(string BeatmapPath, string BeatmapMd5)> _currentBeatmap;

  private readonly object _reloadLock = new();
  private bool _isReloading;
  private DateTime _lastReloadAttemptUtc = DateTime.MinValue;

  private FrameState[] _frames = EmptyFrames;

  private string _loadedPath = string.Empty;
  private DateTime _loadedLastWriteTimeUtc;
  private int _loadedMode = -1;
  private int _loadedLaneCount;

  public ReplayKeyInputSource(
    Func<string> osuDirectory,
    Func<(string BeatmapPath, string BeatmapMd5)> currentBeatmap
  )
  {
    _osuDirectory = osuDirectory;
    _currentBeatmap = currentBeatmap;
  }

  public KeyOverlaySnapshot GetSnapshot(KeyOverlaySnapshot layout, int mode, double audioTime, double speedMultiplier)
  {
    if (layout.Keys.Length == 0)
      return layout;

    RequestReloadIfDue(mode, layout.Keys.Length);

    var frames = Volatile.Read(ref _frames);
    if (frames.Length == 0)
      return WithPressed(layout, new bool[layout.Keys.Length]);

    var speedRate = 1 / Math.Max(speedMultiplier, 0.01);
    var replayTime = audioTime / speedRate;
    var index = FindFrameIndex(frames, replayTime);
    return WithPressed(layout, index < 0 ? new bool[layout.Keys.Length] : frames[index].Pressed);
  }

  private void RequestReloadIfDue(int mode, int laneCount)
  {
    lock (_reloadLock)
    {
      if (_isReloading || DateTime.UtcNow - _lastReloadAttemptUtc < ReloadInterval)
        return;
      _isReloading = true;
      _lastReloadAttemptUtc = DateTime.UtcNow;
    }

    var beatmap = _currentBeatmap();
    Task.Run(() => ReloadIfChanged(beatmap.BeatmapPath, beatmap.BeatmapMd5, mode, laneCount));
  }

  private void ReloadIfChanged(string beatmapPath, string beatmapMd5, int mode, int laneCount)
  {
    var path = string.Empty;
    var lastWriteTimeUtc = default(DateTime);
    try
    {
      if (string.IsNullOrWhiteSpace(beatmapMd5) || !File.Exists(beatmapPath))
      {
        Volatile.Write(ref _frames, EmptyFrames);
        _loadedPath = string.Empty;
        return;
      }

      path = FindReplayPath(beatmapMd5);
      if (string.IsNullOrEmpty(path))
      {
        Volatile.Write(ref _frames, EmptyFrames);
        _loadedPath = string.Empty;
        return;
      }

      lastWriteTimeUtc = File.GetLastWriteTimeUtc(path);
      if (path == _loadedPath && lastWriteTimeUtc == _loadedLastWriteTimeUtc && mode == _loadedMode && laneCount == _loadedLaneCount)
        return;

      var frames = LoadFrames(path, beatmapPath, mode, laneCount);
      Volatile.Write(ref _frames, frames);
      _loadedPath = path;
      _loadedLastWriteTimeUtc = lastWriteTimeUtc;
      _loadedMode = mode;
      _loadedLaneCount = laneCount;
    }
    catch (Exception e)
    {
      Volatile.Write(ref _frames, EmptyFrames);
      LogUtils.DebugLogger($"ReplayKeyInputSource.ReloadIfChanged failed: {e.Message}", true);
    }
    finally
    {
      lock (_reloadLock)
        _isReloading = false;
    }
  }

  private static FrameState[] LoadFrames(string replayPath, string beatmapPath, int mode, int laneCount)
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
      result.Add(new FrameState(ReadDouble(frame, "Time"), ReadPressed(frame, mode, laneCount)));
    }

    return [.. result.OrderBy(frame => frame.Time)];
  }

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
        .Where(path => IsReplayForBeatmap(path, beatmapMd5))
        .OrderByDescending(File.GetLastWriteTimeUtc)
        .FirstOrDefault() ?? string.Empty;
    }
    catch (Exception e)
    {
      LogUtils.DebugLogger($"ReplayKeyInputSource.FindReplayPath failed: {e.Message}", true);
      return string.Empty;
    }
  }

  private static bool IsReplayForBeatmap(string path, string beatmapMd5)
  {
    try
    {
      using var stream = File.OpenRead(path);
      if (stream.ReadByte() < 0)
        return false;
      Span<byte> version = stackalloc byte[4];
      if (stream.Read(version) != version.Length)
        return false;
      return string.Equals(ReadOsuString(stream), beatmapMd5, StringComparison.OrdinalIgnoreCase);
    }
    catch
    {
      return false;
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

  private static bool[] ReadPressed(object frame, int mode, int laneCount)
  {
    var pressed = new bool[laneCount];
    switch (mode)
    {
      case 0:
        pressed[0] = HasAction(frame, "LeftButton");
        if (laneCount > 1)
          pressed[1] = HasAction(frame, "RightButton");
        break;
      case 1:
        pressed[0] = HasAction(frame, "LeftCentre");
        if (laneCount > 1)
          pressed[1] = HasAction(frame, "RightCentre");
        if (laneCount > 2)
          pressed[2] = HasAction(frame, "LeftRim");
        if (laneCount > 3)
          pressed[3] = HasAction(frame, "RightRim");
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
      keys[i] = new KeyOverlayKeyState(layout.Keys[i].Label, i < pressed.Length && pressed[i]);
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
