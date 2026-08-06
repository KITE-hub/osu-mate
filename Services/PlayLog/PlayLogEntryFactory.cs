using OsuMate.Models;
using OsuMate.Services.Osu;

namespace OsuMate.Services.PlayLog
{
    public static class PlayLogEntryFactory
    {
        public static PlayLogEntry FromScoresDbScore(
            OsuMate.Services.StableDb.ScoreRecord score,
            OsuMate.Services.StableDb.BeatmapInfo beatmap)
        {
            var key = PlayLogKeyBuilder.MakeCompletedJoinKey(
                score.Md5Hash, score.PlayerName, score.Mods, score.TotalScore);

            return new PlayLogEntry
            {
                PlayedAt        = PlayLogKeyBuilder.FileTimeToLocal(score.TimestampTicks),
                DedupeKey       = key,
                BeatmapId       = beatmap.DifficultyId,
                BeatmapSetId    = beatmap.BeatmapSetId,
                Artist          = beatmap.Artist,
                Title           = beatmap.Title,
                DifficultyName  = beatmap.DifficultyName,
                Creator         = beatmap.Creator,
                BeatmapMd5      = score.Md5Hash,
                PlayerName      = score.PlayerName,
                // scores.db の mode は「このスコアが実際に記録されたルールセット」なので
                // osu!.db の譜面 mode よりも優先する。譜面側は mania のキー数取得に使う。
                Mode            = score.Mode,
                ManiaKeyCount   = LogModeClassifier.GetManiaKeyCount(score.Mode, beatmap.CircleSize),
                Count300        = score.Count300,
                Count100        = score.Count100,
                Count50         = score.Count50,
                CountGeki       = score.CountGeki,
                CountKatu       = score.CountKatu,
                CountMiss       = score.CountMiss,
                MaxCombo        = score.MaxCombo,
                TotalScore      = score.TotalScore,
                Accuracy        = ScoreHelper.GetAccuracy(
                                    score.Count300, score.Count100, score.Count50,
                                    score.CountGeki, score.CountKatu, score.CountMiss,
                                    score.Mode) * 100,
                ModsRaw         = score.Mods,
                OverallDifficulty = beatmap.OverallDifficulty,
                ModsString      = PlayLogKeyBuilder.FormatModsString(score.Mods),
                IsCompleted     = true,
                IsProvisional   = false,
                StarRating      = null,
                Pp              = null,
            };
        }
    }
}
