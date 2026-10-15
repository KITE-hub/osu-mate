using System.Globalization;

namespace OsuMate.Services.Trainer
{
  internal readonly record struct UninheritedTimingPoint(
    double Time,
    double BeatLength,
    int Meter,
    string SampleSet,
    string SampleIndex
  );

  internal static class TimingPointParser
  {
    private const int DefaultMeter = 4;

    internal static bool TryParseUninherited(string line, out UninheritedTimingPoint point)
    {
      point = default;

      var parts = line.Split(',');
      if (parts.Length < 7)
        return false;
      if (
        !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double time)
      )
        return false;
      if (
        !double.TryParse(
          parts[1],
          NumberStyles.Float,
          CultureInfo.InvariantCulture,
          out double beatLength
        )
      )
        return false;
      if (!int.TryParse(parts[6].Trim(), out int uninherited))
        return false;
      if (uninherited != 1 || !(beatLength > 0))
        return false;

      int meter =
        int.TryParse(parts[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedMeter)
        && parsedMeter > 0
          ? parsedMeter
          : DefaultMeter;

      point = new UninheritedTimingPoint(
        time,
        beatLength,
        meter,
        parts[3].Trim(),
        parts[4].Trim()
      );
      return true;
    }
  }
}
