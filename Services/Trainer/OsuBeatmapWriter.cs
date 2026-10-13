using System.Globalization;
using System.IO;
using System.Text;

namespace OsuMate.Services.Trainer
{
  internal static class OsuBeatmapWriter
  {
    public static void Write(OsuBeatmapFile source, string outputPath, BeatmapEditOptions options)
    {
      decimal rate = options.Rate;
      decimal? arOverride = options.ArOverride;
      decimal? odOverride = options.OdOverride;
      decimal? hpOverride = options.HpOverride;
      decimal? csOverride = options.CsOverride;
      bool disableSv = options.DisableSv;

      if (rate <= 0)
        throw new ArgumentOutOfRangeException(
          nameof(rate),
          rate,
          "rate must be greater than zero."
        );

      IReadOnlyList<string> rawLines = options.HitObjectLines != null
        ? source.WithHitObjectLines(options.HitObjectLines)
        : source.RawLines;

      var outLines = new List<string>();
      var section = OsuBeatmapFile.Section.None;

      foreach (var raw in rawLines)
      {
        var line = raw.Trim();

        if (line.StartsWith('['))
        {
          section = OsuBeatmapFile.ParseSection(line);
          outLines.Add(raw);
          continue;
        }

        switch (section)
        {
          case OsuBeatmapFile.Section.General:
            if (OsuBeatmapFile.TryKV(line, "AudioFilename", out _))
              outLines.Add($"AudioFilename: {options.AudioFilename}");
            else if (OsuBeatmapFile.TryKV(line, "PreviewTime", out var ptStr) && int.TryParse(ptStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int pt))
            {
              int newPt = pt < 0 ? pt : (int)Math.Round(pt / (double)rate);
              outLines.Add($"PreviewTime: {newPt}");
            }
            else
              outLines.Add(raw);
            break;

          case OsuBeatmapFile.Section.Metadata:
            if (OsuBeatmapFile.TryKV(line, "Version", out _))
              outLines.Add($"Version:{options.Version}");
            else if (OsuBeatmapFile.TryKV(line, "Tags", out _))
              outLines.Add($"Tags:{string.Join(" ", options.Tags)}");
            else if (OsuBeatmapFile.TryKV(line, "BeatmapID", out _))
              outLines.Add("BeatmapID:0");
            else if (OsuBeatmapFile.TryKV(line, "BeatmapSetID", out _))
              outLines.Add("BeatmapSetID:-1");
            else
              outLines.Add(raw);
            break;

          case OsuBeatmapFile.Section.Difficulty:
            outLines.Add(
              OverrideDifficultyLine(raw, arOverride, odOverride, hpOverride, csOverride)
            );
            break;

          case OsuBeatmapFile.Section.Events:

            var eventLine = FilterAndScaleEventLine(raw, rate);
            if (eventLine != null)
              outLines.Add(eventLine);
            break;

          case OsuBeatmapFile.Section.TimingPoints:
            var tpLine = ScaleTimingPointLine(raw, rate, disableSv);
            if (tpLine != null)
              outLines.Add(tpLine);
            break;

          case OsuBeatmapFile.Section.HitObjects:
            outLines.Add(ScaleHitObjectLine(raw, rate));
            break;

          case OsuBeatmapFile.Section.Editor:
            outLines.Add(ScaleBookmarksLine(raw, rate));
            break;

          default:
            outLines.Add(raw);
            break;
        }
      }

      string marker = OsuBeatmapFile.SourceMarkerPrefix + Path.GetFileName(source.Filename);
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
      if (ar.HasValue && OsuBeatmapFile.TryKVDecimal(line, "ApproachRate", out _))
        return $"ApproachRate:{ar.Value.ToString("F10", CultureInfo.InvariantCulture)}";
      if (od.HasValue && OsuBeatmapFile.TryKVDecimal(line, "OverallDifficulty", out _))
        return $"OverallDifficulty:{od.Value.ToString("F10", CultureInfo.InvariantCulture)}";
      if (hp.HasValue && OsuBeatmapFile.TryKVDecimal(line, "HPDrainRate", out _))
        return $"HPDrainRate:{hp.Value.ToString("F10", CultureInfo.InvariantCulture)}";
      if (cs.HasValue && OsuBeatmapFile.TryKVDecimal(line, "CircleSize", out _))
        return $"CircleSize:{cs.Value.ToString("F10", CultureInfo.InvariantCulture)}";
      return raw;
    }

    private static string ScaleBookmarksLine(string raw, decimal rate)
    {
      if (!OsuBeatmapFile.TryKV(raw.Trim(), "Bookmarks", out var value))
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

    private static string? ScaleTimingPointLine(string raw, decimal rate, bool disableSv = false)
    {
      var line = raw.Trim();
      if (line == "" || line.StartsWith("//", StringComparison.Ordinal))
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

      if (disableSv && uninherited == 0)
        return null;

      double newTime = time / (double)rate;
      double newBeatLength = beatLength / (double)rate;

      parts[0] = ((int)Math.Round(newTime)).ToString(CultureInfo.InvariantCulture);
      if (disableSv)
      {
        parts[1] = newBeatLength.ToString("F10", CultureInfo.InvariantCulture);
        parts[5] = "100";
        if (parts.Length >= 8 && int.TryParse(parts[7].Trim(), out int effects))
          parts[7] = (effects & ~(1 | 8)).ToString(CultureInfo.InvariantCulture);
      }
      else
      {
        parts[1] = uninherited == 1 ? newBeatLength.ToString("F10", CultureInfo.InvariantCulture) : beatLength.ToString("F10", CultureInfo.InvariantCulture);
      }

      return string.Join(",", parts);
    }

    private static string ScaleHitObjectLine(string raw, decimal rate)
    {
      var line = raw.Trim();
      if (line == "" || line.StartsWith("//", StringComparison.Ordinal))
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
      if (line == "" || line.StartsWith("//", StringComparison.Ordinal))
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
  }
}
