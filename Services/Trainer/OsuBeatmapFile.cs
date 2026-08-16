using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace OsuMate.Services.Trainer
{
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

    private const string SourceMarkerPrefix = "// osutrainer:source=";

    public decimal ApproachRate { get; private set; } = -1M;
    public decimal OverallDifficulty { get; private set; } = -1M;
    public decimal HPDrainRate { get; private set; } = -1M;
    public decimal CircleSize { get; private set; } = -1M;

    public int Mode { get; private set; } = 0;

    public bool IsTaikoOrMania => Mode == 1 || Mode == 3;

    public decimal DominantBpm { get; private set; }
    public decimal MinBpm { get; private set; }
    public decimal MaxBpm { get; private set; }

    private List<string> _rawLines = [];

    private enum Section
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

    public static OsuBeatmapFile Load(string filePath)
    {
      var bm = new OsuBeatmapFile { Filename = filePath };
      bm._rawLines = File.ReadAllLines(filePath, Encoding.UTF8).ToList();
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
        if (line.StartsWith("//") || line == "")
          continue;

        if (line.StartsWith('['))
        {
          section = line switch
          {
            "[General]" => Section.General,
            "[Metadata]" => Section.Metadata,
            "[Difficulty]" => Section.Difficulty,
            "[Events]" => Section.Events,
            "[TimingPoints]" => Section.TimingPoints,
            "[HitObjects]" => Section.HitObjects,
            _ => Section.Other,
          };
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
            ParseTimingPoint(line, bpmPoints);
            break;

          case Section.HitObjects:
            ParseHitObjectTime(line, ref lastHitObjectTime);
            break;
        }
      }

      CalcBpm(bpmPoints, lastHitObjectTime);
    }

    private void ParseHeaderSectionLine(Section section, string line)
    {
      switch (section)
      {
        case Section.General:
          if (TryKV(line, "AudioFilename", out var af))
            AudioFilename = af;
          if (TryKV(line, "Mode", out var modeStr) && int.TryParse(modeStr, out int mode))
            Mode = mode;
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
          if (TryKV(line, "BeatmapID", out var bidStr) && int.TryParse(bidStr, out int bid))
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

    public static OsuBeatmapFile LoadMetadataOnly(string filePath)
    {
      var bm = new OsuBeatmapFile { Filename = filePath };
      bm.ParseMetadataOnly(filePath);
      return bm;
    }

    private void ParseMetadataOnly(string filePath)
    {
      var section = Section.None;
      bool reachedDifficultySection = false;

      foreach (var raw in File.ReadLines(filePath, Encoding.UTF8))
      {
        var line = raw.Trim();
        if (line.StartsWith("//") || line == "")
          continue;

        if (line.StartsWith('['))
        {
          if (reachedDifficultySection)
            break;

          section = line switch
          {
            "[General]" => Section.General,
            "[Metadata]" => Section.Metadata,
            "[Difficulty]" => Section.Difficulty,
            _ => Section.Other,
          };
          if (section == Section.Difficulty)
            reachedDifficultySection = true;
          continue;
        }

        if (section is Section.General or Section.Metadata or Section.Difficulty)
          ParseHeaderSectionLine(section, line);
      }
    }

    private static bool TryKV(string line, string key, out string value)
    {
      value = "";
      if (!line.StartsWith(key + ":"))
        return false;
      value = line[(key.Length + 1)..].Trim();
      return true;
    }

    private static bool TryKVDecimal(string line, string key, out decimal value)
    {
      value = 0;
      if (!TryKV(line, key, out var raw))
        return false;
      return decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static void ParseTimingPoint(string line, List<(double, double)> bpmPoints)
    {
      var parts = line.Split(',');
      if (parts.Length < 7)
        return;
      if (
        !double.TryParse(
          parts[0],
          NumberStyles.Float,
          CultureInfo.InvariantCulture,
          out double time
        )
      )
        return;
      if (
        !double.TryParse(
          parts[1],
          NumberStyles.Float,
          CultureInfo.InvariantCulture,
          out double beatLength
        )
      )
        return;
      if (!int.TryParse(parts[6].Trim(), out int uninherited))
        return;
      if (uninherited == 1 && beatLength > 0)
        bpmPoints.Add((time, beatLength));
    }

    private static void ParseHitObjectTime(string line, ref double lastTime)
    {
      var parts = line.Split(',');
      if (parts.Length < 3)
        return;
      if (double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double t))
        lastTime = Math.Max(lastTime, t);
    }

    private void CalcBpm(List<(double time, double beatLength)> pts, double lastHitObjectTime)
    {
      if (pts.Count == 0)
        return;
      pts.Sort((a, b) => a.time.CompareTo(b.time));

      var durations = new Dictionary<double, double>();
      for (int i = 0; i < pts.Count; i++)
      {
        double endTime =
          (i + 1 < pts.Count) ? pts[i + 1].time : Math.Max(pts[i].time, lastHitObjectTime);
        double dur = Math.Max(0, endTime - pts[i].time);
        var bl = pts[i].beatLength;
        if (durations.ContainsKey(bl))
          durations[bl] += dur;
        else
          durations[bl] = dur;
      }

      var dominantBl = durations.OrderByDescending(kv => kv.Value).First().Key;
      DominantBpm = ToSafeBpm(60000.0 / dominantBl);
      MinBpm = ToSafeBpm(60000.0 / pts.Max(p => p.beatLength));
      MaxBpm = ToSafeBpm(60000.0 / pts.Min(p => p.beatLength));
    }

    private const decimal SafeBpmBound = 1_000_000_000_000m;

    private static decimal ToSafeBpm(double bpm)
    {
      if (double.IsNaN(bpm))
        return 0m;
      if (bpm >= (double)SafeBpmBound)
        return SafeBpmBound;
      if (bpm <= -(double)SafeBpmBound)
        return -SafeBpmBound;
      return (decimal)bpm;
    }

    public static decimal ComputeNewAR(decimal ar, decimal rate)
    {
      double arMs =
        (double)ar <= 5.0 ? 1800.0 - 120.0 * (double)ar : 1200.0 - 150.0 * ((double)ar - 5.0);
      double newArMs = arMs / (double)rate;
      double newAr =
        newArMs >= 1200.0 ? (1800.0 - newArMs) / 120.0 : 5.0 + (1200.0 - newArMs) / 150.0;
      return (decimal)Math.Clamp(newAr, 0.0, 11.0);
    }

    public static decimal ComputeNewOD(decimal od, decimal rate)
    {
      double odMs = 80.0 - 6.0 * (double)od;
      double newOdMs = odMs / (double)rate;
      double newOd = (80.0 - newOdMs) / 6.0;
      return (decimal)Math.Clamp(newOd, 0.0, 11.0);
    }

    public void SaveWithRate(
      string outputPath,
      decimal rate,
      decimal? arOverride = null,
      decimal? odOverride = null,
      decimal? hpOverride = null,
      decimal? csOverride = null
    )
    {
      if (rate <= 0)
        throw new ArgumentOutOfRangeException(
          nameof(rate),
          rate,
          "rate must be greater than zero."
        );

      var outLines = new List<string>();
      var section = Section.None;

      foreach (var raw in _rawLines)
      {
        var line = raw.Trim();

        if (line.StartsWith('['))
        {
          section = line switch
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
          outLines.Add(raw);
          continue;
        }

        switch (section)
        {
          case Section.General:
            if (TryKV(line, "AudioFilename", out _))
              outLines.Add($"AudioFilename: {AudioFilename}");
            else if (TryKV(line, "PreviewTime", out var ptStr) && int.TryParse(ptStr, out int pt))
            {
              int newPt = pt < 0 ? pt : (int)Math.Round(pt / (double)rate);
              outLines.Add($"PreviewTime: {newPt}");
            }
            else
              outLines.Add(raw);
            break;

          case Section.Metadata:
            if (TryKV(line, "Version", out _))
              outLines.Add($"Version:{Version}");
            else if (TryKV(line, "Tags", out _))
              outLines.Add($"Tags:{string.Join(" ", Tags)}");
            else if (TryKV(line, "BeatmapID", out _))
              outLines.Add("BeatmapID:0");
            else if (TryKV(line, "BeatmapSetID", out _))
              outLines.Add("BeatmapSetID:-1");
            else
              outLines.Add(raw);
            break;

          case Section.Difficulty:
            outLines.Add(
              OverrideDifficultyLine(raw, arOverride, odOverride, hpOverride, csOverride)
            );
            break;

          case Section.Events:

            var eventLine = FilterAndScaleEventLine(raw, rate);
            if (eventLine != null)
              outLines.Add(eventLine);
            break;

          case Section.TimingPoints:
            outLines.Add(ScaleTimingPointLine(raw, rate));
            break;

          case Section.HitObjects:
            outLines.Add(ScaleHitObjectLine(raw, rate));
            break;

          case Section.Editor:
            outLines.Add(ScaleBookmarksLine(raw, rate));
            break;

          default:
            outLines.Add(raw);
            break;
        }
      }

      string marker = SourceMarkerPrefix + Path.GetFileName(Filename);
      int markerIndex =
        (
          outLines.Count > 0
          && outLines[0]
            .TrimStart()
            .StartsWith("osu file format", StringComparison.OrdinalIgnoreCase)
        )
          ? 1
          : 0;
      outLines.Insert(markerIndex, marker);

      File.WriteAllLines(outputPath, outLines, Encoding.UTF8);
    }

    private static string OverrideDifficultyLine(
      string raw,
      decimal? ar,
      decimal? od,
      decimal? hp,
      decimal? cs
    )
    {
      var line = raw.Trim();
      if (line == "")
        return raw;
      if (ar.HasValue && TryKVDecimal(line, "ApproachRate", out _))
        return $"ApproachRate:{ar.Value.ToString("F10", CultureInfo.InvariantCulture)}";
      if (od.HasValue && TryKVDecimal(line, "OverallDifficulty", out _))
        return $"OverallDifficulty:{od.Value.ToString("F10", CultureInfo.InvariantCulture)}";
      if (hp.HasValue && TryKVDecimal(line, "HPDrainRate", out _))
        return $"HPDrainRate:{hp.Value.ToString("F10", CultureInfo.InvariantCulture)}";
      if (cs.HasValue && TryKVDecimal(line, "CircleSize", out _))
        return $"CircleSize:{cs.Value.ToString("F10", CultureInfo.InvariantCulture)}";
      return raw;
    }

    private static string ScaleBookmarksLine(string raw, decimal rate)
    {
      if (!TryKV(raw.Trim(), "Bookmarks", out var value))
        return raw;

      var times = value
        .Split(',', StringSplitOptions.RemoveEmptyEntries)
        .Select(s =>
          double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double t)
            ? ((int)Math.Round(t / (double)rate)).ToString(CultureInfo.InvariantCulture)
            : s.Trim()
        );

      return $"Bookmarks: {string.Join(",", times)}";
    }

    private static string ScaleTimingPointLine(string raw, decimal rate)
    {
      var line = raw.Trim();
      if (line == "" || line.StartsWith("//"))
        return raw;

      var parts = line.Split(',');
      if (parts.Length < 7)
        return raw;

      if (
        !double.TryParse(
          parts[0],
          NumberStyles.Float,
          CultureInfo.InvariantCulture,
          out double time
        )
      )
        return raw;
      if (
        !double.TryParse(
          parts[1],
          NumberStyles.Float,
          CultureInfo.InvariantCulture,
          out double beatLength
        )
      )
        return raw;
      if (!int.TryParse(parts[6].Trim(), out int uninherited))
        return raw;

      double newTime = time / (double)rate;
      double newBeatLength = uninherited == 1 ? beatLength / (double)rate : beatLength;

      parts[0] = ((int)Math.Round(newTime)).ToString(CultureInfo.InvariantCulture);
      parts[1] = newBeatLength.ToString("F10", CultureInfo.InvariantCulture);

      return string.Join(",", parts);
    }

    private static string ScaleHitObjectLine(string raw, decimal rate)
    {
      var line = raw.Trim();
      if (line == "" || line.StartsWith("//"))
        return raw;

      var parts = line.Split(',');
      if (parts.Length < 3)
        return raw;

      if (
        !double.TryParse(
          parts[2],
          NumberStyles.Float,
          CultureInfo.InvariantCulture,
          out double time
        )
      )
        return raw;
      parts[2] = ((int)Math.Round(time / (double)rate)).ToString(CultureInfo.InvariantCulture);

      if (parts.Length >= 5 && int.TryParse(parts[3], out int typeFlags))
      {
        bool isSpinner = (typeFlags & 8) != 0;
        bool isHold = (typeFlags & 128) != 0;

        if (isSpinner && parts.Length >= 6)
        {
          if (
            double.TryParse(
              parts[5],
              NumberStyles.Float,
              CultureInfo.InvariantCulture,
              out double endTime
            )
          )
            parts[5] = ((int)Math.Round(endTime / (double)rate)).ToString(
              CultureInfo.InvariantCulture
            );
        }
        else if (isHold && parts.Length >= 6)
        {
          var extra = parts[5].Split(':');
          if (
            extra.Length >= 1
            && double.TryParse(
              extra[0],
              NumberStyles.Float,
              CultureInfo.InvariantCulture,
              out double holdEnd
            )
          )
          {
            extra[0] = ((int)Math.Round(holdEnd / (double)rate)).ToString(
              CultureInfo.InvariantCulture
            );
            parts[5] = string.Join(":", extra);
          }
        }
      }

      return string.Join(",", parts);
    }

    private static string? FilterAndScaleEventLine(string raw, decimal rate)
    {
      var line = raw.Trim();
      if (line == "" || line.StartsWith("//"))
        return raw;

      if (raw.Length > 0 && (raw[0] == ' ' || raw[0] == '\t' || raw[0] == '_'))
        return null;

      if (line.StartsWith("0,0"))
        return raw;

      var parts = line.Split(',');
      if (parts.Length < 2)
        return raw;

      string eventType = parts[0].Trim();

      if (eventType == "1" || eventType == "Video")
        return null;

      if (eventType == "Sprite" || eventType == "Animation")
        return null;

      if ((eventType == "2" || eventType == "Break") && parts.Length >= 3)
      {
        if (
          double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double st)
        )
          parts[1] = ((int)Math.Round(st / (double)rate)).ToString(CultureInfo.InvariantCulture);
        if (
          double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double et)
        )
          parts[2] = ((int)Math.Round(et / (double)rate)).ToString(CultureInfo.InvariantCulture);
        return string.Join(",", parts);
      }

      if ((eventType == "5" || eventType == "Sample") && parts.Length >= 2)
      {
        if (
          double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double st)
        )
          parts[1] = ((int)Math.Round(st / (double)rate)).ToString(CultureInfo.InvariantCulture);
        return string.Join(",", parts);
      }

      return raw;
    }

    public static string NormalizeForFilename(string s)
    {
      var value = Regex.Replace(s ?? string.Empty, @"[""*\\/?\<>|:]", "").TrimEnd(' ', '.');
      if (string.IsNullOrWhiteSpace(value))
        return "untitled";

      var reservedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
      {
        "CON",
        "PRN",
        "AUX",
        "NUL",
        "COM1",
        "COM2",
        "COM3",
        "COM4",
        "COM5",
        "COM6",
        "COM7",
        "COM8",
        "COM9",
        "LPT1",
        "LPT2",
        "LPT3",
        "LPT4",
        "LPT5",
        "LPT6",
        "LPT7",
        "LPT8",
        "LPT9",
      };

      if (reservedNames.Contains(value))
        value = "_" + value;
      return value.Length <= 120 ? value : value[..120].TrimEnd(' ', '.');
    }
  }
}
