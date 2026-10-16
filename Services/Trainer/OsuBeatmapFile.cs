using System.Globalization;
using System.IO;
using System.Text;

namespace OsuMate.Services.Trainer
{
  internal sealed record OsuBeatmapMetadata(
    string Title,
    string Artist,
    string Creator,
    string Version,
    int BeatmapID,
    int Mode,
    decimal CircleSize
  );

  internal class OsuBeatmapFile
  {
    public string Filename { get; private set; } = "";
    public string Title { get; private set; } = "";
    public string Artist { get; private set; } = "";
    public string Creator { get; private set; } = "";
    public string Version { get; set; } = "";
    public string AudioFilename { get; set; } = "";
    public int BeatmapID { get; private set; } = -1;
    public List<string> Tags { get; set; } = [];

    public bool IsOsuTrainerMap => Tags.Contains("osutrainer");

    public string? SourceOsuFileName { get; private set; }

    internal const string SourceMarkerPrefix = "// osutrainer:source=";

    public decimal ApproachRate { get; private set; } = -1M;
    public decimal OverallDifficulty { get; private set; } = -1M;
    public decimal HPDrainRate { get; private set; } = -1M;
    public decimal CircleSize { get; private set; } = -1M;

    public int Mode { get; private set; } = 0;

    public bool IsTaikoOrMania => Mode == Models.OsuMode.Taiko || Mode == Models.OsuMode.Mania;

    public bool IsCatch => Mode == Models.OsuMode.Catch;

    public decimal DominantBpm { get; private set; }
    public decimal MinBpm { get; private set; }
    public decimal MaxBpm { get; private set; }

    public int PreviewTime { get; private set; } = -1;

    public double LastHitObjectTime { get; private set; }

    private readonly List<UninheritedTimingPoint> _uninheritedTimingPoints = [];

    public IReadOnlyList<UninheritedTimingPoint> UninheritedTimingPoints => _uninheritedTimingPoints;

    private List<string> _rawLines = [];

    internal enum Section
    {
      None,
      General,
      Metadata,
      Difficulty,
      Events,
      TimingPoints,
      HitObjects,
      Editor,
      Other,
    }

    private OsuBeatmapFile() { }

    public static int TryReadMode(string filePath)
    {
      try
      {
        var section = Section.None;
        foreach (var raw in File.ReadLines(filePath, Encoding.UTF8))
        {
          var line = raw.Trim();
          if (line == "" || line.StartsWith("//", StringComparison.Ordinal))
            continue;

          if (line.StartsWith('['))
          {
            if (section == Section.General)
              return 0;
            section = ParseSection(line);
            continue;
          }

          if (section == Section.General && TryKV(line, "Mode", out var modeStr))
            return int.TryParse(modeStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int mode)
              ? mode
              : -1;
        }

        return section == Section.General ? 0 : -1;
      }
      catch (Exception e) when (e is IOException or UnauthorizedAccessException)
      {
        return -1;
      }
    }

    internal static Section ParseSection(string line) =>
      line switch
      {
        "[General]" => Section.General,
        "[Metadata]" => Section.Metadata,
        "[Difficulty]" => Section.Difficulty,
        "[Events]" => Section.Events,
        "[TimingPoints]" => Section.TimingPoints,
        "[HitObjects]" => Section.HitObjects,
        "[Editor]" => Section.Editor,
        _ => Section.Other,
      };

    public static OsuBeatmapFile Load(string filePath)
    {
      var bm = new OsuBeatmapFile { Filename = filePath };
      bm._rawLines = new List<string>(File.ReadLines(filePath, Encoding.UTF8));
      bm.Parse();
      return bm;
    }

    private void Parse()
    {
      var section = Section.None;
      var bpmPoints = new List<(double time, double beatLength)>();
      double lastHitObjectTime = 0;

      foreach (var raw in _rawLines)
      {
        var line = raw.Trim();
        if (line.StartsWith(SourceMarkerPrefix, StringComparison.Ordinal))
          SourceOsuFileName = line[SourceMarkerPrefix.Length..].Trim();
        if (line.StartsWith("//", StringComparison.Ordinal) || line == "")
          continue;

        if (line.StartsWith('['))
        {
          section = ParseSection(line);
          continue;
        }

        switch (section)
        {
          case Section.General:
          case Section.Metadata:
          case Section.Difficulty:
            ParseHeaderSectionLine(section, line);
            break;

          case Section.TimingPoints:
            if (TimingPointParser.TryParseUninherited(line, out var timingPoint))
            {
              bpmPoints.Add((timingPoint.Time, timingPoint.BeatLength));
              _uninheritedTimingPoints.Add(timingPoint);
            }
            break;

          case Section.HitObjects:
            ParseHitObjectTime(line, ref lastHitObjectTime);
            break;
        }
      }

      LastHitObjectTime = lastHitObjectTime;

      var bpm =BeatmapBpmCalculator.Compute(bpmPoints, lastHitObjectTime);
      if (bpm is { } summary)
      {
        DominantBpm = summary.Dominant;
        MinBpm = summary.Min;
        MaxBpm = summary.Max;
      }
    }

    public List<string> ExtractHitObjectLines()
    {
      var lines = new List<string>();
      var section = Section.None;

      foreach (var raw in _rawLines)
      {
        var line = raw.Trim();
        if (line.StartsWith('['))
        {
          section = ParseSection(line);
          continue;
        }

        if (section == Section.HitObjects && line != "" && !line.StartsWith("//", StringComparison.Ordinal))
          lines.Add(raw);
      }

      return lines;
    }

    internal IReadOnlyList<string> RawLines => _rawLines;

    internal List<string> WithHitObjectLines(IReadOnlyList<string> newLines)
    {
      var result = new List<string>(_rawLines.Count + newLines.Count);
      var section = Section.None;
      bool replaced = false;

      foreach (var raw in _rawLines)
      {
        var line = raw.Trim();
        if (line.StartsWith('['))
        {
          section = ParseSection(line);
          result.Add(raw);
          if (section == Section.HitObjects)
          {
            result.AddRange(newLines);
            replaced = true;
          }
          continue;
        }

        if (section == Section.HitObjects)
          continue;

        result.Add(raw);
      }

      if (!replaced)
      {
        result.Add("[HitObjects]");
        result.AddRange(newLines);
      }

      return result;
    }

    private void ParseHeaderSectionLine(Section section, string line)
    {
      switch (section)
      {
        case Section.General:
          if (TryKV(line, "AudioFilename", out var af))
            AudioFilename = af;
          if (TryKV(line, "Mode", out var modeStr) && int.TryParse(modeStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int mode))
            Mode = mode;
          if (TryKV(line, "PreviewTime", out var previewStr) && int.TryParse(previewStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int previewTime))
            PreviewTime = previewTime;
          break;

        case Section.Metadata:
          if (TryKV(line, "Title", out var title))
            Title = title;
          if (TryKV(line, "Artist", out var artist))
            Artist = artist;
          if (TryKV(line, "Creator", out var creator))
            Creator = creator;
          if (TryKV(line, "Version", out var version))
            Version = version;
          if (TryKV(line, "BeatmapID", out var bidStr) && int.TryParse(bidStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int bid))
            BeatmapID = bid;
          if (TryKV(line, "Tags", out var tags))
            Tags = [.. tags.Split(' ', StringSplitOptions.RemoveEmptyEntries)];
          break;

        case Section.Difficulty:
          if (TryKVDecimal(line, "ApproachRate", out var ar))
            ApproachRate = ar;
          if (TryKVDecimal(line, "OverallDifficulty", out var od))
            OverallDifficulty = od;
          if (TryKVDecimal(line, "HPDrainRate", out var hp))
            HPDrainRate = hp;
          if (TryKVDecimal(line, "CircleSize", out var cs))
            CircleSize = cs;
          break;
      }
    }

    internal static OsuBeatmapFile LoadHeaderOnly(string filePath)
    {
      var bm = new OsuBeatmapFile { Filename = filePath };
      bm.ParseMetadataOnly(filePath);
      return bm;
    }

    public static OsuBeatmapMetadata LoadMetadataOnly(string filePath)
    {
      var bm = new OsuBeatmapFile { Filename = filePath };
      bm.ParseMetadataOnly(filePath);
      return new OsuBeatmapMetadata(
        bm.Title,
        bm.Artist,
        bm.Creator,
        bm.Version,
        bm.BeatmapID,
        bm.Mode,
        bm.CircleSize
      );
    }

    private void ParseMetadataOnly(string filePath)
    {
      var section = Section.None;
      bool reachedDifficultySection = false;

      foreach (var raw in File.ReadLines(filePath, Encoding.UTF8))
      {
        var line = raw.Trim();
        if (line.StartsWith("//", StringComparison.Ordinal) || line == "")
          continue;

        if (line.StartsWith('['))
        {
          if (reachedDifficultySection)
            break;

          section = ParseSection(line);
          if (section == Section.Difficulty)
            reachedDifficultySection = true;
          continue;
        }

        if (section is Section.General or Section.Metadata or Section.Difficulty)
          ParseHeaderSectionLine(section, line);
      }
    }

    internal static bool TryKV(string line, string key, out string value)
    {
      value = "";
      if (
        line.Length <= key.Length
        || line[key.Length] != ':'
        || !line.StartsWith(key, StringComparison.Ordinal)
      )
        return false;
      value = line[(key.Length + 1)..].Trim();
      return true;
    }

    internal static bool TryKVDecimal(string line, string key, out decimal value)
    {
      value = 0;
      if (!TryKV(line, key, out var raw))
        return false;
      return decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static void ParseHitObjectTime(string line, ref double lastTime)
    {
      var parts = line.Split(',');
      if (parts.Length < 3)
        return;
      if (double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double t))
        lastTime = Math.Max(lastTime, t);
    }
  }
}
