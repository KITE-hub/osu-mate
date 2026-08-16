namespace OsuMate.Models
{
  public sealed class DailyPlayStats
  {
    public DateOnly Date { get; }

    public MetricStat StarRating { get; }

    public MetricStat Pp { get; }

    public MetricStat Accuracy { get; }

    public DailyPlayStats(DateOnly date, MetricStat starRating, MetricStat pp, MetricStat accuracy)
    {
      Date = date;
      StarRating = starRating;
      Pp = pp;
      Accuracy = accuracy;
    }
  }
}
