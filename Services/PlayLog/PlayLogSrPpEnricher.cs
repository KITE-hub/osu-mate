using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using OsuMate.Models;
using OsuMate.PPCalculation;
using OsuMate.Services.Osu;
using OsuMate.Utils;

namespace OsuMate.Services.PlayLog
{
  public class PlayLogSrPpEnricher
  {
    private readonly OsuMemoryService _memory;
    private readonly BeatmapPathResolver _pathResolver;
    private readonly PlayLogRepository _repository;
    private const int CalculationChunkSize = 16;
    private const int PlayingPollDelayMs = 1000;
    private readonly SemaphoreSlim _calculationGate = new(1, 1);

    public PlayLogSrPpEnricher(
      OsuMemoryService memory,
      BeatmapPathResolver pathResolver,
      PlayLogRepository repository
    )
    {
      _memory = memory;
      _pathResolver = pathResolver;
      _repository = repository;
    }

    public async Task CalculateMissingSrPpAsync(
      Dispatcher dispatcher,
      ObservableCollection<PlayLogEntry> entries,
      Dictionary<string, OsuMate.Services.StableDb.BeatmapInfo>? md5Map
    )
    {
      await _calculationGate.WaitAsync();
      try
      {
        for (int i = 0; i < 100 && !_memory.IsDirectoryLoaded; i++)
          await Task.Delay(100);

        if (!_memory.IsDirectoryLoaded)
          return;

        var targets = await dispatcher.InvokeAsync(() =>
          entries
            .Where(e =>
              e.IsCompleted
              && e.StarRating == null
              && !e.IsCalculationFailed
              && (e.BeatmapId > 0 || !string.IsNullOrEmpty(e.BeatmapMd5))
            )
            .ToList()
        );
        if (targets.Count == 0)
          return;

        if (md5Map == null)
        {
          try
          {
            var osuDbPath = Path.Combine(_memory.OsuDirectory, "osu!.db");
            if (File.Exists(osuDbPath))
              md5Map = OsuMate.Services.StableDb.OsuDbReader.ReadBeatmaps(osuDbPath);
          }
          catch (Exception ex)
          {
            LogUtils.DebugLogger(
              "PlayLogSrPpEnricher.CalculateMissingSrPpAsync: osu!.db read failed: " + ex,
              true
            );
          }
        }

        foreach (var dateGroup in targets.GroupBy(e => e.PlayedAt.Date))
        {
          var ordered = dateGroup
            .OrderBy(e => e.BeatmapMd5)
            .ThenBy(e => e.BeatmapId)
            .ThenBy(e => e.Mode)
            .ThenBy(e => e.ModsString)
            .ToList();

          var results = await CalculateGroupAsync(ordered, md5Map);

          await dispatcher.InvokeAsync(() =>
          {
            foreach (var result in results)
            {
              if (result.Failed)
              {
                result.Entry.IsCalculationFailed = true;
                continue;
              }

              result.Entry.StarRating = result.StarRating;
              if (result.Entry.IsCompleted)
                result.Entry.Pp = result.Pp;
            }
          });

          _repository.SaveEntries(results.Select(r => r.Entry).ToList());
        }
      }
      finally
      {
        _calculationGate.Release();
      }
    }

    private readonly record struct EntryCalculation(
      PlayLogEntry Entry,
      double? StarRating,
      double? Pp,
      bool Failed
    );

    private async Task<List<EntryCalculation>> CalculateGroupAsync(
      List<PlayLogEntry> entries,
      Dictionary<string, OsuMate.Services.StableDb.BeatmapInfo>? md5Map
    )
    {
      var results = new List<EntryCalculation>(entries.Count);
      var cache = new CalculatorCache();

      foreach (var chunk in entries.Chunk(CalculationChunkSize))
      {
        while (_memory.IsPlaying)
          await Task.Delay(PlayingPollDelayMs);

        var chunkResults = await Task.Run(() => CalculateChunk(chunk, md5Map, cache));
        results.AddRange(chunkResults);
      }

      return results;
    }

    private List<EntryCalculation> CalculateChunk(
      PlayLogEntry[] chunk,
      Dictionary<string, OsuMate.Services.StableDb.BeatmapInfo>? md5Map,
      CalculatorCache cache
    )
    {
      var results = new List<EntryCalculation>(chunk.Length);
      foreach (var entry in chunk)
      {
        try
        {
          var (sr, pp) = CalculateSrPpForEntry(entry, md5Map, cache);
          results.Add(new EntryCalculation(entry, sr, pp, sr == null));
        }
        catch (Exception ex)
        {
          LogUtils.DebugLogger(
            $"PlayLogSrPpEnricher: SR/pp calc failed for BeatmapId={entry.BeatmapId}: {ex}",
            true
          );
          results.Add(new EntryCalculation(entry, null, null, true));
        }
      }
      return results;
    }

    private sealed class CalculatorCache
    {
      private string? _path;
      private int _mode;
      private PpCalculator? _calculator;

      public PpCalculator Get(string path, int mode)
      {
        if (_calculator == null || _path != path || _mode != mode)
        {
          _calculator = new PpCalculator(path, mode);
          _path = path;
          _mode = mode;
        }
        return _calculator;
      }
    }

    public (double? sr, double? pp) CalculateSrPpForEntry(
      PlayLogEntry entry,
      Dictionary<string, OsuMate.Services.StableDb.BeatmapInfo>? md5Map = null
    ) => CalculateSrPpForEntry(entry, md5Map, new CalculatorCache());

    private (double? sr, double? pp) CalculateSrPpForEntry(
      PlayLogEntry entry,
      Dictionary<string, OsuMate.Services.StableDb.BeatmapInfo>? md5Map,
      CalculatorCache cache
    )
    {
      var beatmapPath =
        _pathResolver.FindBeatmapPathByMd5(entry.BeatmapMd5, md5Map)
        ?? _pathResolver.FindBeatmapPathById(entry.BeatmapId, md5Map)
        ?? _pathResolver.FindBeatmapPath(entry.BeatmapId);
      if (beatmapPath == null)
        return (null, null);

      var mods =
        (string.IsNullOrEmpty(entry.ModsString) || entry.ModsString == "NM")
          ? Array.Empty<string>()
          : entry.ModsString.Split(',').Select(m => m.Trim().ToLowerInvariant()).ToArray();

      var calculator = cache.Get(beatmapPath, entry.Mode);

      var hits = new HitsResult
      {
        HitGeki = entry.CountGeki,
        Hit300 = entry.Count300,
        HitKatu = entry.CountKatu,
        Hit100 = entry.Count100,
        Hit50 = entry.Count50,
        HitMiss = entry.CountMiss,
        Combo = entry.MaxCombo,
        Score = entry.TotalScore,
      };

      double accuracy = OsuUtils.CalculateAccuracy(hits, entry.Mode);

      var args = new CalculateArgs
      {
        Mods = mods,
        Time = int.MaxValue,
        Combo = entry.MaxCombo,
        Score = entry.TotalScore,
        Accuracy = accuracy * 100,
      };

      var data = calculator.Calculate(args, false, true, hits, includeStrainInfo: false);
      double? sr = data.DifficultyAttributes?.StarRating;
      double? pp = data.CurrentPerformanceAttributes?.Total;
      return (sr, pp);
    }
  }
}
