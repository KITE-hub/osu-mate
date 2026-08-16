using osu.Game.Rulesets.Scoring;
using OsuMate.Models;
using OsuMate.Utils;

namespace OsuMate.ViewModels
{
  public class InfoViewModel : ObservableBase
  {
    private string _sr = "-";
    public string Sr
    {
      get => _sr;
      set
      {
        _sr = value;
        OnPropertyChanged();
      }
    }

    private string _od = "-";
    public string Od
    {
      get => _od;
      set
      {
        _od = value;
        OnPropertyChanged();
      }
    }

    private string _bpm = "-";
    public string Bpm
    {
      get => _bpm;
      set
      {
        _bpm = value;
        OnPropertyChanged();
      }
    }

    private string _bpmRange = "";
    public string BpmRange
    {
      get => _bpmRange;
      set
      {
        _bpmRange = value;
        OnPropertyChanged();
      }
    }

    private string _hitObject = "-";
    public string HitObject
    {
      get => _hitObject;
      set
      {
        _hitObject = value;
        OnPropertyChanged();
      }
    }

    private string _time = "-";
    public string Time
    {
      get => _time;
      set
      {
        _time = value;
        OnPropertyChanged();
      }
    }

    private string _density = "-";
    public string Density
    {
      get => _density;
      set
      {
        _density = value;
        OnPropertyChanged();
      }
    }

    private string _hitGeki = "-";
    public string HitGeki
    {
      get => _hitGeki;
      set
      {
        _hitGeki = value;
        OnPropertyChanged();
      }
    }

    private string _hit300 = "-";
    public string Hit300
    {
      get => _hit300;
      set
      {
        _hit300 = value;
        OnPropertyChanged();
      }
    }

    private string _hitKatu = "-";
    public string HitKatu
    {
      get => _hitKatu;
      set
      {
        _hitKatu = value;
        OnPropertyChanged();
      }
    }

    private string _hit100 = "-";
    public string Hit100
    {
      get => _hit100;
      set
      {
        _hit100 = value;
        OnPropertyChanged();
      }
    }

    private string _hit50 = "-";
    public string Hit50
    {
      get => _hit50;
      set
      {
        _hit50 = value;
        OnPropertyChanged();
      }
    }

    private string _hitMiss = "-";
    public string HitMiss
    {
      get => _hitMiss;
      set
      {
        _hitMiss = value;
        OnPropertyChanged();
      }
    }

    private string _windowPerfect = "";
    public string WindowPerfect
    {
      get => _windowPerfect;
      set
      {
        _windowPerfect = value;
        OnPropertyChanged();
      }
    }

    private string _window300 = "";
    public string Window300
    {
      get => _window300;
      set
      {
        _window300 = value;
        OnPropertyChanged();
      }
    }

    private string _window200 = "";
    public string Window200
    {
      get => _window200;
      set
      {
        _window200 = value;
        OnPropertyChanged();
      }
    }

    private string _window100 = "";
    public string Window100
    {
      get => _window100;
      set
      {
        _window100 = value;
        OnPropertyChanged();
      }
    }

    private string _window50 = "";
    public string Window50
    {
      get => _window50;
      set
      {
        _window50 = value;
        OnPropertyChanged();
      }
    }

    private bool _isGekiVisible;
    public bool IsGekiVisible
    {
      get => _isGekiVisible;
      set
      {
        _isGekiVisible = value;
        OnPropertyChanged();
      }
    }

    private bool _isKatuVisible;
    public bool IsKatuVisible
    {
      get => _isKatuVisible;
      set
      {
        _isKatuVisible = value;
        OnPropertyChanged();
      }
    }

    private bool _is50Visible;
    public bool Is50Visible
    {
      get => _is50Visible;
      set
      {
        _is50Visible = value;
        OnPropertyChanged();
      }
    }

    private string _ssPp = "-";
    public string SsPp
    {
      get => _ssPp;
      set
      {
        _ssPp = value;
        OnPropertyChanged();
      }
    }

    private string _lossModePp = "-";
    public string LossModePp
    {
      get => _lossModePp;
      set
      {
        _lossModePp = value;
        OnPropertyChanged();
      }
    }

    private string _predictedPp = "-";
    public string PredictedPp
    {
      get => _predictedPp;
      set
      {
        _predictedPp = value;
        OnPropertyChanged();
      }
    }

    private string _currentPp = "-";
    public string CurrentPp
    {
      get => _currentPp;
      set
      {
        _currentPp = value;
        OnPropertyChanged();
      }
    }

    private string _bestPp = "-";

    public string BestPp
    {
      get => _bestPp;
      set
      {
        _bestPp = value;
        OnPropertyChanged();
      }
    }

    private string _accuracy = "-";
    public string Accuracy
    {
      get => _accuracy;
      set
      {
        _accuracy = value;
        OnPropertyChanged();
      }
    }

    private string _modifiedAvgOffset = "-";
    public string ModifiedAvgOffset
    {
      get => _modifiedAvgOffset;
      set
      {
        _modifiedAvgOffset = value;
        OnPropertyChanged();
      }
    }

    private string _modifiedAvgOffsetStdev = "";
    public string ModifiedAvgOffsetStdev
    {
      get => _modifiedAvgOffsetStdev;
      set
      {
        _modifiedAvgOffsetStdev = value;
        OnPropertyChanged();
      }
    }

    private string _rawUR = "-";
    public string RawUR
    {
      get => _rawUR;
      set
      {
        _rawUR = value;
        OnPropertyChanged();
      }
    }

    private string _modifiedUR = "";
    public string ModifiedUR
    {
      get => _modifiedUR;
      set
      {
        _modifiedUR = value;
        OnPropertyChanged();
      }
    }

    private string _localOffsetHelp = "-";

    public string LocalOffsetHelp
    {
      get => _localOffsetHelp;
      set
      {
        _localOffsetHelp = value;
        OnPropertyChanged();
      }
    }

    private string _universalOffsetHelp = "-";

    public string UniversalOffsetHelp
    {
      get => _universalOffsetHelp;
      set
      {
        _universalOffsetHelp = value;
        OnPropertyChanged();
      }
    }

    internal void UpdateMapInfo(
      BeatmapData data,
      bool isPlaying,
      double audioTime,
      double speedMultiplier
    )
    {
      double srValue = MathUtils.IsNaNWithNum(
        Math.Round(data.DifficultyAttributes?.StarRating ?? 0, 2)
      );
      double currentSrValue = MathUtils.IsNaNWithNum(
        Math.Round(data.CurrentDifficultyAttributes?.StarRating ?? 0, 2)
      );

      Sr = isPlaying ? $"{currentSrValue:F2} / {srValue:F2}" : $"{srValue:F2}";
      Od = MathUtils.IsNaNWithNum(data.OverallDifficulty).ToString("F1");
      Bpm = MathUtils.IsNaNWithNum(data.Bpm.CurrentBpm).ToString("F1");
      BpmRange =
        $" ( {MathUtils.IsNaNWithNum(data.Bpm.MinimumBpm):F0} - {MathUtils.IsNaNWithNum(data.Bpm.MaximumBpm):F0} )";
      HitObject = MathUtils.IsNaNWithNum(data.TotalHitObjectCount).ToString();

      double currentTimeSec = (audioTime * speedMultiplier - data.FirstObjectTimeModified) / 1000.0;
      double duration = (data.LastObjectTimeModified - data.FirstObjectTimeModified) / 1000.0;
      Time =
        $"{TimeSpan.FromSeconds(Math.Max(0, currentTimeSec)):mm\\:ss} / {TimeSpan.FromSeconds(duration):mm\\:ss}";

      double density =
        MathUtils.IsNaNWithNum(data.TotalHitObjectCount) / (duration == 0 ? 1 : duration);
      Density = density.ToString("F2");
    }

    internal void UpdateHits(HitsResult hits)
    {
      HitGeki = hits.HitGeki.ToString();
      Hit300 = hits.Hit300.ToString();
      HitKatu = hits.HitKatu.ToString();
      Hit100 = hits.Hit100.ToString();
      Hit50 = hits.Hit50.ToString();
      HitMiss = hits.HitMiss.ToString();
    }

    internal void UpdateJudge(BeatmapData data, HitsResult hits, int gamemode)
    {
      UpdateHits(hits);

      if (data.ModifiedHitWindows.TryGetValue(HitResult.Perfect, out double perfect))
        WindowPerfect = $" (± {perfect:F2} ms)";
      if (data.ModifiedHitWindows.TryGetValue(HitResult.Great, out double great))
        Window300 = $" (± {great:F2} ms)";
      if (data.ModifiedHitWindows.TryGetValue(HitResult.Good, out double good))
        Window200 = $" (± {good:F2} ms)";
      if (data.ModifiedHitWindows.TryGetValue(HitResult.Ok, out double ok))
        Window100 = $" (± {ok:F2} ms)";
      if (data.ModifiedHitWindows.TryGetValue(HitResult.Meh, out double meh))
        Window50 = $" (± {meh:F2} ms)";

      IsGekiVisible = gamemode == 3;
      IsKatuVisible = gamemode == 3;
      Is50Visible = gamemode == 0 || gamemode == 2 || gamemode == 3;
    }

    internal void UpdateBestPp(double? bestPp)
    {
      BestPp = bestPp.HasValue ? bestPp.Value.ToString("F2") : "-";
    }

    internal void UpdatePp(BeatmapData data, bool isPlaying, bool isResultScreen)
    {
      SsPp = MathUtils.IsNaNWithNum(data.PerformanceAttributes?.Total).ToString("F2");
      double currentPp = MathUtils.IsNaNWithNum(data.CurrentPerformanceAttributes?.Total);

      if (isPlaying || isResultScreen)
      {
        LossModePp = MathUtils
          .IsNaNWithNum(data.PerformanceAttributesLossMode?.Total)
          .ToString("F2");
        PredictedPp = MathUtils
          .IsNaNWithNum(data.PerformanceAttributesPredicted?.Total)
          .ToString("F2");
        CurrentPp = currentPp.ToString("F2");
      }
      else
      {
        LossModePp = "-";
        PredictedPp = "-";
        CurrentPp = "-";
      }
    }

    internal void UpdateAccFast(
      double accuracy,
      bool isPlaying,
      double? robustAvg,
      double? modifiedAvg,
      double? modifiedStdev,
      double? rawUR,
      double? modifiedUR
    )
    {
      Accuracy = accuracy.ToString("F2");

      if (!isPlaying)
        return;

      if (modifiedAvg.HasValue)
      {
        ModifiedAvgOffset = MathUtils.FormatUnder4CharsSign(modifiedAvg.Value);
        ModifiedAvgOffsetStdev = " ± " + MathUtils.FormatUnder4Chars(modifiedStdev!.Value);
      }
      if (robustAvg.HasValue)
      {
        LocalOffsetHelp = MathUtils.FormatNaturalSign(robustAvg.Value) + " ms";
        UniversalOffsetHelp = MathUtils.FormatNaturalSign(-1 * robustAvg.Value) + " ms";
      }
      if (rawUR.HasValue)
      {
        RawUR = MathUtils.IsNaNWithNum(rawUR.Value).ToString("F2");
        ModifiedUR = " ( " + MathUtils.IsNaNWithNum(modifiedUR!.Value).ToString("F2") + " )";
      }
    }
  }
}
