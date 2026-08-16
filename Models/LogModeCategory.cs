namespace OsuMate.Models
{
  public enum LogModeCategory
  {
    Standard,
    Taiko,
    Catch,
    Mania4K,
    Mania7K,
    ManiaOther,
  }

  public static class LogModeClassifier
  {
    public static int? GetManiaKeyCount(int mode, double circleSize)
    {
      if (mode != 3 || circleSize <= 0)
        return null;
      return (int)System.Math.Round(circleSize, System.MidpointRounding.AwayFromZero);
    }

    public static LogModeCategory Classify(int mode, int? maniaKeyCount)
    {
      return mode switch
      {
        0 => LogModeCategory.Standard,
        1 => LogModeCategory.Taiko,
        2 => LogModeCategory.Catch,
        3 when maniaKeyCount == 4 => LogModeCategory.Mania4K,
        3 when maniaKeyCount == 7 => LogModeCategory.Mania7K,
        _ => LogModeCategory.ManiaOther,
      };
    }
  }
}
