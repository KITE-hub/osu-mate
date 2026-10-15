using System.Globalization;
using System.IO;

namespace OsuMate.Services.Trainer
{
  public readonly record struct OffsetMapRequest(decimal SixteenthMaxBpm, decimal ScrollBpm);

  internal sealed record OffsetMapPlan(
    IReadOnlyList<string> TimingPointLines,
    IReadOnlyList<string> HitObjectLines,
    int ClampedScrollSectionCount,
    bool UsedFallbackStart
  );

  internal static class OffsetMapBuilder
  {
    internal const decimal MinBpmSetting = 10M;
    internal const decimal MaxBpmSetting = 1000M;

    private const double MinScrollMultiplier = 0.1;
    private const double MaxScrollMultiplier = 10.0;
    private const double BpmTolerance = 1e-9;
    private const double TimeTolerance = 1e-6;
    private const double BaseBeatLengthForMultiplier = 100.0;
    private const int SixteenthNoteBeatExponent = 2;
    private const int NotePatternLength = 4;
    private const int MaxNoteCount = 500_000;
    private const int FullVolume = 100;
    private const int NoEffects = 0;
    private const int PlayfieldCentreX = 256;
    private const int PlayfieldCentreY = 192;
    private const int CircleType = 1;

    public static OffsetMapPlan Build(OsuBeatmapFile source, OffsetMapRequest request)
    {
      ValidateRequest(request);

      var sections = source.UninheritedTimingPoints.OrderBy(p => p.Time).ToList();
      if (sections.Count == 0)
        throw new InvalidDataException("The beatmap has no uninherited timing points.");

      bool hasPreviewTime = source.PreviewTime >= 0;
      double previewTime = hasPreviewTime ? source.PreviewTime : sections[0].Time;
      double firstBarStart = FindFirstBarStart(sections, previewTime);

      var timingPointLines = new List<string>(sections.Count * 2);
      var hitObjectLines = new List<string>();
      int clampedSectionCount = 0;

      for (int i = 0; i < sections.Count; i++)
      {
        var section = sections[i];
        double sectionEnd =
          i + 1 < sections.Count ? sections[i + 1].Time : double.PositiveInfinity;
        double bpm = 60000.0 / section.BeatLength;
        if (!double.IsFinite(bpm) || bpm <= 0)
          throw new InvalidDataException(
            string.Create(
              CultureInfo.InvariantCulture,
              $"Invalid BPM at {section.Time}ms."
            )
          );

        var (scrollMultiplier, clamped) = ComputeScrollMultiplier((double)request.ScrollBpm, bpm);
        if (clamped)
          clampedSectionCount++;

        timingPointLines.Add(BuildRedLine(section));
        timingPointLines.Add(BuildGreenLine(section, scrollMultiplier));

        AppendNotes(
          hitObjectLines,
          section.Time,
          Math.Max(section.Time, firstBarStart),
          sectionEnd,
          source.LastHitObjectTime,
          ComputeNoteInterval(section.BeatLength, bpm, (double)request.SixteenthMaxBpm)
        );
      }

      if (hitObjectLines.Count == 0)
        throw new InvalidOperationException(
          "No notes to place: the first measure after PreviewTime is past the last object of the beatmap."
        );

      return new OffsetMapPlan(
        timingPointLines,
        hitObjectLines,
        clampedSectionCount,
        !hasPreviewTime
      );
    }

    private static void ValidateRequest(OffsetMapRequest request)
    {
      if (request.SixteenthMaxBpm < MinBpmSetting || request.SixteenthMaxBpm > MaxBpmSetting)
        throw new ArgumentOutOfRangeException(
          nameof(request),
          request.SixteenthMaxBpm,
          $"16th max BPM must be between {MinBpmSetting} and {MaxBpmSetting}."
        );
      if (request.ScrollBpm < MinBpmSetting || request.ScrollBpm > MaxBpmSetting)
        throw new ArgumentOutOfRangeException(
          nameof(request),
          request.ScrollBpm,
          $"Scroll BPM must be between {MinBpmSetting} and {MaxBpmSetting}."
        );
    }

    private static double FindFirstBarStart(
      IReadOnlyList<UninheritedTimingPoint> sections,
      double previewTime
    )
    {
      for (int i = 0; i < sections.Count - 1; i++)
      {
        double candidate = BarStartAtOrAfter(sections[i], previewTime);
        if (candidate < sections[i + 1].Time - TimeTolerance)
          return candidate;
      }

      return BarStartAtOrAfter(sections[^1], previewTime);
    }

    private static double BarStartAtOrAfter(UninheritedTimingPoint section, double time)
    {
      if (time <= section.Time)
        return section.Time;

      double barLength = section.BeatLength * section.Meter;
      double barCount = Math.Ceiling((time - section.Time) / barLength - TimeTolerance);
      return section.Time + barCount * barLength;
    }

    private static double ComputeNoteInterval(double beatLength, double bpm, double sixteenthMaxBpm)
    {
      double lowerBpm = sixteenthMaxBpm / 2 * (1 - BpmTolerance);
      double upperBpm = sixteenthMaxBpm * (1 + BpmTolerance);

      int shift = 0;
      while (Math.ScaleB(bpm, -shift) > upperBpm)
        shift++;
      while (Math.ScaleB(bpm, -shift) < lowerBpm)
        shift--;

      return Math.ScaleB(beatLength, shift - SixteenthNoteBeatExponent);
    }

    private static (double Multiplier, bool Clamped) ComputeScrollMultiplier(
      double scrollBpm,
      double sectionBpm
    )
    {
      double requested = scrollBpm / sectionBpm;
      double clamped = Math.Clamp(requested, MinScrollMultiplier, MaxScrollMultiplier);
      return (clamped, clamped != requested);
    }

    private static string BuildRedLine(UninheritedTimingPoint section) =>
      string.Create(
        CultureInfo.InvariantCulture,
        $"{section.Time},{section.BeatLength},{section.Meter},{section.SampleSet},{section.SampleIndex},{FullVolume},1,{NoEffects}"
      );

    private static string BuildGreenLine(UninheritedTimingPoint section, double scrollMultiplier)
    {
      double beatLength = -BaseBeatLengthForMultiplier / scrollMultiplier;
      return string.Create(
        CultureInfo.InvariantCulture,
        $"{section.Time},{beatLength:F10},{section.Meter},{section.SampleSet},{section.SampleIndex},{FullVolume},0,{NoEffects}"
      );
    }

    private static void AppendNotes(
      List<string> hitObjectLines,
      double sectionStart,
      double from,
      double limit,
      double endTime,
      double interval
    )
    {
      long firstIndex = (long)Math.Ceiling((from - sectionStart) / interval - TimeTolerance);

      for (long index = firstIndex; ; index++)
      {
        double time = sectionStart + index * interval;
        if (time >= limit - TimeTolerance || time > endTime + TimeTolerance)
          return;

        if (hitObjectLines.Count >= MaxNoteCount)
          throw new InvalidDataException(
            $"Too many notes would be generated (more than {MaxNoteCount})."
          );

        hitObjectLines.Add(BuildHitObjectLine(time, (int)((index - firstIndex) % NotePatternLength)));
      }
    }

    private static string BuildHitObjectLine(double time, int patternIndex)
    {
      int hitSound = patternIndex < NotePatternLength / 2 ? TaikoHitSound.Don : TaikoHitSound.Kat;
      long roundedTime = (long)Math.Round(time);
      return string.Create(
        CultureInfo.InvariantCulture,
        $"{PlayfieldCentreX},{PlayfieldCentreY},{roundedTime},{CircleType},{hitSound},0:0:0:0:"
      );
    }
  }
}
