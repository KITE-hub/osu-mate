using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Catch.Objects;
using osu.Game.Rulesets.Scoring;
using OsuMate.Models;

namespace OsuMate.Services.Osu
{

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
          var (maxTinyDroplets, allDroplets, maxFruits) = CountCatchObjects(beatmap);
          int maxDroplets = allDroplets - maxTinyDroplets;
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

    private static (int TinyDroplets, int Droplets, int Fruits) CountCatchObjects(IBeatmap beatmap)
    {
      int tinyDroplets = 0;
      int droplets = 0;
      int fruits = 0;

      foreach (var hitObject in beatmap.HitObjects)
      {
        if (hitObject is Fruit)
        {
          fruits++;
          continue;
        }

        if (hitObject is not JuiceStream juiceStream)
          continue;

        foreach (var nested in juiceStream.NestedHitObjects)
        {
          if (nested is TinyDroplet)
            tinyDroplets++;
          if (nested is Droplet)
            droplets++;
          if (nested is Fruit)
            fruits++;
        }
      }

      return (tinyDroplets, droplets, fruits);
    }

    public static int GetPredictionTotal(IBeatmap beatmap, int mode)
    {
      switch (mode)
      {
        case 0:
        case 3:
          return beatmap.HitObjects.Count;
        case 1:
          return ScoreHelper.GetMaxCombo(beatmap, mode);
        case 2:
        {
          return ScoreHelper.GetMaxCombo(beatmap, mode) + CountCatchObjects(beatmap).TinyDroplets;
        }
        default:
          throw new ArgumentException("Invalid mode provided. Given mode: " + mode);
      }
    }

    private static int Scale(int count, int total, int currentTotalHits) =>
      currentTotalHits == 0 ? 0 : count * total / currentTotalHits;

    public static Dictionary<HitResult, int> GenerateHitResultsForPredicted(
      int predictionTotal,
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
            { HitResult.Great, Scale(hits.Hit300, predictionTotal, currentTotalHits) },
            { HitResult.Ok, Scale(hits.Hit100, predictionTotal, currentTotalHits) },
            { HitResult.Meh, Scale(hits.Hit50, predictionTotal, currentTotalHits) },
            { HitResult.Miss, Scale(hits.HitMiss, predictionTotal, currentTotalHits) },
          };
        }
        case 1:
        {
          int currentTotalHits = hits.Hit300 + hits.Hit100 + hits.HitMiss;
          return new Dictionary<HitResult, int>
          {
            { HitResult.Great, Scale(hits.Hit300, predictionTotal, currentTotalHits) },
            { HitResult.Ok, Scale(hits.Hit100, predictionTotal, currentTotalHits) },
            { HitResult.Miss, Scale(hits.HitMiss, predictionTotal, currentTotalHits) },
          };
        }
        case 2:
        {
          int currentTotalHits = hits.Hit300 + hits.Hit100 + hits.Hit50 + hits.HitKatu + hits.HitMiss;
          return new Dictionary<HitResult, int>
          {
            { HitResult.Great, Scale(hits.Hit300, predictionTotal, currentTotalHits) },
            { HitResult.LargeTickHit, Scale(hits.Hit100, predictionTotal, currentTotalHits) },
            { HitResult.SmallTickHit, Scale(hits.Hit50, predictionTotal, currentTotalHits) },
            { HitResult.SmallTickMiss, Scale(hits.HitKatu, predictionTotal, currentTotalHits) },
            { HitResult.Miss, Scale(hits.HitMiss, predictionTotal, currentTotalHits) },
          };
        }
        case 3:
        {
          int currentTotalHits =
            hits.HitGeki + hits.Hit300 + hits.HitKatu + hits.Hit100 + hits.Hit50 + hits.HitMiss;
          return new Dictionary<HitResult, int>
          {
            [HitResult.Perfect] = Scale(hits.HitGeki, predictionTotal, currentTotalHits),
            [HitResult.Great] = Scale(hits.Hit300, predictionTotal, currentTotalHits),
            [HitResult.Good] = Scale(hits.HitKatu, predictionTotal, currentTotalHits),
            [HitResult.Ok] = Scale(hits.Hit100, predictionTotal, currentTotalHits),
            [HitResult.Meh] = Scale(hits.Hit50, predictionTotal, currentTotalHits),
            [HitResult.Miss] = Scale(hits.HitMiss, predictionTotal, currentTotalHits),
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
}
