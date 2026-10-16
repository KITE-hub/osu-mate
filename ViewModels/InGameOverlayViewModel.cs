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

    private readonly List<(int Id, string Label, string Value)> _resolvedLines = [];

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

      _resolvedLines.Clear();
      foreach (int id in enabledIds)
      {
        if (
          TryGetLine(
            id,
            data,
            hits,
            accuracy,
            gamemode,
            audioTime,
            speedMultiplier,
            bestPp,
            out string label,
            out string value
          )
        )
          _resolvedLines.Add((id, label, value));
      }

      if (HasSameLayout(_resolvedLines))
      {
        for (int i = 0; i < _resolvedLines.Count; i++)
        {
          Lines[i].Label = _resolvedLines[i].Label;
          Lines[i].Value = _resolvedLines[i].Value;
        }
        return;
      }

      Lines = new ObservableCollection<OverlayLineItem>(
        _resolvedLines.Select(line => new OverlayLineItem
        {
          Id = line.Id,
          Label = line.Label,
          Value = line.Value,
        })
      );
    }

    private bool HasSameLayout(List<(int Id, string Label, string Value)> resolved)
    {
      if (Lines.Count != resolved.Count)
        return false;

      for (int i = 0; i < resolved.Count; i++)
      {
        if (Lines[i].Id != resolved[i].Id)
          return false;
      }

      return true;
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

    private static bool TryGetLine(
      int id,
      BeatmapData data,
      HitsResult hits,
      double accuracy,
      int gamemode,
      double audioTime,
      double speedMultiplier,
      double? bestPp,
      out string label,
      out string value
    )
    {
      switch (id)
      {
        case OverlayLineIds.StarRating:
          label = "SR";
          value = $"{MathUtils.IsNaNWithNum(Math.Round(data.DifficultyAttributes?.StarRating ?? 0, 2))}";
          return true;

        case OverlayLineIds.SsPp:
          label = "SS pp";
          value = $"{Math.Round(MathUtils.IsNaNWithNum(data.PerformanceAttributes?.Total))}pp";
          return true;

        case OverlayLineIds.LossPp:
          label = "Loss pp";
          value =
            $"{Math.Round(MathUtils.IsNaNWithNum(data.PerformanceAttributesLossMode?.Total))}pp";
          return true;

        case OverlayLineIds.PredictedPp:
          label = "Pred pp";
          value =
            $"{Math.Round(MathUtils.IsNaNWithNum(data.PerformanceAttributesPredicted?.Total))}pp";
          return true;

        case OverlayLineIds.CurrentPp:
          label = "pp";
          value =
            $"{Math.Round(MathUtils.IsNaNWithNum(data.CurrentPerformanceAttributes?.Total))}pp";
          return true;

        case OverlayLineIds.Accuracy:
          label = "ACC";
          value = FormatAccuracy(accuracy);
          return true;

        case OverlayLineIds.Hits:
          label = "Hits";
          value = OsuUtils.ConvertHits(gamemode, hits);
          return true;

        case OverlayLineIds.AverageOffset:
          label = "Avg offset";
          value = FormatAverageOffset(
            data.DetailedOffset.modifiedAvg,
            data.DetailedOffset.modifiedStdev
          );
          return true;

        case OverlayLineIds.UniversalOffsetHelp:
          label = "Universal offset help";
          value = $"{MathUtils.FormatNaturalSign(-1 * data.DetailedOffset.robustAvg)} ms";
          return true;

        case OverlayLineIds.LocalOffsetHelp:
          label = "Local offset help";
          value = $"{MathUtils.FormatNaturalSign(data.DetailedOffset.robustAvg)} ms";
          return true;

        case OverlayLineIds.UnstableRate:
          label = "UR";
          value = FormatUnstableRate(data.UR.rawUR, data.UR.modifiedUR);
          return true;

        case OverlayLineIds.Progress:
          label = "Progress";
          value = FormatProgress(data, audioTime, speedMultiplier);
          return true;

        case OverlayLineIds.RemainingNotes:
          int judgedSoFar =
            hits.HitGeki + hits.Hit300 + hits.HitKatu + hits.Hit100 + hits.Hit50 + hits.HitMiss;
          label = "Remaining";
          value = $"{Math.Max(0, data.TotalHitObjectCount - judgedSoFar)}";
          return true;

        case OverlayLineIds.Bpm:
          string bpm = MathUtils.IsNaNWithNum(data.Bpm.CurrentBpm).ToString("F1");
          string minBpm = MathUtils.IsNaNWithNum(data.Bpm.MinimumBpm).ToString("F0");
          string maxBpm = MathUtils.IsNaNWithNum(data.Bpm.MaximumBpm).ToString("F0");
          label = "BPM";
          value = $"{bpm} ({minBpm}-{maxBpm})";
          return true;

        case OverlayLineIds.BestPp:
          label = "Best pp";
          value = bestPp.HasValue ? $"{Math.Round(bestPp.Value)}pp" : "-";
          return true;

        default:
          label = string.Empty;
          value = string.Empty;
          return false;
      }
    }

    private static string FormatProgress(BeatmapData data, double audioTime, double speedMultiplier)
    {
      var mother = data.StrainTimeModified - data.FirstObjectTimeModified;
      if (mother == 0)
        return "0 %";

      var child = audioTime * speedMultiplier - data.FirstObjectTimeModified;
      var progress = Math.Clamp(child * 100 / mother, 0, 100);
      return $"{progress:F1} %";
    }
  }
}
