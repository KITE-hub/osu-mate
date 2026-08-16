using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Catch.Objects;
using osu.Game.Rulesets.Scoring;
using OsuMate.Models;

namespace OsuMate.Services.Osu;

internal static class HitResultHelper
{
  public static Dictionary<HitResult, int> GenerateHitResultsForSs(IBeatmap beatmap, int mode)
  {
    switch (mode)
    {
      case 0:
      {
        return new Dictionary<HitResult, int>
        {
          { HitResult.Great, beatmap.HitObjects.Count },
          { HitResult.Ok, 0 },
          { HitResult.Meh, 0 },
          { HitResult.Miss, 0 },
        };
      }
      case 1:
      {
        int countGreat = ScoreHelper.GetMaxCombo(beatmap, mode);
        return new Dictionary<HitResult, int>
        {
          { HitResult.Great, countGreat },
          { HitResult.Ok, 0 },
          { HitResult.Miss, 0 },
        };
      }
      case 2:
      {
        int maxCombo = ScoreHelper.GetMaxCombo(beatmap, mode);
        var juiceStreams = beatmap.HitObjects.OfType<JuiceStream>().ToList();
        int maxTinyDroplets = juiceStreams.Sum(s =>
          s.NestedHitObjects.OfType<TinyDroplet>().Count()
        );
        int maxDroplets =
          juiceStreams.Sum(s => s.NestedHitObjects.OfType<Droplet>().Count()) - maxTinyDroplets;
        int maxFruits = beatmap.HitObjects.Sum(h =>
          h is Fruit ? 1 : (h as JuiceStream)?.NestedHitObjects.Count(n => n is Fruit) ?? 0
        );
        int countDroplets = Math.Max(0, maxDroplets);
        int countFruits = maxFruits + (maxDroplets - countDroplets);
        int countTinyDroplets = maxCombo + maxTinyDroplets - countFruits - countDroplets;
        int countTinyMisses = maxTinyDroplets - countTinyDroplets;
        return new Dictionary<HitResult, int>
        {
          { HitResult.Great, countFruits },
          { HitResult.LargeTickHit, countDroplets },
          { HitResult.SmallTickHit, countTinyDroplets },
          { HitResult.SmallTickMiss, countTinyMisses },
          { HitResult.Miss, 0 },
        };
      }
      case 3:
      {
        return new Dictionary<HitResult, int>
        {
          [HitResult.Perfect] = beatmap.HitObjects.Count,
          [HitResult.Great] = 0,
          [HitResult.Good] = 0,
          [HitResult.Ok] = 0,
          [HitResult.Meh] = 0,
          [HitResult.Miss] = 0,
        };
      }
      default:
        throw new ArgumentException("Invalid mode provided. Given mode: " + mode);
    }
  }

  public static Dictionary<HitResult, int> GenerateHitResultsForLossMode(
    Dictionary<HitResult, int> statics,
    HitsResult hits,
    int mode
  )
  {
    return mode switch
    {
      0 => new Dictionary<HitResult, int>
      {
        { HitResult.Great, statics[HitResult.Great] - hits.Hit100 - hits.Hit50 - hits.HitMiss },
        { HitResult.Ok, hits.Hit100 },
        { HitResult.Meh, hits.Hit50 },
        { HitResult.Miss, hits.HitMiss },
      },
      1 => new Dictionary<HitResult, int>
      {
        { HitResult.Great, statics[HitResult.Great] - hits.Hit100 - hits.HitMiss },
        { HitResult.Ok, hits.Hit100 },
        { HitResult.Miss, hits.HitMiss },
      },
      2 => new Dictionary<HitResult, int>
      {
        { HitResult.Great, statics[HitResult.Great] - hits.HitMiss },
        { HitResult.LargeTickHit, hits.Hit100 },
        { HitResult.SmallTickHit, hits.Hit50 },
        { HitResult.SmallTickMiss, hits.HitKatu },
        { HitResult.Miss, hits.HitMiss },
      },
      3 => new Dictionary<HitResult, int>
      {
        [HitResult.Perfect] =
          statics[HitResult.Perfect]
          - hits.Hit300
          - hits.HitKatu
          - hits.Hit100
          - hits.Hit50
          - hits.HitMiss,
        [HitResult.Great] = hits.Hit300,
        [HitResult.Good] = hits.HitKatu,
        [HitResult.Ok] = hits.Hit100,
        [HitResult.Meh] = hits.Hit50,
        [HitResult.Miss] = hits.HitMiss,
      },
      _ => throw new ArgumentException("Invalid mode provided. Given mode: " + mode),
    };
  }

  public static Dictionary<HitResult, int> GenerateHitResultsForPredicted(
    IBeatmap beatmap,
    HitsResult hits,
    int mode
  )
  {
    switch (mode)
    {
      case 0:
      {
        int currentTotalHits = hits.Hit300 + hits.Hit100 + hits.Hit50 + hits.HitMiss;
        return new Dictionary<HitResult, int>
        {
          {
            HitResult.Great,
            currentTotalHits == 0 ? 0 : hits.Hit300 * beatmap.HitObjects.Count / currentTotalHits
          },
          {
            HitResult.Ok,
            currentTotalHits == 0 ? 0 : hits.Hit100 * beatmap.HitObjects.Count / currentTotalHits
          },
          {
            HitResult.Meh,
            currentTotalHits == 0 ? 0 : hits.Hit50 * beatmap.HitObjects.Count / currentTotalHits
          },
          {
            HitResult.Miss,
            currentTotalHits == 0 ? 0 : hits.HitMiss * beatmap.HitObjects.Count / currentTotalHits
          },
        };
      }
      case 1:
      {
        int currentTotalHits = hits.Hit300 + hits.Hit100 + hits.HitMiss;
        int totalHits = ScoreHelper.GetMaxCombo(beatmap, mode);
        return new Dictionary<HitResult, int>
        {
          {
            HitResult.Great,
            currentTotalHits == 0 ? 0 : hits.Hit300 * totalHits / currentTotalHits
          },
          { HitResult.Ok, currentTotalHits == 0 ? 0 : hits.Hit100 * totalHits / currentTotalHits },
          {
            HitResult.Miss,
            currentTotalHits == 0 ? 0 : hits.HitMiss * totalHits / currentTotalHits
          },
        };
      }
      case 2:
      {
        int maxCombo = ScoreHelper.GetMaxCombo(beatmap, mode);
        var juiceStreams = beatmap.HitObjects.OfType<JuiceStream>().ToList();
        int maxTinyDroplets = juiceStreams.Sum(s =>
          s.NestedHitObjects.OfType<TinyDroplet>().Count()
        );
        int totalObjects = maxCombo + maxTinyDroplets;
        int currentTotalHits = hits.Hit300 + hits.Hit100 + hits.Hit50 + hits.HitKatu + hits.HitMiss;
        return new Dictionary<HitResult, int>
        {
          {
            HitResult.Great,
            currentTotalHits == 0 ? 0 : hits.Hit300 * totalObjects / currentTotalHits
          },
          {
            HitResult.LargeTickHit,
            currentTotalHits == 0 ? 0 : hits.Hit100 * totalObjects / currentTotalHits
          },
          {
            HitResult.SmallTickHit,
            currentTotalHits == 0 ? 0 : hits.Hit50 * totalObjects / currentTotalHits
          },
          {
            HitResult.SmallTickMiss,
            currentTotalHits == 0 ? 0 : hits.HitKatu * totalObjects / currentTotalHits
          },
          {
            HitResult.Miss,
            currentTotalHits == 0 ? 0 : hits.HitMiss * totalObjects / currentTotalHits
          },
        };
      }
      case 3:
      {
        int currentTotalHits =
          hits.HitGeki + hits.Hit300 + hits.HitKatu + hits.Hit100 + hits.Hit50 + hits.HitMiss;
        return new Dictionary<HitResult, int>
        {
          [HitResult.Perfect] =
            currentTotalHits == 0 ? 0 : hits.HitGeki * beatmap.HitObjects.Count / currentTotalHits,
          [HitResult.Great] =
            currentTotalHits == 0 ? 0 : hits.Hit300 * beatmap.HitObjects.Count / currentTotalHits,
          [HitResult.Good] =
            currentTotalHits == 0 ? 0 : hits.HitKatu * beatmap.HitObjects.Count / currentTotalHits,
          [HitResult.Ok] =
            currentTotalHits == 0 ? 0 : hits.Hit100 * beatmap.HitObjects.Count / currentTotalHits,
          [HitResult.Meh] =
            currentTotalHits == 0 ? 0 : hits.Hit50 * beatmap.HitObjects.Count / currentTotalHits,
          [HitResult.Miss] =
            currentTotalHits == 0 ? 0 : hits.HitMiss * beatmap.HitObjects.Count / currentTotalHits,
        };
      }
      default:
        throw new ArgumentException("Invalid mode provided. Given mode: " + mode);
    }
  }

  public static Dictionary<HitResult, int> GenerateHitResultsForCurrent(HitsResult hits, int mode)
  {
    return mode switch
    {
      0 => new Dictionary<HitResult, int>
      {
        { HitResult.Great, hits.Hit300 },
        { HitResult.Ok, hits.Hit100 },
        { HitResult.Meh, hits.Hit50 },
        { HitResult.Miss, hits.HitMiss },
      },
      1 => new Dictionary<HitResult, int>
      {
        { HitResult.Great, hits.Hit300 },
        { HitResult.Ok, hits.Hit100 },
        { HitResult.Miss, hits.HitMiss },
      },
      2 => new Dictionary<HitResult, int>
      {
        { HitResult.Great, hits.Hit300 },
        { HitResult.LargeTickHit, hits.Hit100 },
        { HitResult.SmallTickHit, hits.Hit50 },
        { HitResult.SmallTickMiss, hits.HitKatu },
        { HitResult.Miss, hits.HitMiss },
      },
      3 => new Dictionary<HitResult, int>
      {
        { HitResult.Perfect, hits.HitGeki },
        { HitResult.Great, hits.Hit300 },
        { HitResult.Good, hits.HitKatu },
        { HitResult.Ok, hits.Hit100 },
        { HitResult.Meh, hits.Hit50 },
        { HitResult.Miss, hits.HitMiss },
      },
      _ => throw new ArgumentException("Invalid mode provided. Given mode: " + mode),
    };
  }
}
