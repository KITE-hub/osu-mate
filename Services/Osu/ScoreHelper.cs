using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Catch.Objects;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Taiko.Objects;

namespace OsuMate.Services.Osu;

internal static class ScoreHelper
{
  public static int CountTotalHitObjects(IBeatmap beatmap, int mode)
  {
    return mode switch
    {
      0 => beatmap.HitObjects.Count,
      1 => beatmap.HitObjects.OfType<Hit>().Count(),
      2 => beatmap.HitObjects.Count(h => h is Fruit)
        + beatmap
          .HitObjects.OfType<JuiceStream>()
          .SelectMany(j => j.NestedHitObjects)
          .Count(h => h is not TinyDroplet),
      3 => beatmap.HitObjects.Count,
      _ => throw new ArgumentException("Invalid ruleset ID provided."),
    };
  }

  public static double GetAccuracy(
    int count300,
    int count100,
    int count50,
    int countGeki,
    int countKatu,
    int countMiss,
    int mode
  )
  {
    var statistics = new Dictionary<HitResult, int>
    {
      { HitResult.Perfect, countGeki },
      { HitResult.Great, count300 },
      { HitResult.Good, countKatu },
      { HitResult.Ok, count100 },
      { HitResult.Meh, count50 },
      { HitResult.Miss, countMiss },
      { HitResult.LargeTickHit, count100 },
      { HitResult.SmallTickHit, count50 },
      { HitResult.SmallTickMiss, countKatu },
    };
    return GetAccuracy(statistics, mode);
  }

  public static double GetAccuracy(IReadOnlyDictionary<HitResult, int> statistics, int mode)
  {
    switch (mode)
    {
      case 0:
      {
        var countGreat = statistics[HitResult.Great];
        var countGood = statistics[HitResult.Ok];
        var countMeh = statistics[HitResult.Meh];
        var countMiss = statistics[HitResult.Miss];
        var total = countGreat + countGood + countMeh + countMiss;

        return (double)((6 * countGreat) + (2 * countGood) + countMeh) / (6 * total);
      }

      case 1:
      {
        var countGreat = statistics[HitResult.Great];
        var countGood = statistics[HitResult.Ok];
        var countMiss = statistics[HitResult.Miss];
        var total = countGreat + countGood + countMiss;

        return (double)((2 * countGreat) + countGood) / (2 * total);
      }

      case 2:
      {
        double hits =
          statistics[HitResult.Great]
          + statistics[HitResult.LargeTickHit]
          + statistics[HitResult.SmallTickHit];
        double total = hits + statistics[HitResult.Miss] + statistics[HitResult.SmallTickMiss];

        return hits / total;
      }

      case 3:
      {
        double hits =
          (6 * statistics[HitResult.Perfect])
          + (6 * statistics[HitResult.Great])
          + (4 * statistics[HitResult.Good])
          + (2 * statistics[HitResult.Ok])
          + statistics[HitResult.Meh];
        double total =
          6
          * (
            statistics[HitResult.Meh]
            + statistics[HitResult.Ok]
            + statistics[HitResult.Great]
            + statistics[HitResult.Miss]
            + statistics[HitResult.Perfect]
            + statistics[HitResult.Good]
          );

        return hits / total;
      }

      default:
        throw new ArgumentException("Invalid mode provided. Given mode: " + mode);
    }
  }

  public static int GetMaxCombo(IBeatmap beatmap, int mode)
  {
    return mode switch
    {
      0 => beatmap.GetMaxCombo(),
      1 => beatmap.HitObjects.OfType<Hit>().Count(),
      2 => beatmap.HitObjects.Count(h => h is Fruit)
        + beatmap
          .HitObjects.OfType<JuiceStream>()
          .SelectMany(j => j.NestedHitObjects)
          .Count(h => h is not TinyDroplet),
      3 => beatmap.HitObjects.Count,
      _ => throw new ArgumentException("Invalid ruleset ID provided."),
    };
  }
}
