using System.Collections.ObjectModel;
using osu.Game.Rulesets.Scoring;
using OsuMate.Models;
using OsuMate.Services.Osu;
using OsuMate.Utils;

namespace OsuMate.ViewModels
{
  public class InGameOverlayViewModel : ObservableBase
  {
    private double _fontSize = 13;
    public double FontSize
    {
      get => _fontSize;
      set
      {
        _fontSize = value;
        OnPropertyChanged();
      }
    }

    private bool _isShowValueFirst;
    public bool IsShowValueFirst
    {
      get => _isShowValueFirst;
      set
      {
        _isShowValueFirst = value;
        OnPropertyChanged();
      }
    }

    private ObservableCollection<OverlayLineItem> _lines = [];
    public ObservableCollection<OverlayLineItem> Lines
    {
      get => _lines;
      private set
      {
        _lines = value;
        OnPropertyChanged();
      }
    }

    internal void Update(
      BeatmapData? data,
      HitsResult hits,
      double accuracy,
      int gamemode,
      List<int> enabledIds,
      double audioTime,
      double speedMultiplier,
      double? bestPp = null
    )
    {
      if (data == null)
        return;

      var newLines = enabledIds
        .Select(id =>
          GetLine(id, data, hits, accuracy, gamemode, audioTime, speedMultiplier, bestPp)
        )
        .Where(l => l != null)
        .ToList();

      bool sameLayout =
        Lines.Count == newLines.Count && Lines.Zip(newLines, (a, b) => a.Id == b!.Id).All(x => x);

      if (sameLayout)
      {
        for (int i = 0; i < Lines.Count; i++)
        {
          Lines[i].Label = newLines[i]!.Label;
          Lines[i].Value = newLines[i]!.Value;
        }
      }
      else
      {
        Lines = new ObservableCollection<OverlayLineItem>(newLines!);
      }
    }

    internal void UpdateFast(
      HitsResult hits,
      double accuracy,
      double? modifiedAvg,
      double? modifiedStdev,
      double? rawUR,
      double? modifiedUR,
      int gamemode
    )
    {
      foreach (var line in Lines)
      {
        switch (line.Id)
        {
          case OverlayLineIds.Accuracy:
            line.Value = FormatAccuracy(accuracy);
            break;
          case OverlayLineIds.Hits:
            line.Value = OsuUtils.ConvertHits(gamemode, hits);
            break;
          case OverlayLineIds.AverageOffset:
            if (modifiedAvg.HasValue)
              line.Value = FormatAverageOffset(modifiedAvg.Value, modifiedStdev!.Value);
            break;
          case OverlayLineIds.UnstableRate:
            if (rawUR.HasValue)
              line.Value = FormatUnstableRate(rawUR.Value, modifiedUR!.Value);
            break;
        }
      }
    }

    private static string FormatAccuracy(double accuracy) => $"{accuracy:F2}%";

    private static string FormatAverageOffset(double average, double stdev) =>
      $"{MathUtils.FormatUnder4CharsSign(average)} ± {MathUtils.FormatUnder4Chars(stdev)}";

    private static string FormatUnstableRate(double raw, double modified) =>
      $"{MathUtils.IsNaNWithNum(raw):F2} ({MathUtils.IsNaNWithNum(modified):F2})";

    private static OverlayLineItem? GetLine(
      int id,
      BeatmapData data,
      HitsResult hits,
      double accuracy,
      int gamemode,
      double audioTime,
      double speedMultiplier,
      double? bestPp = null
    )
    {
      switch (id)
      {
        case OverlayLineIds.StarRating:
          var sr = MathUtils.IsNaNWithNum(
            Math.Round(data.DifficultyAttributes?.StarRating ?? 0, 2)
          );
          return new()
          {
            Id = id,
            Label = "SR",
            Value = $"{sr}",
          };

        case OverlayLineIds.SsPp:
          var sspp = Math.Round(MathUtils.IsNaNWithNum(data.PerformanceAttributes?.Total));
          return new()
          {
            Id = id,
            Label = "SS pp",
            Value = $"{sspp}pp",
          };

        case OverlayLineIds.LossPp:
          var losspp = Math.Round(
            MathUtils.IsNaNWithNum(data.PerformanceAttributesLossMode?.Total)
          );
          return new()
          {
            Id = id,
            Label = "Loss pp",
            Value = $"{losspp}pp",
          };

        case OverlayLineIds.PredictedPp:
          var predpp = Math.Round(
            MathUtils.IsNaNWithNum(data.PerformanceAttributesPredicted?.Total)
          );
          return new()
          {
            Id = id,
            Label = "Pred pp",
            Value = $"{predpp}pp",
          };

        case OverlayLineIds.CurrentPp:
          var curpp = Math.Round(MathUtils.IsNaNWithNum(data.CurrentPerformanceAttributes?.Total));
          return new()
          {
            Id = id,
            Label = "pp",
            Value = $"{curpp}pp",
          };

        case OverlayLineIds.Accuracy:
          return new()
          {
            Id = id,
            Label = "ACC",
            Value = FormatAccuracy(accuracy),
          };

        case OverlayLineIds.Hits:
          return new()
          {
            Id = id,
            Label = "Hits",
            Value = OsuUtils.ConvertHits(gamemode, hits),
          };

        case OverlayLineIds.AverageOffset:
          return new()
          {
            Id = id,
            Label = "Avg offset",
            Value = FormatAverageOffset(
              data.DetailedOffset.modifiedAvg,
              data.DetailedOffset.modifiedStdev
            ),
          };

        case OverlayLineIds.UniversalOffsetHelp:
          var robustAvgToUniversalOffset = MathUtils.FormatNaturalSign(
            -1 * data.DetailedOffset.robustAvg
          );
          return new()
          {
            Id = id,
            Label = "Universal offset help",
            Value = $"{robustAvgToUniversalOffset} ms",
          };

        case OverlayLineIds.LocalOffsetHelp:
          var robustAvgToLocalOffset = MathUtils.FormatNaturalSign(data.DetailedOffset.robustAvg);
          return new()
          {
            Id = id,
            Label = "Local offset help",
            Value = $"{robustAvgToLocalOffset} ms",
          };

        case OverlayLineIds.UnstableRate:
          return new()
          {
            Id = id,
            Label = "UR",
            Value = FormatUnstableRate(data.UR.rawUR, data.UR.modifiedUR),
          };

        case OverlayLineIds.Progress:
          var child = audioTime * speedMultiplier - data.FirstObjectTimeModified;
          var mother = data.StrainTimeModified - data.FirstObjectTimeModified;
          if (mother == 0)
            return new()
            {
              Id = id,
              Label = "Progress",
              Value = "0 %",
            };
          var progress = Math.Clamp(child * 100 / mother, 0, 100);
          return new()
          {
            Id = id,
            Label = "Progress",
            Value = $"{progress:F1} %",
          };

        case OverlayLineIds.RemainingNotes:
          var judgedSoFar =
            hits.HitGeki + hits.Hit300 + hits.HitKatu + hits.Hit100 + hits.Hit50 + hits.HitMiss;
          var remainingNotes = Math.Max(0, data.TotalHitObjectCount - judgedSoFar);
          return new()
          {
            Id = id,
            Label = "Remaining",
            Value = $"{remainingNotes}",
          };

        case OverlayLineIds.Bpm:
          var bpm = MathUtils.IsNaNWithNum(data.Bpm.CurrentBpm).ToString("F1");
          var minBpm = MathUtils.IsNaNWithNum(data.Bpm.MinimumBpm).ToString("F0");
          var maxBpm = MathUtils.IsNaNWithNum(data.Bpm.MaximumBpm).ToString("F0");
          return new()
          {
            Id = id,
            Label = "BPM",
            Value = $"{bpm} ({minBpm}-{maxBpm})",
          };

        case OverlayLineIds.BestPp:
          var bestPpValue = bestPp.HasValue ? $"{Math.Round(bestPp.Value)}pp" : "-";
          return new()
          {
            Id = id,
            Label = "Best pp",
            Value = bestPpValue,
          };

        default:
          return null;
      }
    }
  }
}
