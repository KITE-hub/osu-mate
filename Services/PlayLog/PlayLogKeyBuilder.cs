using System;
using System.Linq;
using OsuMate.Models;
using OsuMate.Services.Osu;

namespace OsuMate.Services.PlayLog
{
  public static class PlayLogKeyBuilder
  {
    public static DateTime FileTimeToLocal(long ticks)
    {
      return new DateTime(ticks, DateTimeKind.Utc).ToLocalTime();
    }

    public static string MakeCompletedKey(
      string beatmapMd5,
      string playerName,
      int mode,
      int modsRaw,
      int totalScore,
      long onlineScoreId,
      string? replayMd5
    )
    {
      if (playerName == "Guest")
        playerName = "";
      if (onlineScoreId != 0)
        return $"online|{onlineScoreId}";
      if (!string.IsNullOrWhiteSpace(replayMd5))
        return $"replay|{replayMd5}";
      return $"memc|{beatmapMd5}|{playerName}|{mode}|{modsRaw}|{totalScore}";
    }

    public static string MakeInterruptedKey(PlayLogEntry e)
    {
      var playerName = e.PlayerName == "Guest" ? "" : e.PlayerName;
      return $"mem|{e.BeatmapMd5}|{playerName}|{e.PlayedAt:yyyyMMddHHmmssfff}";
    }

    public static string FormatModsString(int modsRaw)
    {
      var calc = OsuUtils.ParseMods(modsRaw).Calculation;
      return calc.Length == 0 ? "NM" : string.Join(",", calc.Select(m => m.ToUpper()));
    }
  }
}
