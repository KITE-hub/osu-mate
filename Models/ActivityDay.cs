namespace OsuMate.Models
{
  public sealed class ActivityDay
  {
    public DateOnly Date { get; }

    public int TotalHits { get; }

    public int Level { get; }

    public bool IsToday { get; }

    public bool IsPlaceholder { get; }

    public MetricStat StarRating { get; }

    public MetricStat Pp { get; }

    public MetricStat Accuracy { get; }

    public string TooltipText =>
      $"Date: {Date:yyyy/MM/dd}\n"
      + $"Valid Hits: {TotalHits}\n"
      + $"SR: {FormatMetric(StarRating)}\n"
      + $"pp: {FormatMetric(Pp)}\n"
      + $"Acc: {FormatMetric(Accuracy, "%")}";

    public ActivityDay(
      DateOnly date,
      int totalHits,
      int level,
      bool isToday,
      MetricStat starRating,
      MetricStat pp,
      MetricStat accuracy
    )
    {
      Date = date;
      TotalHits = totalHits;
      Level = level;
      IsToday = isToday;
      IsPlaceholder = false;
      StarRating = starRating;
      Pp = pp;
      Accuracy = accuracy;
    }

    public static ActivityDay Placeholder { get; } = new ActivityDay();

    private ActivityDay()
    {
      IsPlaceholder = true;
      StarRating = MetricStat.Empty;
      Pp = MetricStat.Empty;
      Accuracy = MetricStat.Empty;
    }

    private static string FormatMetric(MetricStat stat, string suffix = "") =>
      stat.SampleCount == 0 ? "-" : $"{stat.Mean:F2}±{stat.StdDev:F2}{suffix}";
  }
}
