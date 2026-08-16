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
              "PlayLogSrPpEnricher.CalculateMissingSrPpAsync: osu!.db read failed: " + ex.Message,
              true
            );
          }
        }

        foreach (var dateGroup in targets.GroupBy(e => e.PlayedAt.Date))
        {
          var toSave = new List<PlayLogEntry>();

          foreach (var entry in dateGroup)
          {
            try
            {
              var (sr, pp) = CalculateSrPpForEntry(entry, md5Map);
              if (sr == null)
              {
                await dispatcher.InvokeAsync(() =>
                {
                  entry.IsCalculationFailed = true;
                });
                toSave.Add(entry);
                continue;
              }

              await dispatcher.InvokeAsync(() =>
              {
                entry.StarRating = sr;
                if (entry.IsCompleted)
                  entry.Pp = pp;
              });
              toSave.Add(entry);
            }
            catch (Exception ex)
            {
              LogUtils.DebugLogger(
                $"PlayLogSrPpEnricher: SR/pp calc failed for BeatmapId={entry.BeatmapId}: {ex.Message}",
                true
              );
              await dispatcher.InvokeAsync(() =>
              {
                entry.IsCalculationFailed = true;
              });
              toSave.Add(entry);
            }
          }

          if (toSave.Count > 0)
            _repository.SaveEntries(toSave);
        }
      }
      finally
      {
        _calculationGate.Release();
      }
    }

    public (double? sr, double? pp) CalculateSrPpForEntry(
      PlayLogEntry entry,
      Dictionary<string, OsuMate.Services.StableDb.BeatmapInfo>? md5Map = null
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

      var calculator = new PpCalculator(beatmapPath, entry.Mode);

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

      var data = calculator.Calculate(args, false, true, hits);
      double? sr = data.DifficultyAttributes?.StarRating;
      double? pp = data.CurrentPerformanceAttributes?.Total;
      return (sr, pp);
    }
  }
}
