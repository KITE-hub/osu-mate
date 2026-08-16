namespace OsuMate.Models
{
  public sealed class UpdateIntervalOption
  {
    public string Label { get; }

    public int IntervalMs { get; }

    public UpdateIntervalOption(string label, int intervalMs)
    {
      Label = label;
      IntervalMs = intervalMs;
    }
  }
}
