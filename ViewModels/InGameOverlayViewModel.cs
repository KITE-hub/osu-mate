using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using osu.Game.Rulesets.Scoring;
using OsuMate.Models;
using OsuMate.Services.Osu;
using OsuMate.Utils;

namespace OsuMate.ViewModels
{
  public class OverlayLineItem : ObservableBase
  {
    private string _label = "";
    private string _value = "";

    public int Id { get; init; }

    public string Label
    {
      get => _label;
      set
      {
        _label = value;
        OnPropertyChanged();
      }
    }

    public string Value
    {
      get => _value;
      set
      {
        _value = value;
        OnPropertyChanged();
      }
    }
  }

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

      if (Lines.Count == newLines.Count)
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
          case 6:
            line.Value = $"{accuracy:F2}%";
            break;
          case 7:
            line.Value = OsuUtils.ConvertHits(gamemode, hits);
            break;
          case 8:
            if (modifiedAvg.HasValue)
              line.Value =
                $"{MathUtils.FormatUnder4CharsSign(modifiedAvg.Value)} ± {MathUtils.FormatUnder4Chars(modifiedStdev!.Value)}";
            break;
          case 11:
            if (rawUR.HasValue)
              line.Value =
                $"{MathUtils.IsNaNWithNum(rawUR.Value):F2} ({MathUtils.IsNaNWithNum(modifiedUR!.Value):F2})";
            break;
        }
      }
    }

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
        case 1:
          var sr = MathUtils.IsNaNWithNum(
            Math.Round(data.DifficultyAttributes?.StarRating ?? 0, 2)
          );
          return new()
          {
            Id = id,
            Label = "SR",
            Value = $"{sr}",
          };

        case 2:
          var sspp = Math.Round(MathUtils.IsNaNWithNum(data.PerformanceAttributes?.Total));
          return new()
          {
            Id = id,
            Label = "SS pp",
            Value = $"{sspp}pp",
          };

        case 3:
          var losspp = Math.Round(
            MathUtils.IsNaNWithNum(data.PerformanceAttributesLossMode?.Total)
          );
          return new()
          {
            Id = id,
            Label = "Loss pp",
            Value = $"{losspp}pp",
          };

        case 4:
          var predpp = Math.Round(
            MathUtils.IsNaNWithNum(data.PerformanceAttributesPredicted?.Total)
          );
          return new()
          {
            Id = id,
            Label = "Pred pp",
            Value = $"{predpp}pp",
          };

        case 5:
          var curpp = Math.Round(MathUtils.IsNaNWithNum(data.CurrentPerformanceAttributes?.Total));
          return new()
          {
            Id = id,
            Label = "pp",
            Value = $"{curpp}pp",
          };

        case 6:
          var acc = accuracy.ToString("F2");
          return new()
          {
            Id = id,
            Label = "ACC",
            Value = $"{acc}%",
          };

        case 7:
          return new()
          {
            Id = id,
            Label = "Hits",
            Value = OsuUtils.ConvertHits(gamemode, hits),
          };

        case 8:
          var modifiedAvgOffset = MathUtils.FormatUnder4CharsSign(data.DetailedOffset.modifiedAvg);
          var modifiedStdev = MathUtils.FormatUnder4Chars(data.DetailedOffset.modifiedStdev);
          return new()
          {
            Id = id,
            Label = "Avg offset",
            Value = $"{modifiedAvgOffset} ± {modifiedStdev}",
          };

        case 9:
          var robustAvgToUniversalOffset = MathUtils.FormatNaturalSign(
            -1 * data.DetailedOffset.robustAvg
          );
          return new()
          {
            Id = id,
            Label = "Universal offset help",
            Value = $"{robustAvgToUniversalOffset} ms",
          };

        case 10:
          var robustAvgToLocalOffset = MathUtils.FormatNaturalSign(data.DetailedOffset.robustAvg);
          return new()
          {
            Id = id,
            Label = "Local offset help",
            Value = $"{robustAvgToLocalOffset} ms",
          };

        case 11:
          var rawUR = MathUtils.IsNaNWithNum(data.UR.rawUR).ToString("F2");
          var modifiedUR = MathUtils.IsNaNWithNum(data.UR.modifiedUR).ToString("F2");
          return new()
          {
            Id = id,
            Label = "UR",
            Value = $"{rawUR} ({modifiedUR})",
          };

        case 12:
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

        case 13:
          var judgedSoFar =
            hits.HitGeki + hits.Hit300 + hits.HitKatu + hits.Hit100 + hits.Hit50 + hits.HitMiss;
          var remainingNotes = Math.Max(0, data.TotalHitObjectCount - judgedSoFar);
          return new()
          {
            Id = id,
            Label = "Remaining",
            Value = $"{remainingNotes}",
          };

        case 14:
          var bpm = MathUtils.IsNaNWithNum(data.Bpm.CurrentBpm).ToString("F1");
          var minBpm = MathUtils.IsNaNWithNum(data.Bpm.MinimumBpm).ToString("F0");
          var maxBpm = MathUtils.IsNaNWithNum(data.Bpm.MaximumBpm).ToString("F0");
          return new()
          {
            Id = id,
            Label = "BPM",
            Value = $"{bpm} ({minBpm}-{maxBpm})",
          };

        case 15:
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
