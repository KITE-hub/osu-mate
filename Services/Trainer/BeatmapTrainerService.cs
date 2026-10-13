using System.IO;
using System.IO.Compression;
using OsuMate.Utils;
using System.Globalization;

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

    private const int MaxOsuFileNameLength = 200;
    private const int OsuFileNameDecorationLength = 13;
    private const int MaxAudioBaseNameLength = 150;

    public BeatmapTrainerService(OsuMemoryService memory)
    {
      _memory = memory;
    }

    public string? GetCurrentBeatmapPath()
    {
      if (!_memory.IsDirectoryLoaded)
        return null;

      var (path, _) = _memory.GetCurrentBeatmap();
      return path.Length > 0 && File.Exists(path) ? path : null;
    }

    public static string? FindOriginalMap(string osutrainerMapPath)
    {
      string? dir = Path.GetDirectoryName(osutrainerMapPath);
      if (dir == null)
        return null;

      OsuBeatmapFile? trainerMap = null;
      try
      {
        trainerMap = OsuBeatmapFile.Load(osutrainerMapPath);
      }
      catch (Exception ex)
      {
        LogUtils.DebugLogger($"[Trainer] Failed to load generated beatmap: {ex}", true);
        return null;
      }

      string[] candidates;
      try
      {
        candidates = Directory.GetFiles(dir, "*.osu").OrderBy(f => f).ToArray();
      }
      catch (Exception ex)
      {
        LogUtils.DebugLogger($"[Trainer] Failed to enumerate folders: {ex}", true);
        return null;
      }

      if (!string.IsNullOrEmpty(trainerMap.SourceOsuFileName))
      {
        string? candidatePath = SafePath.CombineWithin(dir, trainerMap.SourceOsuFileName);
        if (candidatePath != null && File.Exists(candidatePath))
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
              $"[Trainer] Failed to load embedded original beatmap: {ex}",
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
            $"[Trainer] Failed to load candidate beatmap ({osuFile}): {ex}",
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
      bool disableSv,
      Action<string>? progress = null,
      CancellationToken ct = default
    )
    {
      await Task.Run(() =>
      {
        progress?.Invoke("Loading beatmap...");
        var original = OsuBeatmapFile.Load(beatmapPath);
        string songDir =
          Path.GetDirectoryName(beatmapPath)
          ?? throw new InvalidDataException($"Beatmap path has no directory: {beatmapPath}");

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
            var generated = TryGenerateRateBeatmap(
              original,
              songDir,
              req,
              adjustPitchWithSpeed,
              disableSv,
              randomizedHitObjectLines,
              progress,
              failures
            );
            if (generated.HasValue)
              osuFilesToAdd.Add(generated.Value);
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
            TryDeleteFile(tempOsuPath);
        }
      });
    }

    private static (string tempOsuPath, string newOsuFilename)? TryGenerateRateBeatmap(
      OsuBeatmapFile original,
      string songDir,
      BatchGenerationRequest req,
      bool adjustPitchWithSpeed,
      bool disableSv,
      List<string>? randomizedHitObjectLines,
      Action<string>? progress,
      List<(decimal rate, string reason)> failures
    )
    {
      string tempOsuPath = "";
      try
      {
        if (req.Rate <= 0)
          throw new ArgumentOutOfRangeException(
            nameof(req),
            req.Rate,
            "rate must be greater than zero."
          );

        string newVersion = BuildVersionName(
          original,
          req,
          disableSv,
          randomizedHitObjectLines != null
        );
        string newAudioName = BuildAudioName(original, req.Rate, adjustPitchWithSpeed);
        string newOsuFilename = BuildOsuFilename(original, newVersion);

        string? inAudio = SafePath.CombineWithin(songDir, original.AudioFilename);
        if (inAudio == null)
          throw new InvalidDataException(
            $"AudioFilename points outside the beatmap folder: {original.AudioFilename}"
          );

        string newAudioPath = Path.Combine(songDir, newAudioName);
        if (!File.Exists(newAudioPath))
        {
          progress?.Invoke($"Generating audio... ({req.Rate:0.0#}x)");
          SongSpeedChanger.GenerateAudioFile(inAudio, newAudioPath, req.Rate, adjustPitchWithSpeed);
        }

        progress?.Invoke($"Generating beatmap... ({req.Rate:0.0#}x)");
        tempOsuPath = Path.GetTempFileName();

        var tags = new List<string>(original.Tags);
        if (!tags.Contains("osutrainer"))
          tags.Add("osutrainer");

        OsuBeatmapWriter.Write(
          original,
          tempOsuPath,
          new BeatmapEditOptions(
            newVersion,
            newAudioName,
            tags,
            req.Rate,
            req.ArOverride,
            req.OdOverride,
            req.HpOverride,
            req.CsOverride,
            disableSv,
            randomizedHitObjectLines
          )
        );

        return (tempOsuPath, newOsuFilename);
      }
      catch (Exception ex)
      {
        LogUtils.DebugLogger($"[Trainer] Failed to generate Rate {req.Rate:0.0#}x: {ex}", true);
        failures.Add((req.Rate, ex.Message));
        if (!string.IsNullOrEmpty(tempOsuPath))
          TryDeleteFile(tempOsuPath);
        return null;
      }
    }

    private static string BuildVersionName(
      OsuBeatmapFile original,
      BatchGenerationRequest req,
      bool disableSv,
      bool randomized
    )
    {
      string rateStr = req.Rate.ToString("0.0#", CultureInfo.InvariantCulture);
      string newVersion = $"{original.Version} {rateStr}x";

      var diffSuffixes = new List<string>();
      if (randomized)
        diffSuffixes.Add("Random");
      if (disableSv)
        diffSuffixes.Add("NoSV");
      diffSuffixes.AddRange(BuildDifficultySuffixes(original, req));

      return diffSuffixes.Count > 0 ? $"{newVersion} [{string.Join(" ", diffSuffixes)}]" : newVersion;
    }

    private static IEnumerable<string> BuildDifficultySuffixes(
      OsuBeatmapFile original,
      BatchGenerationRequest req
    )
    {
      var overrides = new (string Label, decimal? Requested, decimal Original)[]
      {
        ("AR", req.ArOverride, original.ApproachRate),
        ("OD", req.OdOverride, original.OverallDifficulty),
        ("HP", req.HpOverride, original.HPDrainRate),
        ("CS", req.CsOverride, original.CircleSize),
      };

      foreach (var (label, requested, originalValue) in overrides)
      {
        if (!requested.HasValue)
          continue;

        var resolved = TrainerCalculationService.ResolveOverride(
          originalValue >= 0 ? originalValue : null,
          requested.Value
        );
        if (resolved.HasValue)
          yield return string.Create(CultureInfo.InvariantCulture, $"{label}{resolved.Value:F1}");
      }
    }

    private static string BuildAudioName(
      OsuBeatmapFile original,
      decimal rate,
      bool adjustPitchWithSpeed
    )
    {
      string audioBase = Path.GetFileNameWithoutExtension(original.AudioFilename);
      if (audioBase.Length > MaxAudioBaseNameLength)
        audioBase = audioBase[..MaxAudioBaseNameLength].TrimEnd(' ', '.');

      string newAudioName = string.Create(CultureInfo.InvariantCulture, $"{audioBase} {rate:0.000}x");
      if (adjustPitchWithSpeed && Math.Abs(rate - 1M) > 0.001M)
        newAudioName += $" (pitch {(rate < 1 ? "lowered" : "raised")})";
      return newAudioName + ".mp3";
    }

    private static readonly HashSet<char> InvalidFileNameChars = [.. Path.GetInvalidFileNameChars()];

    private static string NormalizeForFilename(string s)
    {
      var value = string.Concat(
        (s ?? string.Empty).Where(c => !InvalidFileNameChars.Contains(c) && !char.IsControl(c))
      ).TrimEnd(' ', '.');
      if (string.IsNullOrWhiteSpace(value))
        return "untitled";

      return value.Length <= 120 ? value : value[..120].TrimEnd(' ', '.');
    }

    private static string BuildOsuFilename(OsuBeatmapFile original, string newVersion)
    {
      string[] parts = FitToBudget(
        [
          NormalizeForFilename(original.Artist),
          NormalizeForFilename(original.Title),
          NormalizeForFilename(original.Creator),
          NormalizeForFilename(newVersion),
        ],
        MaxOsuFileNameLength - OsuFileNameDecorationLength
      );
      return $"{parts[0]} - {parts[1]} ({parts[2]}) [{parts[3]}].osu";
    }

    private static string[] FitToBudget(string[] parts, int budget)
    {
      var fitted = (string[])parts.Clone();
      int lastIndex = fitted.Length - 1;

      while (fitted.Sum(p => p.Length) > budget)
      {
        int longest = 0;
        for (int i = 1; i < fitted.Length; i++)
        {
          if (fitted[i].Length > fitted[longest].Length)
            longest = i;
        }

        if (fitted[longest].Length <= 1)
          break;

        fitted[longest] =
          longest == lastIndex
            ? fitted[longest][1..].TrimStart(' ')
            : fitted[longest][..^1].TrimEnd(' ', '.');
        if (fitted[longest].Length == 0)
          fitted[longest] = "_";
      }

      return fitted;
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

      bool launched = false;
      try
      {
        try
        {
          ZipFile.CreateFromDirectory(songDir, oszPath, CompressionLevel.Fastest, false);
        }
        catch (Exception ex)
        {
          LogUtils.DebugLogger($"[Trainer] .osz creation error: {ex}", true);
          throw;
        }

        using (var archive = ZipFile.Open(oszPath, ZipArchiveMode.Update))
        {
          foreach (var osu in osuFiles)
          {
            archive.CreateEntryFromFile(osu.tempOsuPath, osu.newOsuFilename);
          }
        }

        using var proc = new System.Diagnostics.Process();
        proc.StartInfo.FileName = oszPath;
        proc.StartInfo.UseShellExecute = true;
        try
        {
          proc.Start();
          launched = true;
        }
        catch (Exception ex)
        {
          LogUtils.DebugLogger($"[Trainer] .osz launch error: {ex}", true);
          throw new InvalidOperationException(
            "Failed to open .osz file.\nPlease check if .osz files are associated with osu!.",
            ex
          );
        }
      }
      finally
      {
        if (!launched)
          TryDeleteFile(oszPath);
      }
    }

    private static void TryDeleteFile(string path)
    {
      try
      {
        File.Delete(path);
      }
      catch (Exception ex)
      {
        LogUtils.DebugLogger($"[Trainer] Failed to delete temp file {path}: {ex}", true);
      }
    }
  }
}
