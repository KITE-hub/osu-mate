using System;
using System.Collections.Generic;
using System.IO;
using OsuMate.Models;
using OsuMate.Utils;

namespace OsuMate.Services.PlayLog
{
  public class HistoricalImporter
  {
    private readonly OsuMemoryService _memory;
    private readonly GlobalConfig _config;

    public HistoricalImporter(OsuMemoryService memory)
    {
      _memory = memory;
      _config = ConfigUtils.LoadGlobalConfig();
    }

    public List<PlayLogEntry> LoadFromLocalOsuData(
      out Dictionary<string, OsuMate.Services.StableDb.BeatmapInfo>? md5Map
    )
    {
      md5Map = null;
      var list = new List<PlayLogEntry>();
      try
      {
        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(_config.OsuExeDirectory))
          candidates.Add(_config.OsuExeDirectory);

        if (!string.IsNullOrWhiteSpace(_memory.OsuDirectory))
          candidates.Add(_memory.OsuDirectory);

        candidates.Add(AppDomain.CurrentDomain.BaseDirectory);

        var exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
        if (!string.IsNullOrEmpty(exePath))
        {
          var dir = Path.GetDirectoryName(exePath);
          if (!string.IsNullOrEmpty(dir) && !candidates.Contains(dir))
            candidates.Add(dir);
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var osuAppData = Path.Combine(appData, "osu!");
        if (!candidates.Contains(osuAppData))
          candidates.Add(osuAppData);

        LogUtils.DebugLogger(
          $"HistoricalImporter: DB search candidates: {string.Join(", ", candidates)}",
          true
        );

        string? osuDbPath = null;
        string? scoresDbPath = null;
        foreach (var dir in candidates)
        {
          var o = Path.Combine(dir, "osu!.db");
          var s = Path.Combine(dir, "scores.db");
          if (File.Exists(o) && File.Exists(s))
          {
            osuDbPath = o;
            scoresDbPath = s;
            break;
          }
        }
        if (osuDbPath == null || scoresDbPath == null)
        {
          LogUtils.DebugLogger(
            "HistoricalImporter: osu!.db or scores.db not found in any candidate directory",
            true
          );
          return list;
        }
        LogUtils.DebugLogger($"HistoricalImporter: Reading DB from {osuDbPath}", true);

        var parsedMd5Map = OsuMate.Services.StableDb.OsuDbReader.ReadBeatmaps(osuDbPath);
        var scores = OsuMate.Services.StableDb.ScoresDbReader.ReadScores(scoresDbPath);
        list.AddRange(BuildEntriesFromScores(scores, parsedMd5Map));
        md5Map = parsedMd5Map;
      }
      catch (Exception ex)
      {
        LogUtils.DebugLogger("HistoricalImporter.LoadFromLocalOsuData failed: " + ex.Message, true);
      }

      return list;
    }

    private static List<PlayLogEntry> BuildEntriesFromScores(
      List<OsuMate.Services.StableDb.ScoreRecord> scores,
      Dictionary<string, OsuMate.Services.StableDb.BeatmapInfo>? md5Map
    )
    {
      var list = new List<PlayLogEntry>(scores.Count);
      foreach (var score in scores)
      {
        if (md5Map != null && md5Map.TryGetValue(score.Md5Hash, out var beatmap))
        {
          list.Add(PlayLogEntryFactory.FromScoresDbScore(score, beatmap));
          continue;
        }

        list.Add(
          PlayLogEntryFactory.FromScoresDbScore(
            score,
            new OsuMate.Services.StableDb.BeatmapInfo
            {
              Md5Hash = score.Md5Hash,
              DifficultyId = 0,
              BeatmapSetId = 0,
              Mode = score.Mode,
            }
          )
        );
      }
      return list;
    }
  }
}
