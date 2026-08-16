namespace OsuMate.Models
{
  public sealed class MetricStat
  {
    public double Mean { get; }

    public double StdDev { get; }

    public int SampleCount { get; }

    public MetricStat(double mean, double stdDev, int sampleCount)
    {
      Mean = mean;
      StdDev = stdDev;
      SampleCount = sampleCount;
    }

    public static MetricStat Empty { get; } = new MetricStat(0, 0, 0);
  }
}
