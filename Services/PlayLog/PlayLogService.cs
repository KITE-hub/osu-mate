using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Windows.Threading;
using OsuMate.Models;
using OsuMate.Services.Osu;
using OsuMate.Utils;
using OsuMemoryDataProvider;
using System.Globalization;

namespace OsuMate.Services.PlayLog
{
  public class PlayLogService : IDisposable
  {
    private readonly OsuMemoryService _memory;
    private readonly PpCalculationService _ppService;
    private readonly Dispatcher _uiDispatcher;
    private readonly PlayLogRepository _repository;
    private readonly BeatmapPathResolver _pathResolver;
    private readonly PlayLogSrPpEnricher _srPpEnricher;
    private readonly HistoricalImporter _historicalImporter;
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private readonly Channel<PendingWrite> _writeQueue = Channel.CreateUnbounded<PendingWrite>(
      new UnboundedChannelOptions { SingleReader = true }
    );
    private readonly Task _writerTask;

    private readonly record struct PendingWrite(
      PlayLogEntry Entry,
      string? OldDedupeKey,
      DateTimeOffset? OldPlayedAt
    );

    private const int AutoplayModFlag = 2048;

    private PlaySession? _currentSession;

    public BulkObservableCollection<PlayLogEntry> Entries { get; } = [];

    public Dispatcher UiDispatcher => _uiDispatcher;

    private readonly ConcurrentDictionary<string, PlayLogEntry> _entriesByKey = new();

    private Dictionary<string, OsuMate.Services.StableDb.BeatmapInfo>? _md5Map;

    public PlayLogService(
      OsuMemoryService memory,
      PpCalculationService ppService,
      PlayLogRepository repository,
      BeatmapPathResolver pathResolver,
      PlayLogSrPpEnricher srPpEnricher,
      HistoricalImporter historicalImporter,
      Dispatcher uiDispatcher
    )
    {
      _uiDispatcher = uiDispatcher;
      _memory = memory;
      _ppService = ppService;
      _repository = repository;
      _pathResolver = pathResolver;
      _srPpEnricher = srPpEnricher;
      _historicalImporter = historicalImporter;

      _memory.OnStatusChanged += HandleStatusChanged;

      _memory.OnMemoryRead += HandleMemoryTick;

      _memory.OnOsuDirectoryLoaded += HandleOsuDirectoryLoaded;

      _writerTask = Task.Run(RunWriterAsync);
    }

    private async Task RunWriterAsync()
    {
      await foreach (var write in _writeQueue.Reader.ReadAllAsync())
      {
        try
        {
          _repository.SaveEntry(write.Entry, write.OldDedupeKey, write.OldPlayedAt);
        }
        catch (Exception ex)
        {
          LogUtils.DebugLogger("PlayLogService.RunWriterAsync failed: " + ex, true);
        }
      }
    }

    private void EnqueueSave(
      PlayLogEntry entry,
      string? oldDedupeKey = null,
      DateTimeOffset? oldPlayedAt = null
    ) => _writeQueue.Writer.TryWrite(new PendingWrite(entry, oldDedupeKey, oldPlayedAt));

    private void HandleOsuDirectoryLoaded(string osuDirectory)
    {
      _ = Task.Run(async () =>
      {
        try
        {
          await ReconcileAsync();
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

    public async Task LoadAndCalculateAsync()
    {
      try
      {
        await ReconcileAsync();
      }
      catch (Exception ex)
      {
        LogUtils.DebugLogger("PlayLogService.LoadAndCalculateAsync failed: " + ex, true);
      }
    }

    private async Task ReconcileAsync()
    {
      await _loadGate.WaitAsync();
      try
      {
        var uiDispatcher = _uiDispatcher;

        var (historicalEntries, md5Map, persistedByKey) = await Task.Run(() =>
        {
          var historical = _historicalImporter.LoadFromLocalOsuData(out var map);
          return (historical, map, SelectBestByDedupeKey(_repository.LoadAllFromDisk()));
        });

        if (md5Map != null)
          _md5Map = md5Map;

        var addedEntries = new List<PlayLogEntry>();
        var metadataChangedEntries = new List<PlayLogEntry>();
        var replacedEntries = new List<(PlayLogEntry Entry, string OldDedupeKey, DateTimeOffset OldPlayedAt)>();

        await uiDispatcher.InvokeAsync(() =>
        {
          var removed = new HashSet<PlayLogEntry>();
          var visible = new List<PlayLogEntry>();

          foreach (var persisted in persistedByKey.Values)
          {
            if (_entriesByKey.TryAdd(persisted.DedupeKey, persisted))
              visible.Add(persisted);
          }

          var memoryIndex = CompletedMemoryIndex.Build(_entriesByKey.Values);

          foreach (var entry in historicalEntries)
          {
            if (_entriesByKey.TryGetValue(entry.DedupeKey, out var existing))
            {
              if (!ReferenceEquals(existing, entry) && ApplyHistoricalModeMetadata(existing, entry))
                metadataChangedEntries.Add(existing);
              continue;
            }

            var matchedMemoryEntry = memoryIndex.FindMatch(entry);
            if (matchedMemoryEntry is not null)
            {
              MergePersistedCalculation(entry, matchedMemoryEntry);
              _entriesByKey.TryRemove(matchedMemoryEntry.DedupeKey, out _);
              memoryIndex.Remove(matchedMemoryEntry);
              removed.Add(matchedMemoryEntry);
              replacedEntries.Add((entry, matchedMemoryEntry.DedupeKey, matchedMemoryEntry.PlayedAt));
            }
            else
            {
              addedEntries.Add(entry);
            }

            _entriesByKey[entry.DedupeKey] = entry;
            visible.Add(entry);
          }

          if (visible.Count == 0 && removed.Count == 0)
            return;

          Entries.ReplaceAll(
            Entries.Concat(visible).Where(e => !removed.Contains(e)).OrderByDescending(e => e.PlayedAt)
          );
        });

        await Task.Run(() =>
        {
          _repository.SaveEntries(addedEntries.Concat(metadataChangedEntries));
          foreach (var (entry, oldDedupeKey, oldPlayedAt) in replacedEntries)
            _repository.SaveEntry(entry, oldDedupeKey, oldPlayedAt);
        });

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

    private sealed class CompletedMemoryIndex
    {
      private readonly Dictionary<(string, string, int, int, int), List<PlayLogEntry>> _byKey = [];

      public static CompletedMemoryIndex Build(IEnumerable<PlayLogEntry> entries)
      {
        var index = new CompletedMemoryIndex();
        foreach (var entry in entries)
          index.Add(entry);
        return index;
      }

      public void Add(PlayLogEntry entry)
      {
        if (!IsMatchableMemoryEntry(entry))
          return;

        var key = KeyOf(entry);
        if (!_byKey.TryGetValue(key, out var list))
          _byKey[key] = list = [];
        list.Add(entry);
      }

      public void Remove(PlayLogEntry entry)
      {
        if (_byKey.TryGetValue(KeyOf(entry), out var list))
          list.Remove(entry);
      }

      public PlayLogEntry? FindMatch(PlayLogEntry historical)
      {
        if (!_byKey.TryGetValue(KeyOf(historical), out var list))
          return null;

        return list.FirstOrDefault(candidate =>
          candidate.DedupeKey != historical.DedupeKey
          && IsMatchingCompletedMemoryEntry(candidate, historical)
        );
      }

      private static (string, string, int, int, int) KeyOf(PlayLogEntry e) =>
        (e.BeatmapMd5, e.PlayerName, e.Mode, e.ModsRaw, e.TotalScore);
    }

    private static bool IsMatchableMemoryEntry(PlayLogEntry entry) =>
      entry.IsCompleted
      && !entry.OnlineScoreId.HasValue
      && string.IsNullOrWhiteSpace(entry.ReplayMd5);

    private static bool IsMatchingCompletedMemoryEntry(
      PlayLogEntry candidate,
      PlayLogEntry historical
    )
    {
      if (!IsMatchableMemoryEntry(candidate))
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
          CommitSession(isCompleted);
        }

        if (prev == OsuMemoryStatus.ResultsScreen && _currentSession != null)
        {
          UpdateLastEntryAsCompleted();
          _currentSession = null;
        }
      }
      catch (Exception ex)
      {
        LogUtils.DebugLogger("PlayLogService.HandleStatusChanged failed: " + ex, true);
      }
    }

    private void HandleMemoryTick()
    {
      try
      {
        if (_currentSession == null)
          return;

        if (_memory.CurrentStatus != OsuMemoryStatus.Playing)
          return;

        var livePlayer = _memory.GetBaseAddressSnapshot().Player;
        RefreshSessionMetadataFromLivePlayer();
        _currentSession.CaptureScore(
          livePlayer.Hit300,
          livePlayer.Hit100,
          livePlayer.Hit50,
          livePlayer.HitGeki,
          livePlayer.HitKatu,
          livePlayer.HitMiss,
          livePlayer.MaxCombo,
          livePlayer.Score,
          livePlayer.Accuracy
        );

        int currentRetries = _memory.GetBaseAddressSnapshot().GeneralData.Retries;
        if (currentRetries == _currentSession.StartRetries)
          return;

        CommitSession(isCompleted: false);
        if (!_memory.GetBaseAddressSnapshot().Player.IsReplay)
          CaptureSessionStart();
      }
      catch (Exception ex)
      {
        LogUtils.DebugLogger("PlayLogService.HandleMemoryTick failed: " + ex, true);
      }
    }

    private void RefreshSessionMetadataFromLivePlayer()
    {
      if (_currentSession == null)
        return;

      int mode = _memory.CurrentOsuGamemode;
      if (OsuMode.IsValid(mode))
      {
        _currentSession.UpdateMode(mode);

        if (_currentSession.NeedsManiaKeyCountResolve)
        {
          var beatmapPath = _pathResolver.ResolveBeatmapFilePath(
            _memory.GetBaseAddressSnapshot().Beatmap,
            _md5Map
          );
          _currentSession.SetManiaKeyCount(
            beatmapPath != null ? BeatmapPathResolver.ReadManiaKeyCountFromFile(beatmapPath) : null
          );
        }
      }

      _currentSession.UpdatePlayerName(_memory.GetBaseAddressSnapshot().Player.Username);
    }

    private void CaptureSessionStart()
    {
      var baseAddresses = _memory.GetBaseAddressSnapshot();

      if ((baseAddresses.GeneralData.Mods & AutoplayModFlag) == AutoplayModFlag)
        return;

      var bm = baseAddresses.Beatmap;
      var filePath = _pathResolver.ResolveBeatmapFilePath(bm, _md5Map);
      var (artist, title, difficulty, creator) =
        filePath != null
          ? BeatmapPathResolver.ReadBeatmapMetadataFromFile(filePath)
          : ("", "", "", "");
      int mode = _memory.CurrentOsuGamemode;
      int? maniaKeyCount =
        mode == OsuMode.Mania && filePath != null
          ? BeatmapPathResolver.ReadManiaKeyCountFromFile(filePath)
          : null;

      string playerName = baseAddresses.Player.Username ?? "";
      _currentSession = new PlaySession
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

    private void CommitSession(bool isCompleted)
    {
      if (_currentSession == null)
        return;

      if (!_memory.IsOsuRunning)
      {
        _currentSession = null;
        return;
      }

      var snap = _currentSession;
      var displayMods = OsuUtils.ParseMods(snap.ModsRaw).Display;
      var modsStr =
        displayMods.Length == 0 ? "NM" : string.Join(",", displayMods.Select(m => m.ToUpper()));

      var lastData = _ppService.LastCalculatedData;
      double? initialSr = lastData?.DifficultyAttributes?.StarRating;
      double? initialPp = isCompleted ? lastData?.CurrentPerformanceAttributes?.Total : null;

      var entry = new PlayLogEntry
      {
        PlayedAt = DateTimeOffset.Now,
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
        snap.MarkCommitted(entry.DedupeKey);

      if (!isCompleted)
        _currentSession = null;
    }

    private void AddEntryToLog(PlayLogEntry entry)
    {
      RegisterUniqueKey(entry);

      _uiDispatcher.InvokeAsync(() => Entries.Insert(0, entry));
      EnqueueSave(entry);
    }

    private void RegisterUniqueKey(PlayLogEntry entry)
    {
      var baseKey = entry.DedupeKey;
      if (_entriesByKey.TryAdd(baseKey, entry))
        return;

      var playedAtStamp = entry.PlayedAt.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);
      entry.DedupeKey = $"{baseKey}|{playedAtStamp}";
      var suffix = 1;
      while (!_entriesByKey.TryAdd(entry.DedupeKey, entry))
        entry.DedupeKey = $"{baseKey}|{playedAtStamp}|{suffix++}";
      LogUtils.DebugLogger($"PlayLogService: preserved completed-key collision for {baseKey}", true);
    }

    private void UpdateLastEntryAsCompleted()
    {
      var session = _currentSession;
      if (session == null)
        return;

      var oldKey = session.PendingCompletedKey;
      var uiDispatcher = _uiDispatcher;
      if (oldKey == null || !_entriesByKey.TryGetValue(oldKey, out var existing))
      {
        _currentSession = null;
        return;
      }

      RefreshSessionMetadataFromLivePlayer();
      var rs = _memory.GetBaseAddressSnapshot().ResultsScreen;
      var maniaKeyCount = session.ManiaKeyCount;
      _currentSession = null;

      _ = uiDispatcher.InvokeAsync(() =>
      {
        existing.Mode = rs.Mode;
        existing.ManiaKeyCount = existing.Mode == OsuMode.Mania ? maniaKeyCount : null;
        existing.Count300 = rs.Hit300;
        existing.Count100 = rs.Hit100;
        existing.Count50 = rs.Hit50;
        existing.CountGeki = rs.HitGeki;
        existing.CountKatu = rs.HitKatu;
        existing.CountMiss = rs.HitMiss;
        existing.MaxCombo = rs.MaxCombo;
        existing.TotalScore = rs.Score;
        existing.IsCompleted = true;

        var newKey = PlayLogKeyBuilder.MakeCompletedKey(
          existing.BeatmapMd5,
          existing.PlayerName,
          existing.Mode,
          existing.ModsRaw,
          existing.TotalScore,
          0,
          null
        );

        string? staleKey = null;
        if (newKey != oldKey)
        {
          staleKey = oldKey;
          _entriesByKey.TryRemove(oldKey, out _);
          existing.DedupeKey = newKey;
          RegisterUniqueKey(existing);
        }

        EnqueueSave(existing, staleKey);
      });
    }

    public void Dispose()
    {
      _memory.OnStatusChanged -= HandleStatusChanged;
      _memory.OnMemoryRead -= HandleMemoryTick;
      _memory.OnOsuDirectoryLoaded -= HandleOsuDirectoryLoaded;

      _writeQueue.Writer.TryComplete();
      try
      {
        _writerTask.Wait(TimeSpan.FromSeconds(3));
      }
      catch (AggregateException ex)
      {
        LogUtils.DebugLogger("PlayLogService.Dispose failed: " + ex, true);
      }
    }
  }
}
