using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using OsuMate.Models;
using OsuMate.Services.Trainer;
using OsuMate.Utils;

namespace OsuMate.Services.PlayLog
{
  public class BeatmapPathResolver
  {
    private static readonly TimeSpan RescanInterval = TimeSpan.FromMinutes(5);

    private readonly OsuMemoryService _memory;
    private readonly object _scanLock = new();
    private string _scannedSongsPath = "";
    private DateTime _lastScanAtUtc = DateTime.MinValue;
    private readonly ConcurrentDictionary<string, DateTime> _missingFileNameAtUtc = new(
      StringComparer.OrdinalIgnoreCase
    );

    private Dictionary<int, OsuMate.Services.StableDb.BeatmapInfo>? _idMapCache;
    private Dictionary<string, OsuMate.Services.StableDb.BeatmapInfo>? _idMapCacheSource;
    private readonly ConcurrentDictionary<int, string> _pathByBeatmapId = new();
    private readonly ConcurrentDictionary<string, string> _pathByFileName = new(
      StringComparer.OrdinalIgnoreCase
    );

    public BeatmapPathResolver(OsuMemoryService memory)
    {
      _memory = memory;
    }

    public string? FindBeatmapPathByMd5(
      string? md5,
      Dictionary<string, OsuMate.Services.StableDb.BeatmapInfo>? md5Map
    )
    {
      if (string.IsNullOrEmpty(md5) || md5Map == null)
        return null;
      if (!md5Map.TryGetValue(md5, out var info))
        return null;
      return ResolveExistingPath(info.FolderName, info.OsuFileName);
    }

    private string? ResolveExistingPath(string? folderName, string? osuFileName)
    {
      if (string.IsNullOrWhiteSpace(folderName) || string.IsNullOrWhiteSpace(osuFileName))
        return null;

      var path = SafePath.CombineWithin(_memory.SongsPath, folderName, osuFileName);
      return path != null && File.Exists(path) ? path : null;
    }

    public string? FindBeatmapPathById(
      int beatmapId,
      Dictionary<string, OsuMate.Services.StableDb.BeatmapInfo>? md5Map
    )
    {
      if (beatmapId <= 0 || md5Map == null)
        return null;

      if (_idMapCache == null || !ReferenceEquals(_idMapCacheSource, md5Map))
      {
        var idMap = new Dictionary<int, OsuMate.Services.StableDb.BeatmapInfo>();
        foreach (var info in md5Map.Values)
        {
          if (info.DifficultyId > 0)
            idMap[info.DifficultyId] = info;
        }
        _idMapCache = idMap;
        _idMapCacheSource = md5Map;
      }

      if (!_idMapCache.TryGetValue(beatmapId, out var beatmapInfo))
        return null;
      return ResolveExistingPath(beatmapInfo.FolderName, beatmapInfo.OsuFileName);
    }

    public string? FindBeatmapPath(int beatmapId)
    {
      if (beatmapId <= 0 || !_memory.IsDirectoryLoaded)
        return null;
      try
      {
        if (_pathByBeatmapId.TryGetValue(beatmapId, out var cachedPath) && File.Exists(cachedPath))
          return cachedPath;

        ScanSongsFolderIfStale();
        if (_pathByBeatmapId.TryGetValue(beatmapId, out var scannedPath) && File.Exists(scannedPath))
          return scannedPath;
      }
      catch (Exception ex)
      {
        LogUtils.DebugLogger("BeatmapPathResolver.FindBeatmapPath failed: " + ex, true);
      }
      return null;
    }

    private void ScanSongsFolderIfStale()
    {
      lock (_scanLock)
      {
        string songsPath = _memory.SongsPath;
        if (songsPath == _scannedSongsPath && DateTime.UtcNow - _lastScanAtUtc < RescanInterval)
          return;

        try
        {
          foreach (var dir in Directory.EnumerateDirectories(songsPath))
          {
            foreach (var file in Directory.EnumerateFiles(dir, "*.osu"))
            {
              var id = ReadBeatmapIdFromFile(file);
              if (id > 0)
                _pathByBeatmapId[id] = file;
            }
          }
        }
        finally
        {
          _scannedSongsPath = songsPath;
          _lastScanAtUtc = DateTime.UtcNow;
        }
      }
    }

    public string? ResolveBeatmapFilePath(
      OsuMemoryDataProvider.OsuMemoryModels.Direct.CurrentBeatmap beatmap,
      Dictionary<string, OsuMate.Services.StableDb.BeatmapInfo>? md5Map = null
    )
    {
      try
      {
        if (!_memory.IsDirectoryLoaded || string.IsNullOrWhiteSpace(_memory.SongsPath))
          return null;

        var candidate = ResolveExistingPath(beatmap.FolderName, beatmap.OsuFileName);
        if (candidate != null)
          return candidate;

        if (beatmap.Id > 0)
        {
          var byId = FindBeatmapPathById(beatmap.Id, md5Map);
          if (byId != null)
            return byId;
        }

        if (!string.IsNullOrWhiteSpace(beatmap.OsuFileName))
        {
          if (
            _pathByFileName.TryGetValue(beatmap.OsuFileName, out var cachedPath)
            && File.Exists(cachedPath)
          )
            return cachedPath;

          if (
            _missingFileNameAtUtc.TryGetValue(beatmap.OsuFileName, out var missedAt)
            && DateTime.UtcNow - missedAt < RescanInterval
          )
            return null;

          var fallback = Directory
            .EnumerateFiles(_memory.SongsPath, "*.osu", SearchOption.AllDirectories)
            .FirstOrDefault(f =>
              Path.GetFileName(f).Equals(beatmap.OsuFileName, StringComparison.OrdinalIgnoreCase)
            );
          if (fallback != null)
          {
            _pathByFileName[beatmap.OsuFileName] = fallback;
            return fallback;
          }

          _missingFileNameAtUtc[beatmap.OsuFileName] = DateTime.UtcNow;
        }

        return null;
      }
      catch (Exception ex)
      {
        LogUtils.DebugLogger(
          "BeatmapPathResolver.ResolveBeatmapFilePath failed: " + ex,
          true
        );
        return null;
      }
    }

    public static string ComputeMd5(string? filePath)
    {
      if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        return "";
      try
      {
        using var md5 = MD5.Create();
        using var stream = File.OpenRead(filePath);
        var hash = md5.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
      }
      catch (Exception ex)
      {
        LogUtils.DebugLogger("BeatmapPathResolver.ComputeMd5 failed: " + ex, true);
        return "";
      }
    }

    public static (
      string artist,
      string title,
      string difficulty,
      string creator
    ) ReadBeatmapMetadataFromFile(string file)
    {
      try
      {
        var bm = OsuBeatmapFile.LoadMetadataOnly(file);
        return (bm.Artist, bm.Title, bm.Version, bm.Creator);
      }
      catch
      {
        return ("", "", "", "");
      }
    }

    public static int? ReadManiaKeyCountFromFile(string file)
    {
      try
      {
        var bm = OsuBeatmapFile.LoadMetadataOnly(file);

        return bm.CircleSize >= 0
          ? LogModeClassifier.GetManiaKeyCount(3, (double)bm.CircleSize)
          : null;
      }
      catch
      {
        return null;
      }
    }

    internal static int ReadBeatmapIdFromFile(string file)
    {
      try
      {
        return OsuBeatmapFile.LoadMetadataOnly(file).BeatmapID;
      }
      catch
      {
        return -1;
      }
    }
  }
}
