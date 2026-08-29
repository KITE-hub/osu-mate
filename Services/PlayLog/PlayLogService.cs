using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using OsuMate.Models;
using OsuMate.Services.Osu;
using OsuMate.Utils;
using OsuMemoryDataProvider;

namespace OsuMate.Services.PlayLog
{
  public class PlayLogService : IDisposable
  {
    private readonly OsuMemoryService _memory;
    private readonly PpCalculationService _ppService;
    private Dispatcher? _uiDispatcher;
    private readonly PlayLogRepository _repository;
    private readonly BeatmapPathResolver _pathResolver;
    private readonly PlayLogSrPpEnricher _srPpEnricher;
    private readonly HistoricalImporter _historicalImporter;
    private readonly SemaphoreSlim _loadGate = new(1, 1);

    private bool _isRealtimeTrackingEnabled = true;

    private PlaySessionSnapshot? _currentSession;

    public ObservableCollection<PlayLogEntry> Entries { get; } = [];

    private readonly ConcurrentDictionary<string, PlayLogEntry> _entriesByKey = new();

    private Dictionary<string, OsuMate.Services.StableDb.BeatmapInfo>? _md5Map;

    public void AttachUiDispatcher(Dispatcher dispatcher)
    {
      _uiDispatcher = dispatcher;
    }

    public PlayLogService(
      OsuMemoryService memory,
      PpCalculationService ppService,
      PlayLogRepository repository,
      BeatmapPathResolver pathResolver,
      PlayLogSrPpEnricher srPpEnricher,
      HistoricalImporter historicalImporter
    )
    {
      _memory = memory;
      _ppService = ppService;
      _repository = repository;
      _pathResolver = pathResolver;
      _srPpEnricher = srPpEnricher;
      _historicalImporter = historicalImporter;

      _memory.OnStatusChanged += HandleStatusChanged;

      _memory.OnMemoryRead += HandleMemoryTick;

      _memory.OnOsuDirectoryLoaded += HandleOsuDirectoryLoaded;
    }

    private void HandleOsuDirectoryLoaded(string osuDir)
    {
      var unused = Task.Run(async () =>
      {
        try
        {
          await HandleOsuDirectoryLoadedAsync();
        }
        catch (Exception ex)
        {
          LogUtils.DebugLogger(
            "PlayLogService.OnOsuDirectoryLoaded: Exception occurred during processing: " + ex,
            true
          );
        }
      });
    }

    private async Task HandleOsuDirectoryLoadedAsync()
    {
      await _loadGate.WaitAsync();
      try
      {
        var newEntries = _historicalImporter.LoadFromLocalOsuData(out var md5Map);

        _md5Map ??= md5Map;

        var persistedEntries = _repository.LoadAllFromDisk();
        var persistedByKey = SelectBestByDedupeKey(persistedEntries);

        var entriesToSave = new List<(PlayLogEntry Entry, string? OldDedupeKey, DateTime? OldPlayedAt)>();

        var uiDispatcher = _uiDispatcher;
        if (uiDispatcher == null)
          return;

        await uiDispatcher.InvokeAsync(() =>
        {
          void InsertSorted(PlayLogEntry item)
          {
            int index = FindInsertIndexDescending(Entries, item.PlayedAt);
            Entries.Insert(index, item);
          }

          foreach (var persisted in persistedByKey.Values.OrderByDescending(e => e.PlayedAt))
          {
            if (_entriesByKey.TryAdd(persisted.DedupeKey, persisted))
              InsertSorted(persisted);
          }

          foreach (var entry in newEntries.OrderByDescending(e => e.PlayedAt))
          {
            if (_entriesByKey.ContainsKey(entry.DedupeKey))
              continue;

            var matchedMemoryForEntry = FindMatchingCompletedMemoryEntry(_entriesByKey.Values, entry);
            if (matchedMemoryForEntry is not null)
            {
              MergePersistedCalculation(entry, matchedMemoryForEntry);
              _entriesByKey.TryRemove(matchedMemoryForEntry.DedupeKey, out _);
              Entries.Remove(matchedMemoryForEntry);
              _entriesByKey[entry.DedupeKey] = entry;
              InsertSorted(entry);
              entriesToSave.Add((entry, matchedMemoryForEntry.DedupeKey, matchedMemoryForEntry.PlayedAt));
              continue;
            }

            _entriesByKey[entry.DedupeKey] = entry;
            InsertSorted(entry);
            entriesToSave.Add((entry, null, null));
          }
        });

        foreach (var (entry, oldDedupeKey, oldPlayedAt) in entriesToSave)
          _repository.SaveEntry(entry, oldDedupeKey, oldPlayedAt);

        await _srPpEnricher.CalculateMissingSrPpAsync(uiDispatcher, Entries, _md5Map);
      }
      finally
      {
        _loadGate.Release();
      }
    }

    private static Dictionary<string, PlayLogEntry> SelectBestByDedupeKey(
      IEnumerable<PlayLogEntry> entries
    )
    {
      return entries
        .GroupBy(e => e.DedupeKey)
        .ToDictionary(
          g => g.Key,
          g =>
            g.OrderByDescending(e => e.StarRating.HasValue || e.Pp.HasValue ? 1 : 0)
              .ThenBy(e => e.IsCalculationFailed)
              .ThenByDescending(e => e.PlayedAt)
              .First()
        );
    }

    public async Task LoadAndCalculateAsync()
    {
      await _loadGate.WaitAsync();
      try
      {
        var allEntries = _repository.LoadAllFromDisk();
        LogUtils.DebugLogger($"PlayLogService: LoadAllFromDisk={allEntries.Count}", true);

        var historicalEntries = _historicalImporter.LoadFromLocalOsuData(out var md5Map);

        _md5Map = md5Map;
        LogUtils.DebugLogger($"PlayLogService: LoadHistorical={historicalEntries.Count}", true);

        var savedByKey = SelectBestByDedupeKey(allEntries);

        var combinedByKey = new Dictionary<string, PlayLogEntry>(savedByKey);
        var entriesToSave = new List<PlayLogEntry>();

        foreach (var historical in historicalEntries)
        {
          if (combinedByKey.TryGetValue(historical.DedupeKey, out var persisted))
          {
            if (ApplyHistoricalModeMetadata(persisted, historical))
              entriesToSave.Add(persisted);

            continue;
          }

          var provisionalHistorical = FindMatchingCompletedMemoryEntry(combinedByKey.Values, historical);
          combinedByKey[historical.DedupeKey] = historical;
          if (provisionalHistorical is not null)
          {
            MergePersistedCalculation(historical, provisionalHistorical);
            combinedByKey.Remove(provisionalHistorical.DedupeKey);
            _repository.SaveEntry(
              historical,
              provisionalHistorical.DedupeKey,
              provisionalHistorical.PlayedAt
            );
          }
          else
          {
            entriesToSave.Add(historical);
          }
        }

        foreach (var entry in entriesToSave)
          _repository.SaveEntry(entry);

        var combined = combinedByKey
          .Values.GroupBy(e => e.DedupeKey)
          .Select(g =>
            g.OrderByDescending(e => e.StarRating.HasValue ? 1 : 0)
              .ThenByDescending(e => e.PlayedAt)
              .First()
          )
          .ToList();

        var sorted = combined.OrderByDescending(e => e.PlayedAt).ToList();

        var uiDispatcher = _uiDispatcher;
        if (uiDispatcher == null)
          return;

        await uiDispatcher.InvokeAsync(() =>
        {
          Entries.Clear();
          _entriesByKey.Clear();
          foreach (var entry in sorted)
          {
            Entries.Add(entry);
            _entriesByKey[entry.DedupeKey] = entry;
          }
        });

        await _srPpEnricher.CalculateMissingSrPpAsync(uiDispatcher, Entries, _md5Map);
      }
      catch (Exception ex)
      {
        LogUtils.DebugLogger("PlayLogService.LoadAndCalculateAsync failed: " + ex.Message, true);
      }
      finally
      {
        _loadGate.Release();
      }
    }

    private static int FindInsertIndexDescending(IList<PlayLogEntry> list, DateTime playedAt)
    {
      int lo = 0,
        hi = list.Count;
      while (lo < hi)
      {
        int mid = lo + (hi - lo) / 2;
        if (list[mid].PlayedAt > playedAt)
          lo = mid + 1;
        else
          hi = mid;
      }
      return lo;
    }

    private static bool ApplyHistoricalModeMetadata(PlayLogEntry persisted, PlayLogEntry historical)
    {
      bool changed = false;

      if (persisted.Mode != historical.Mode)
      {
        persisted.Mode = historical.Mode;
        changed = true;
      }

      if (persisted.ManiaKeyCount != historical.ManiaKeyCount)
      {
        persisted.ManiaKeyCount = historical.ManiaKeyCount;
        changed = true;
      }

      return changed;
    }

    private static PlayLogEntry? FindMatchingCompletedMemoryEntry(
      IEnumerable<PlayLogEntry> entries,
      PlayLogEntry historical
    )
    {
      return entries.FirstOrDefault(entry =>
        entry.DedupeKey != historical.DedupeKey
        && IsMatchingCompletedMemoryEntry(entry, historical)
      );
    }

    private static bool IsMatchingCompletedMemoryEntry(
      PlayLogEntry candidate,
      PlayLogEntry historical
    )
    {
      if (
        !candidate.IsCompleted
        || candidate.OnlineScoreId.HasValue
        || !string.IsNullOrWhiteSpace(candidate.ReplayMd5)
      )
        return false;

      return candidate.BeatmapMd5 == historical.BeatmapMd5
        && candidate.PlayerName == historical.PlayerName
        && candidate.Mode == historical.Mode
        && candidate.ModsRaw == historical.ModsRaw
        && candidate.TotalScore == historical.TotalScore
        && candidate.Count300 == historical.Count300
        && candidate.Count100 == historical.Count100
        && candidate.Count50 == historical.Count50
        && candidate.CountGeki == historical.CountGeki
        && candidate.CountKatu == historical.CountKatu
        && candidate.CountMiss == historical.CountMiss
        && candidate.MaxCombo == historical.MaxCombo
        && Math.Abs((candidate.PlayedAt - historical.PlayedAt).TotalMinutes) <= 2;
    }

    private static void MergePersistedCalculation(PlayLogEntry destination, PlayLogEntry source)
    {
      if (!destination.StarRating.HasValue && source.StarRating.HasValue)
        destination.StarRating = source.StarRating;
      if (!destination.Pp.HasValue && source.Pp.HasValue)
        destination.Pp = source.Pp;
      destination.IsCalculationFailed =
        destination.IsCalculationFailed || source.IsCalculationFailed;
    }

    private void HandleStatusChanged(OsuMemoryStatus prev, OsuMemoryStatus current)
    {
      try
      {
        if (!_isRealtimeTrackingEnabled)
          return;

        if (
          current == OsuMemoryStatus.Playing
          && prev == OsuMemoryStatus.SongSelect
          && _memory.IsOsuRunning
          && _memory.IsDirectoryLoaded
          && !_memory.GetBaseAddressSnapshot().Player.IsReplay
        )
        {
          CaptureSessionStart();
          return;
        }

        if (prev == OsuMemoryStatus.Playing && _currentSession != null)
        {
          bool isCompleted = current == OsuMemoryStatus.ResultsScreen;
          CommitSession(isCompleted, current);
        }

        if (prev == OsuMemoryStatus.ResultsScreen && _currentSession != null)
        {
          UpdateLastEntryAsCompleted();
          _currentSession = null;
        }
      }
      catch (Exception ex)
      {
        LogUtils.DebugLogger("PlayLogService.HandleStatusChanged failed: " + ex.Message, true);
      }
    }

    private void HandleMemoryTick()
    {
      try
      {
        if (!_isRealtimeTrackingEnabled)
          return;
        if (_currentSession == null)
          return;

        if (_memory.CurrentStatus != OsuMemoryStatus.Playing)
          return;

        var livePlayer = _memory.GetBaseAddressSnapshot().Player;
        RefreshSessionMetadataFromLivePlayer();
        _currentSession.LastHit300 = livePlayer.Hit300;
        _currentSession.LastHit100 = livePlayer.Hit100;
        _currentSession.LastHit50 = livePlayer.Hit50;
        _currentSession.LastHitGeki = livePlayer.HitGeki;
        _currentSession.LastHitKatu = livePlayer.HitKatu;
        _currentSession.LastHitMiss = livePlayer.HitMiss;
        _currentSession.LastMaxCombo = livePlayer.MaxCombo;
        _currentSession.LastScore = livePlayer.Score;
        _currentSession.LastAccuracy = livePlayer.Accuracy;

        int currentRetries = _memory.GetBaseAddressSnapshot().GeneralData.Retries;
        if (currentRetries == _currentSession.StartRetries)
          return;

        CommitSession(isCompleted: false, endStatus: OsuMemoryStatus.Playing);
        if (!_memory.GetBaseAddressSnapshot().Player.IsReplay)
          CaptureSessionStart();
      }
      catch (Exception ex)
      {
        LogUtils.DebugLogger("PlayLogService.HandleMemoryTick failed: " + ex.Message, true);
      }
    }

    private void RefreshSessionMetadataFromLivePlayer()
    {
      if (_currentSession == null)
        return;

      int mode = _memory.CurrentOsuGamemode;
      if (mode is >= 0 and <= 3)
      {
        _currentSession.Mode = mode;

        if (mode != 3)
        {
          _currentSession.ManiaKeyCount = null;
        }
        else if (
          _currentSession.ManiaKeyCount == null
          && !_currentSession.ManiaKeyCountResolveAttempted
        )
        {
          var beatmapPath = _pathResolver.ResolveBeatmapFilePath(
            _memory.GetBaseAddressSnapshot().Beatmap,
            _md5Map
          );
          if (beatmapPath != null)
            _currentSession.ManiaKeyCount = BeatmapPathResolver.ReadManiaKeyCountFromFile(
              beatmapPath
            );
          _currentSession.ManiaKeyCountResolveAttempted = true;
        }
      }

      var playerName = _memory.GetBaseAddressSnapshot().Player.Username;
      if (!string.IsNullOrWhiteSpace(playerName))
        _currentSession.PlayerName = playerName;
    }

    private void CaptureSessionStart()
    {
      var baseAddresses = _memory.GetBaseAddressSnapshot();

      if ((baseAddresses.GeneralData.Mods & 2048) == 2048)
        return;

      var bm = baseAddresses.Beatmap;
      var filePath = _pathResolver.ResolveBeatmapFilePath(bm, _md5Map);
      var (artist, title, difficulty, creator) =
        filePath != null
          ? BeatmapPathResolver.ReadBeatmapMetadataFromFile(filePath)
          : ("", "", "", "");
      int mode = _memory.CurrentOsuGamemode;
      int? maniaKeyCount =
        mode == 3 && filePath != null
          ? BeatmapPathResolver.ReadManiaKeyCountFromFile(filePath)
          : null;

      string playerName = baseAddresses.Player.Username ?? "";
      _currentSession = new PlaySessionSnapshot
      {
        StartedAt = DateTime.Now,
        BeatmapId = bm.Id,
        BeatmapSetId = bm.SetId,
        Artist = artist,
        Title = title,
        DifficultyName = difficulty,
        Creator = creator,
        FolderName = bm.FolderName ?? "",
        OsuFileName = bm.OsuFileName ?? "",
        BeatmapMd5 = BeatmapPathResolver.ComputeMd5(filePath),
        PlayerName = playerName,
        Mode = mode,
        ManiaKeyCount = maniaKeyCount,
        Mods = _ppService.PrevMods,
        ModsRaw = baseAddresses.GeneralData.Mods,
        OverallDifficulty = bm.Od,
        StartRetries = baseAddresses.GeneralData.Retries,
      };
    }

    private void CommitSession(bool isCompleted, OsuMemoryStatus endStatus)
    {
      if (_currentSession == null)
        return;

      if (!_memory.IsOsuRunning)
      {
        _currentSession = null;
        return;
      }

      var snap = _currentSession;
      var isCompletedNow = endStatus == OsuMemoryStatus.ResultsScreen;
      var displayMods = OsuUtils.ParseMods(snap.ModsRaw).Display;
      var modsStr =
        displayMods.Length == 0 ? "NM" : string.Join(",", displayMods.Select(m => m.ToUpper()));

      var lastData = _ppService.LastCalculatedData;
      double? initialSr = lastData?.DifficultyAttributes?.StarRating;
      double? initialPp = isCompleted ? lastData?.CurrentPerformanceAttributes?.Total : null;

      var entry = new PlayLogEntry
      {
        PlayedAt = DateTime.Now,
        BeatmapId = snap.BeatmapId,
        BeatmapSetId = snap.BeatmapSetId,
        Artist = snap.Artist,
        Title = snap.Title,
        DifficultyName = snap.DifficultyName,
        Creator = snap.Creator,
        PlayerName = snap.PlayerName,
        Mode = snap.Mode,
        ManiaKeyCount = snap.ManiaKeyCount,
        OverallDifficulty = snap.OverallDifficulty,
        ModsString = modsStr,
        IsCompleted = isCompleted,
        Count300 = snap.LastHit300,
        Count100 = snap.LastHit100,
        Count50 = snap.LastHit50,
        CountGeki = snap.LastHitGeki,
        CountKatu = snap.LastHitKatu,
        CountMiss = snap.LastHitMiss,
        MaxCombo = snap.LastMaxCombo,
        TotalScore = snap.LastScore,
        Accuracy = snap.LastAccuracy,
        BeatmapMd5 = snap.BeatmapMd5,
        ModsRaw = snap.ModsRaw,

        IsProvisional = !isCompleted,
        StarRating = initialSr,
        Pp = initialPp,
      };

      entry.DedupeKey = isCompleted
        ? PlayLogKeyBuilder.MakeCompletedKey(
          entry.BeatmapMd5,
          entry.PlayerName,
          entry.Mode,
          entry.ModsRaw,
          entry.TotalScore,
          0,
          null
        )
        : PlayLogKeyBuilder.MakeInterruptedKey(entry);

      AddEntryToLog(entry);

      if (isCompleted)
        snap.PendingCompletedKey = entry.DedupeKey;

      if (!isCompleted)
        _currentSession = null;
    }

    private void AddEntryToLog(PlayLogEntry entry)
    {
      if (!_entriesByKey.TryAdd(entry.DedupeKey, entry))
      {
        var baseKey = entry.DedupeKey;
        entry.DedupeKey = $"{baseKey}|{entry.PlayedAt:yyyyMMddHHmmssfff}";
        var suffix = 1;
        while (!_entriesByKey.TryAdd(entry.DedupeKey, entry))
          entry.DedupeKey = $"{baseKey}|{entry.PlayedAt:yyyyMMddHHmmssfff}|{suffix++}";
        LogUtils.DebugLogger(
          $"PlayLogService: preserved completed-key collision for {baseKey}",
          true
        );
      }

      _uiDispatcher?.InvokeAsync(() => Entries.Insert(0, entry));
      _repository.SaveEntry(entry);
    }

    private void UpdateLastEntryAsCompleted()
    {
      if (_currentSession == null)
        return;

      var oldKey = _currentSession.PendingCompletedKey;
      if (oldKey == null || !_entriesByKey.TryGetValue(oldKey, out var existing))
      {
        _currentSession = null;
        return;
      }

      RefreshSessionMetadataFromLivePlayer();
      var rs = _memory.GetBaseAddressSnapshot().ResultsScreen;

      var uiDispatcher = _uiDispatcher;
      if (uiDispatcher == null)
      {
        _currentSession = null;
        return;
      }

      string newKey = null!;
      string? staleKey = null;
      uiDispatcher.Invoke(() =>
      {
        existing.Mode = rs.Mode;
        existing.ManiaKeyCount = existing.Mode == 3 ? _currentSession.ManiaKeyCount : null;
        existing.Count300 = rs.Hit300;
        existing.Count100 = rs.Hit100;
        existing.Count50 = rs.Hit50;
        existing.CountGeki = rs.HitGeki;
        existing.CountKatu = rs.HitKatu;
        existing.CountMiss = rs.HitMiss;
        existing.MaxCombo = rs.MaxCombo;
        existing.TotalScore = rs.Score;
        existing.IsCompleted = true;

        newKey = PlayLogKeyBuilder.MakeCompletedKey(
          existing.BeatmapMd5,
          existing.PlayerName,
          existing.Mode,
          existing.ModsRaw,
          existing.TotalScore,
          0,
          null
        );
        if (newKey != oldKey)
        {
          staleKey = oldKey;
          existing.DedupeKey = newKey;
          _entriesByKey.TryRemove(staleKey, out _);
        }
        _entriesByKey[newKey] = existing;
      });

      _repository.SaveEntry(existing, staleKey);

      _currentSession = null;
    }

    public void Dispose() { }
  }
}
