using System.IO;
using System.IO.Compression;
using OsuMate.Utils;

namespace OsuMate.Services.Trainer
{
  public class BatchGenerationRequest
  {
    public decimal Rate { get; set; }
    public decimal? ArOverride { get; set; }
    public decimal? OdOverride { get; set; }
    public decimal? HpOverride { get; set; }
    public decimal? CsOverride { get; set; }
  }

  public class BeatmapTrainerService
  {
    private readonly OsuMemoryService _memory;

    internal const decimal DifficultyChangeThreshold = 0.001M;

    public BeatmapTrainerService(OsuMemoryService memory)
    {
      _memory = memory;
    }

    public string? GetCurrentBeatmapPath()
    {
      if (!_memory.IsDirectoryLoaded)
        return null;
      var beatmap = _memory.GetBaseAddressSnapshot().Beatmap;
      string? folder = beatmap.FolderName?.Trim();
      string? file = beatmap.OsuFileName?.Trim();
      if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(file))
        return null;

      try
      {
        string path = Path.Combine(_memory.SongsPath, folder, file);
        return File.Exists(path) ? path : null;
      }
      catch (Exception ex)
      {
        LogUtils.DebugLogger(
          $"BeatmapTrainerService.GetCurrentBeatmapPath failed: {ex.Message}",
          true
        );
        return null;
      }
    }

    public static string? FindOriginalMap(string osutrainerMapPath)
    {
      string dir = Path.GetDirectoryName(osutrainerMapPath)!;

      OsuBeatmapFile? trainerMap = null;
      try
      {
        trainerMap = OsuBeatmapFile.Load(osutrainerMapPath);
      }
      catch (Exception ex)
      {
        LogUtils.DebugLogger($"[Trainer] Failed to load generated beatmap: {ex.Message}", true);
        return null;
      }

      string[] candidates;
      try
      {
        candidates = Directory.GetFiles(dir, "*.osu").OrderBy(f => f).ToArray();
      }
      catch (Exception ex)
      {
        LogUtils.DebugLogger($"[Trainer] Failed to enumerate folders: {ex.Message}", true);
        return null;
      }

      if (!string.IsNullOrEmpty(trainerMap.SourceOsuFileName))
      {
        string candidatePath = Path.Combine(dir, trainerMap.SourceOsuFileName);
        if (File.Exists(candidatePath))
        {
          try
          {
            var candidate = OsuBeatmapFile.Load(candidatePath);
            if (!candidate.IsOsuTrainerMap)
              return candidatePath;
          }
          catch (Exception ex)
          {
            LogUtils.DebugLogger(
              $"[Trainer] Failed to load embedded original beatmap: {ex.Message}",
              true
            );
          }
        }
      }

      string? bestMatch = null;
      int bestLength = -1;
      foreach (var osuFile in candidates)
      {
        if (string.Equals(osuFile, osutrainerMapPath, StringComparison.OrdinalIgnoreCase))
          continue;
        try
        {
          var candidate = OsuBeatmapFile.Load(osuFile);
          if (candidate.IsOsuTrainerMap)
            continue;
          if (string.IsNullOrEmpty(candidate.Version))
            continue;

          if (
            trainerMap.Version.StartsWith(candidate.Version + " ", StringComparison.Ordinal)
            && candidate.Version.Length > bestLength
          )
          {
            bestMatch = osuFile;
            bestLength = candidate.Version.Length;
          }
        }
        catch (Exception ex)
        {
          LogUtils.DebugLogger(
            $"[Trainer] Failed to load candidate beatmap ({osuFile}): {ex.Message}",
            true
          );
        }
      }

      return bestMatch;
    }

    public async Task GenerateBeatmapsBatchAsync(
      string beatmapPath,
      IEnumerable<BatchGenerationRequest> requests,
      bool adjustPitchWithSpeed,
      bool randomizeEnabled,
      Action<string>? progress = null,
      CancellationToken ct = default
    )
    {
      await Task.Run(() =>
      {
        progress?.Invoke("Loading beatmap...");
        var original = OsuBeatmapFile.Load(beatmapPath);
        string songDir = Path.GetDirectoryName(beatmapPath)!;

        List<string>? randomizedHitObjectLines = null;
        if (randomizeEnabled && RandomModApplier.IsSupported(original.Mode))
        {
          randomizedHitObjectLines = RandomModApplier.Apply(
            original.ExtractHitObjectLines(),
            original.Mode,
            original.CircleSize,
            new Random()
          );
        }

        var osuFilesToAdd = new List<(string tempOsuPath, string newOsuFilename)>();
        var failures = new List<(decimal rate, string reason)>();

        try
        {
          foreach (var req in requests)
          {
            ct.ThrowIfCancellationRequested();
            string tempOsuPath = "";
            try
            {
              if (req.Rate <= 0)
                throw new ArgumentOutOfRangeException(
                  nameof(requests),
                  req.Rate,
                  "rate must be greater than zero."
                );

              string rateStr = req.Rate.ToString("0.0#");
              string newVersion = $"{original.Version} {rateStr}x";

              var diffSuffixes = new List<string>();
              if (randomizedHitObjectLines != null)
                diffSuffixes.Add("Random");
              if (
                req.ArOverride.HasValue
                && original.ApproachRate >= 0
                && Math.Abs(req.ArOverride.Value - original.ApproachRate)
                  > DifficultyChangeThreshold
              )
                diffSuffixes.Add($"AR{req.ArOverride:F1}");
              if (
                req.OdOverride.HasValue
                && original.OverallDifficulty >= 0
                && Math.Abs(req.OdOverride.Value - original.OverallDifficulty)
                  > DifficultyChangeThreshold
              )
                diffSuffixes.Add($"OD{req.OdOverride:F1}");
              if (
                req.HpOverride.HasValue
                && original.HPDrainRate >= 0
                && Math.Abs(req.HpOverride.Value - original.HPDrainRate) > DifficultyChangeThreshold
              )
                diffSuffixes.Add($"HP{req.HpOverride:F1}");
              if (
                req.CsOverride.HasValue
                && original.CircleSize >= 0
                && Math.Abs(req.CsOverride.Value - original.CircleSize) > DifficultyChangeThreshold
              )
                diffSuffixes.Add($"CS{req.CsOverride:F1}");
              if (diffSuffixes.Count > 0)
                newVersion += $" [{string.Join(" ", diffSuffixes)}]";

              string audioBase = Path.GetFileNameWithoutExtension(original.AudioFilename);
              string newAudioName = $"{audioBase} {req.Rate:0.000}x";
              if (adjustPitchWithSpeed && Math.Abs(req.Rate - 1M) > 0.001M)
                newAudioName += $" (pitch {(req.Rate < 1 ? "lowered" : "raised")})";
              newAudioName += ".mp3";

              string artist = OsuBeatmapFile.NormalizeForFilename(original.Artist);
              string title = OsuBeatmapFile.NormalizeForFilename(original.Title);
              string creator = OsuBeatmapFile.NormalizeForFilename(original.Creator);
              string diffName = OsuBeatmapFile.NormalizeForFilename(newVersion);
              string newOsuFilename = $"{artist} - {title} ({creator}) [{diffName}].osu";

              var tags = new List<string>(original.Tags);
              if (!tags.Contains("osutrainer"))
                tags.Add("osutrainer");

              string newAudioPath = Path.Combine(songDir, newAudioName);
              bool needMp3 = !File.Exists(newAudioPath);

              if (needMp3)
              {
                progress?.Invoke($"Generating audio... ({req.Rate:0.0#}x)");
                string inAudio = Path.Combine(songDir, original.AudioFilename);
                SongSpeedChanger.GenerateAudioFile(
                  inAudio,
                  newAudioPath,
                  req.Rate,
                  adjustPitchWithSpeed
                );
              }

              progress?.Invoke($"Generating beatmap... ({req.Rate:0.0#}x)");
              tempOsuPath = Path.GetTempFileName();

              var mapToSave = OsuBeatmapFile.Load(beatmapPath);
              mapToSave.Version = newVersion;
              mapToSave.AudioFilename = newAudioName;
              mapToSave.Tags = tags;
              if (randomizedHitObjectLines != null)
                mapToSave.ReplaceHitObjectLines(randomizedHitObjectLines);
              mapToSave.SaveWithRate(
                tempOsuPath,
                req.Rate,
                req.ArOverride,
                req.OdOverride,
                req.HpOverride,
                req.CsOverride
              );

              osuFilesToAdd.Add((tempOsuPath, newOsuFilename));
            }
            catch (Exception ex)
            {
              LogUtils.DebugLogger(
                $"[Trainer] Failed to generate Rate {req.Rate:0.0#}x: {ex.Message}",
                true
              );
              failures.Add((req.Rate, ex.Message));
              if (!string.IsNullOrEmpty(tempOsuPath))
              {
                try
                {
                  if (File.Exists(tempOsuPath))
                    File.Delete(tempOsuPath);
                }
                catch (Exception delEx)
                {
                  LogUtils.DebugLogger(
                    $"[Trainer] Failed to delete temporary file: {delEx.Message}",
                    true
                  );
                }
              }
            }
          }

          if (osuFilesToAdd.Count == 0)
          {
            string detail = string.Join(" / ", failures.Select(f => $"{f.rate:0.0#}x: {f.reason}"));
            throw new InvalidOperationException($"Failed to generate for all Rates. {detail}");
          }

          ct.ThrowIfCancellationRequested();
          progress?.Invoke("Creating .osz...");
          AddNewBeatmapsToSongFolder(songDir, osuFilesToAdd);

          if (failures.Count > 0)
          {
            string failedRates = string.Join(", ", failures.Select(f => $"{f.rate:0.0#}x"));
            progress?.Invoke($"Done! (failed: {failedRates})");
          }
          else
          {
            progress?.Invoke("Done!");
          }
        }
        finally
        {
          foreach (var (tempOsuPath, _) in osuFilesToAdd)
          {
            try
            {
              if (File.Exists(tempOsuPath))
                File.Delete(tempOsuPath);
            }
            catch (Exception ex)
            {
              LogUtils.DebugLogger(
                $"[Trainer] Failed to delete temporary file: {ex.Message}",
                true
              );
            }
          }
        }
      });
    }

    private static void AddNewBeatmapsToSongFolder(
      string songDir,
      IEnumerable<(string tempOsuPath, string newOsuFilename)> osuFiles
    )
    {
      string oszPath = Path.Combine(
        Path.GetTempPath(),
        $"{Path.GetFileName(songDir)}-{Guid.NewGuid():N}.osz"
      );

      try
      {
        ZipFile.CreateFromDirectory(songDir, oszPath);
      }
      catch (Exception ex)
      {
        LogUtils.DebugLogger($"[Trainer] .osz creation error: {ex.Message}", true);
        throw;
      }

      using (var archive = ZipFile.Open(oszPath, ZipArchiveMode.Update))
      {
        foreach (var osu in osuFiles)
        {
          archive.CreateEntryFromFile(osu.tempOsuPath, osu.newOsuFilename);
        }
      }

      var proc = new System.Diagnostics.Process();
      proc.StartInfo.FileName = oszPath;
      proc.StartInfo.UseShellExecute = true;
      try
      {
        proc.Start();
      }
      catch (Exception ex)
      {
        LogUtils.DebugLogger($"[Trainer] .osz launch error: {ex.Message}", true);
        throw new InvalidOperationException(
          "Failed to open .osz file.\nPlease check if .osz files are associated with osu!.",
          ex
        );
      }
    }
  }
}
