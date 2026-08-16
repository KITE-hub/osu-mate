using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Scoring;

namespace OsuMate.Models;

internal class BeatmapData
{
  internal DifficultyAttributes? CurrentDifficultyAttributes { get; set; }
  internal PerformanceAttributes? CurrentPerformanceAttributes { get; set; }
  internal DifficultyAttributes? DifficultyAttributes { get; set; }
  internal PerformanceAttributes? PerformanceAttributes { get; set; }
  internal DifficultyAttributes? DifficultyAttributesIffc { get; set; }
  internal PerformanceAttributes? PerformanceAttributesIffc { get; set; }
  internal PerformanceAttributes? PerformanceAttributesPredicted { get; set; }
  internal PerformanceAttributes? PerformanceAttributesLossMode { get; set; }
  internal Dictionary<HitResult, int> HitResults { get; set; } = [];
  internal Dictionary<HitResult, int> HitResultPredicted { get; set; } = [];
  internal Dictionary<HitResult, int> IfFcHitResult { get; set; } = [];
  internal Dictionary<HitResult, int> HitResultLossMode { get; set; } = [];
  internal int FirstObjectTimeModified { get; set; }
  internal int LastObjectTimeModified { get; set; }
  internal int StrainTimeModified { get; set; }
  internal (double CurrentBpm, double MinimumBpm, double MaximumBpm) Bpm { get; set; }
  internal int TotalHitObjectCount { get; set; }
  internal double OverallDifficulty { get; set; }
  internal (
    double rawAvg,
    double modifiedAvg,
    double modifiedStdev,
    double robustAvg
  ) DetailedOffset { get; set; }
  internal (double rawUR, double modifiedUR) UR { get; set; }
  internal Dictionary<HitResult, double> ModifiedHitWindows { get; set; } = [];
}
