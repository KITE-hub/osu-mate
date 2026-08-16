using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using OsuMate.Models;
using OsuMate.Utils;

namespace OsuMate.Services.PlayLog
{
  public class PlayLogRepository
  {
    private readonly object _saveFileLock = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
      WriteIndented = true,
      Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
      DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public string LogOutputDir { get; } =
      Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PlayLogs");

    public void SaveEntry(
      PlayLogEntry entry,
      string? oldDedupeKey = null,
      DateTime? oldPlayedAt = null
    )
    {
      try
      {
        Directory.CreateDirectory(LogOutputDir);
        string fileName = entry.PlayedAt.ToString("yyyy-MM-dd") + ".json";
        string path = Path.Combine(LogOutputDir, fileName);

        lock (_saveFileLock)
        {
          List<PlayLogEntry> existing;
          try
          {
            existing = LoadFromFile(path);
          }
          catch (JsonException ex)
          {
            LogUtils.DebugLogger(
              $"PlayLogRepository.SaveEntry: {path} is corrupted, recovering with a fresh file: {ex.Message}",
              true
            );
            QuarantineCorruptedFile(path);
            existing = [];
          }

          var idx = existing.FindIndex(e => e.DedupeKey == entry.DedupeKey);

          if (idx < 0 && oldDedupeKey != null)
            idx = existing.FindIndex(e => e.DedupeKey == oldDedupeKey);
          if (idx >= 0)
            existing[idx] = entry;
          else
            existing.Add(entry);

          WriteEntriesToFile(path, existing);

          if (
            oldDedupeKey != null
            && oldPlayedAt.HasValue
            && oldPlayedAt.Value.Date != entry.PlayedAt.Date
          )
            RemoveFromDateFile(oldPlayedAt.Value, oldDedupeKey);
        }
      }
      catch (Exception ex)
      {
        LogUtils.DebugLogger("PlayLogRepository.SaveEntry failed: " + ex.Message, true);
      }
    }

    public void SaveEntries(IEnumerable<PlayLogEntry> entries)
    {
      foreach (var group in entries.GroupBy(e => e.PlayedAt.ToString("yyyy-MM-dd")))
      {
        SaveEntryGroup(group.Key, [.. group]);
      }
    }

    private void SaveEntryGroup(string fileNameDate, List<PlayLogEntry> entries)
    {
      if (entries.Count == 0)
        return;
      try
      {
        Directory.CreateDirectory(LogOutputDir);
        string fileName = fileNameDate + ".json";
        string path = Path.Combine(LogOutputDir, fileName);

        lock (_saveFileLock)
        {
          List<PlayLogEntry> existing;
          try
          {
            existing = LoadFromFile(path);
          }
          catch (JsonException ex)
          {
            LogUtils.DebugLogger(
              $"PlayLogRepository.SaveEntries: {path} is corrupted, recovering with a fresh file: {ex.Message}",
              true
            );
            QuarantineCorruptedFile(path);
            existing = [];
          }

          foreach (var entry in entries)
          {
            var idx = existing.FindIndex(e => e.DedupeKey == entry.DedupeKey);
            if (idx >= 0)
              existing[idx] = entry;
            else
              existing.Add(entry);
          }

          WriteEntriesToFile(path, existing);
        }
      }
      catch (Exception ex)
      {
        LogUtils.DebugLogger("PlayLogRepository.SaveEntries failed: " + ex.Message, true);
      }
    }

    private void RemoveFromDateFile(DateTime date, string dedupeKey)
    {
      string fileName = date.ToString("yyyy-MM-dd") + ".json";
      string path = Path.Combine(LogOutputDir, fileName);

      List<PlayLogEntry> existing;
      try
      {
        existing = LoadFromFile(path);
      }
      catch (JsonException ex)
      {
        LogUtils.DebugLogger(
          $"PlayLogRepository.RemoveFromDateFile: {path} is corrupted, skipping cleanup: {ex.Message}",
          true
        );
        return;
      }

      if (existing.RemoveAll(e => e.DedupeKey == dedupeKey) == 0)
        return;

      WriteEntriesToFile(path, existing);
    }

    private static void WriteEntriesToFile(string path, List<PlayLogEntry> entries)
    {
      var ordered = entries.OrderBy(e => e.PlayedAt).ToList();
      string json = JsonSerializer.Serialize(ordered, JsonOpts);

      string tempPath = path + ".tmp";
      File.WriteAllText(tempPath, json, System.Text.Encoding.UTF8);
      File.Move(tempPath, path, overwrite: true);
    }

    private static void QuarantineCorruptedFile(string path)
    {
      try
      {
        string backupPath = path + $".corrupted-{DateTime.Now:yyyyMMddHHmmss}.bak";
        File.Move(path, backupPath, overwrite: true);
      }
      catch (Exception ex)
      {
        LogUtils.DebugLogger("PlayLogRepository.QuarantineCorruptedFile failed: " + ex.Message, true);
      }
    }

    public List<PlayLogEntry> LoadAllFromDisk()
    {
      var all = new List<PlayLogEntry>();
      if (!Directory.Exists(LogOutputDir))
        return all;

      foreach (var file in Directory.GetFiles(LogOutputDir, "*.json"))
      {
        try
        {
          all.AddRange(LoadFromFile(file));
        }
        catch (JsonException ex)
        {
          LogUtils.DebugLogger(
            $"PlayLogRepository.LoadAllFromDisk: Skipped because failed to read {file}: {ex.Message}",
            true
          );
        }
      }
      return all;
    }

    internal static List<PlayLogEntry> LoadFromFile(string path)
    {
      if (!File.Exists(path))
        return [];
      var json = File.ReadAllText(path, System.Text.Encoding.UTF8);
      return JsonSerializer.Deserialize<List<PlayLogEntry>>(json, JsonOpts) ?? [];
    }
  }
}
