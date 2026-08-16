using OsuMate.Models;
using OsuMate.Services.Osu;

namespace OsuMate.Services.PlayLog
{
  public static class PlayLogEntryFactory
  {
    public static PlayLogEntry FromScoresDbScore(
      OsuMate.Services.StableDb.ScoreRecord score,
      OsuMate.Services.StableDb.BeatmapInfo beatmap
    )
    {
      var key = PlayLogKeyBuilder.MakeCompletedKey(
        score.Md5Hash,
        score.PlayerName,
        score.Mode,
        score.Mods,
        score.TotalScore,
        score.OnlineScoreId,
        score.ReplayMd5
      );

      return new PlayLogEntry
      {
        PlayedAt = PlayLogKeyBuilder.FileTimeToLocal(score.TimestampTicks),
        DedupeKey = key,
        OnlineScoreId = score.OnlineScoreId == 0 ? null : score.OnlineScoreId,
        ReplayMd5 = string.IsNullOrWhiteSpace(score.ReplayMd5) ? null : score.ReplayMd5,
        BeatmapId = beatmap.DifficultyId,
        BeatmapSetId = beatmap.BeatmapSetId,
        Artist = beatmap.Artist,
        Title = beatmap.Title,
        DifficultyName = beatmap.DifficultyName,
        Creator = beatmap.Creator,
        BeatmapMd5 = score.Md5Hash,
        PlayerName = score.PlayerName,

        Mode = score.Mode,
        ManiaKeyCount = LogModeClassifier.GetManiaKeyCount(score.Mode, beatmap.CircleSize),
        Count300 = score.Count300,
        Count100 = score.Count100,
        Count50 = score.Count50,
        CountGeki = score.CountGeki,
        CountKatu = score.CountKatu,
        CountMiss = score.CountMiss,
        MaxCombo = score.MaxCombo,
        TotalScore = score.TotalScore,
        Accuracy =
          ScoreHelper.GetAccuracy(
            score.Count300,
            score.Count100,
            score.Count50,
            score.CountGeki,
            score.CountKatu,
            score.CountMiss,
            score.Mode
          ) * 100,
        ModsRaw = score.Mods,
        OverallDifficulty = beatmap.OverallDifficulty,
        ModsString = PlayLogKeyBuilder.FormatModsString(score.Mods),
        IsCompleted = true,
        IsProvisional = false,
        StarRating = null,
        Pp = null,
      };
    }
  }
}
