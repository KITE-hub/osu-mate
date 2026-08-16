namespace OsuMate.Services.Osu
{
  internal static class TargetPlayerFilter
  {
    public static bool Matches(string? playerName, IEnumerable<string> targetPlayerNames)
    {
      if (string.IsNullOrEmpty(playerName))
        return true;
      return targetPlayerNames.Contains(playerName, StringComparer.OrdinalIgnoreCase);
    }
  }
}
