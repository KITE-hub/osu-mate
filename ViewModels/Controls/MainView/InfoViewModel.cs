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
      set => SetField(ref _sr, value);
    }

    private string _od = "-";
    public string Od
    {
      get => _od;
      set => SetField(ref _od, value);
    }

    private string _bpm = "-";
    public string Bpm
    {
      get => _bpm;
      set => SetField(ref _bpm, value);
    }

    private string _bpmRange = "";
    public string BpmRange
    {
      get => _bpmRange;
      set => SetField(ref _bpmRange, value);
    }

    private string _hitObject = "-";
    public string HitObject
    {
      get => _hitObject;
      set => SetField(ref _hitObject, value);
    }

    private string _time = "-";
    public string Time
    {
      get => _time;
      set => SetField(ref _time, value);
    }

    private string _density = "-";
    public string Density
    {
      get => _density;
      set => SetField(ref _density, value);
    }

    private string _hitGeki = "-";
    public string HitGeki
    {
      get => _hitGeki;
      set => SetField(ref _hitGeki, value);
    }

    private string _hit300 = "-";
    public string Hit300
    {
      get => _hit300;
      set => SetField(ref _hit300, value);
    }

    private string _hitKatu = "-";
    public string HitKatu
    {
      get => _hitKatu;
      set => SetField(ref _hitKatu, value);
    }

    private string _hit100 = "-";
    public string Hit100
    {
      get => _hit100;
      set => SetField(ref _hit100, value);
    }

    private string _hit50 = "-";
    public string Hit50
    {
      get => _hit50;
      set => SetField(ref _hit50, value);
    }

    private string _hitMiss = "-";
    public string HitMiss
    {
      get => _hitMiss;
      set => SetField(ref _hitMiss, value);
    }

    private string _windowPerfect = "";
    public string WindowPerfect
    {
      get => _windowPerfect;
      set => SetField(ref _windowPerfect, value);
    }

    private string _window300 = "";
    public string Window300
    {
      get => _window300;
      set => SetField(ref _window300, value);
    }

    private string _window200 = "";
    public string Window200
    {
      get => _window200;
      set => SetField(ref _window200, value);
    }

    private string _window100 = "";
    public string Window100
    {
      get => _window100;
      set => SetField(ref _window100, value);
    }

    private string _window50 = "";
    public string Window50
    {
      get => _window50;
      set => SetField(ref _window50, value);
    }

    private bool _isGekiVisible;
    public bool IsGekiVisible
    {
      get => _isGekiVisible;
      set => SetField(ref _isGekiVisible, value);
    }

    private bool _isKatuVisible;
    public bool IsKatuVisible
    {
      get => _isKatuVisible;
      set => SetField(ref _isKatuVisible, value);
    }

    private bool _is50Visible;
    public bool Is50Visible
    {
      get => _is50Visible;
      set => SetField(ref _is50Visible, value);
    }

    private string _ssPp = "-";
    public string SsPp
    {
      get => _ssPp;
      set => SetField(ref _ssPp, value);
    }

    private string _lossModePp = "-";
    public string LossModePp
    {
      get => _lossModePp;
      set => SetField(ref _lossModePp, value);
    }

    private string _predictedPp = "-";
    public string PredictedPp
    {
      get => _predictedPp;
      set => SetField(ref _predictedPp, value);
    }

    private string _currentPp = "-";
    public string CurrentPp
    {
      get => _currentPp;
      set => SetField(ref _currentPp, value);
    }

    private string _bestPp = "-";

    public string BestPp
    {
      get => _bestPp;
      set => SetField(ref _bestPp, value);
    }

    private string _latestPlayedAt = "-";

    public string LatestPlayedAt
    {
      get => _latestPlayedAt;
      set => SetField(ref _latestPlayedAt, value);
    }

    private string _bestPpAchievedAt = "-";

    public string BestPpAchievedAt
    {
      get => _bestPpAchievedAt;
      set => SetField(ref _bestPpAchievedAt, value);
    }

    private string _accuracy = "-";
    public string Accuracy
    {
      get => _accuracy;
      set => SetField(ref _accuracy, value);
    }

    private string _modifiedAvgOffset = "-";
    public string ModifiedAvgOffset
    {
      get => _modifiedAvgOffset;
      set => SetField(ref _modifiedAvgOffset, value);
    }

    private string _modifiedAvgOffsetStdev = "";
    public string ModifiedAvgOffsetStdev
    {
      get => _modifiedAvgOffsetStdev;
      set => SetField(ref _modifiedAvgOffsetStdev, value);
    }

    private string _rawUR = "-";
    public string RawUR
    {
      get => _rawUR;
      set => SetField(ref _rawUR, value);
    }

    private string _modifiedUR = "";
    public string ModifiedUR
    {
      get => _modifiedUR;
      set => SetField(ref _modifiedUR, value);
    }

    private string _localOffsetHelp = "-";

    public string LocalOffsetHelp
    {
      get => _localOffsetHelp;
      set => SetField(ref _localOffsetHelp, value);
    }

    private string _universalOffsetHelp = "-";

    public string UniversalOffsetHelp
    {
      get => _universalOffsetHelp;
      set => SetField(ref _universalOffsetHelp, value);
    }

    private readonly record struct MapInfoKey(
      double OverallDifficulty,
      double MinimumBpm,
      double MaximumBpm,
      int TotalHitObjectCount,
      int FirstObjectTimeModified,
      int LastObjectTimeModified
    );

    private readonly record struct StarRatingKey(double Current, double Total, bool IsPlaying);

    private readonly record struct PlaybackTimeKey(long CurrentSeconds, long DurationSeconds);

    private MapInfoKey? _lastMapInfoKey;
    private StarRatingKey? _lastStarRatingKey;
    private PlaybackTimeKey? _lastPlaybackTimeKey;
    private double? _lastCurrentBpm;
    private HitsResult? _lastHits;
    private Dictionary<HitResult, double>? _lastHitWindows;

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

      var starRatingKey = new StarRatingKey(currentSrValue, srValue, isPlaying);
      if (_lastStarRatingKey != starRatingKey)
      {
        _lastStarRatingKey = starRatingKey;
        Sr = isPlaying ? $"{currentSrValue:F2} / {srValue:F2}" : $"{srValue:F2}";
      }

      double currentBpm = MathUtils.IsNaNWithNum(data.Bpm.CurrentBpm);
      if (_lastCurrentBpm != currentBpm)
      {
        _lastCurrentBpm = currentBpm;
        Bpm = currentBpm.ToString("F1");
      }

      double duration = (data.LastObjectTimeModified - data.FirstObjectTimeModified) / 1000.0;

      var mapInfoKey = new MapInfoKey(
        data.OverallDifficulty,
        data.Bpm.MinimumBpm,
        data.Bpm.MaximumBpm,
        data.TotalHitObjectCount,
        data.FirstObjectTimeModified,
        data.LastObjectTimeModified
      );
      if (_lastMapInfoKey != mapInfoKey)
      {
        _lastMapInfoKey = mapInfoKey;
        Od = MathUtils.IsNaNWithNum(data.OverallDifficulty).ToString("F1");
        BpmRange =
          $" ( {MathUtils.IsNaNWithNum(data.Bpm.MinimumBpm):F0} - {MathUtils.IsNaNWithNum(data.Bpm.MaximumBpm):F0} )";
        HitObject = MathUtils.IsNaNWithNum(data.TotalHitObjectCount).ToString();
        double density =
          MathUtils.IsNaNWithNum(data.TotalHitObjectCount) / (duration == 0 ? 1 : duration);
        Density = density.ToString("F2");
      }

      double currentTimeSec = (audioTime * speedMultiplier - data.FirstObjectTimeModified) / 1000.0;
      var playbackTimeKey = new PlaybackTimeKey(
        (long)Math.Max(0, currentTimeSec),
        (long)Math.Max(0, duration)
      );
      if (_lastPlaybackTimeKey != playbackTimeKey)
      {
        _lastPlaybackTimeKey = playbackTimeKey;
        Time = FormatPlaybackTime(currentTimeSec, duration);
      }
    }

    internal void UpdateHits(HitsResult hits)
    {
      if (hits.Equals(_lastHits))
        return;

      _lastHits = hits;
      HitGeki = hits.HitGeki.ToString();
      Hit300 = hits.Hit300.ToString();
      HitKatu = hits.HitKatu.ToString();
      Hit100 = hits.Hit100.ToString();
      Hit50 = hits.Hit50.ToString();
      HitMiss = hits.HitMiss.ToString();
    }

    private static string FormatHitWindow(BeatmapData data, HitResult result) =>
      data.ModifiedHitWindows.TryGetValue(result, out double window) && window > 0
        ? $" (± {window:F2} ms)"
        : "-";

    private static string FormatPlaybackTime(double currentSeconds, double durationSeconds)
    {
      var current = TimeSpan.FromSeconds(Math.Max(0, currentSeconds));
      var duration = TimeSpan.FromSeconds(Math.Max(0, durationSeconds));
      var format = duration.TotalHours >= 1 ? @"h\:mm\:ss" : @"mm\:ss";
      return $"{current.ToString(format)} / {duration.ToString(format)}";
    }

    internal void UpdateJudge(BeatmapData data, HitsResult hits, int gamemode)
    {
      UpdateHits(hits);

      if (!ReferenceEquals(_lastHitWindows, data.ModifiedHitWindows))
      {
        _lastHitWindows = data.ModifiedHitWindows;
        WindowPerfect = FormatHitWindow(data, HitResult.Perfect);
        Window300 = FormatHitWindow(data, HitResult.Great);
        Window200 = FormatHitWindow(data, HitResult.Good);
        Window100 = FormatHitWindow(data, HitResult.Ok);
        Window50 = FormatHitWindow(data, HitResult.Meh);
      }

      IsGekiVisible = gamemode == OsuMode.Mania;
      IsKatuVisible = gamemode == OsuMode.Mania;
      Is50Visible = gamemode is OsuMode.Osu or OsuMode.Catch or OsuMode.Mania;
    }

    internal void UpdateBestPp(double? bestPp)
    {
      BestPp = bestPp.HasValue ? bestPp.Value.ToString("F2") : "-";
    }

    internal void UpdatePlayedAt(DateTimeOffset? latestPlayedAt, DateTimeOffset? bestPpPlayedAt)
    {
      static string Format(DateTimeOffset? d) => d.HasValue ? d.Value.ToString("yyyy-MM-dd") : "-";
      LatestPlayedAt = Format(latestPlayedAt);
      BestPpAchievedAt = Format(bestPpPlayedAt);
    }

    private readonly record struct PpKey(
      double Ss,
      double Loss,
      double Predicted,
      double Current,
      bool ShowLive
    );

    private PpKey? _lastPpKey;

    internal void UpdatePp(BeatmapData data, bool isPlaying, bool isResultScreen)
    {
      var key = new PpKey(
        MathUtils.IsNaNWithNum(data.PerformanceAttributes?.Total),
        MathUtils.IsNaNWithNum(data.PerformanceAttributesLossMode?.Total),
        MathUtils.IsNaNWithNum(data.PerformanceAttributesPredicted?.Total),
        MathUtils.IsNaNWithNum(data.CurrentPerformanceAttributes?.Total),
        isPlaying || isResultScreen
      );

      if (_lastPpKey == key)
        return;
      _lastPpKey = key;

      SsPp = key.Ss.ToString("F2");

      if (key.ShowLive)
      {
        LossModePp = key.Loss.ToString("F2");
        PredictedPp = key.Predicted.ToString("F2");
        CurrentPp = key.Current.ToString("F2");
      }
      else
      {
        LossModePp = "-";
        PredictedPp = "-";
        CurrentPp = "-";
      }
    }

    private double? _lastAccuracy;
    private (double? Avg, double? Stdev) _lastOffset;
    private double? _lastRobustAvg;
    private (double? Raw, double? Modified) _lastUr;

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
      if (_lastAccuracy != accuracy)
      {
        _lastAccuracy = accuracy;
        Accuracy = accuracy.ToString("F2");
      }

      if (!isPlaying)
        return;

      if (modifiedAvg.HasValue && _lastOffset != (modifiedAvg, modifiedStdev))
      {
        _lastOffset = (modifiedAvg, modifiedStdev);
        ModifiedAvgOffset = MathUtils.FormatUnder4CharsSign(modifiedAvg.Value);
        ModifiedAvgOffsetStdev = " ± " + MathUtils.FormatUnder4Chars(modifiedStdev!.Value);
      }
      if (robustAvg.HasValue && _lastRobustAvg != robustAvg)
      {
        _lastRobustAvg = robustAvg;
        LocalOffsetHelp = MathUtils.FormatNaturalSign(robustAvg.Value) + " ms";
        UniversalOffsetHelp = MathUtils.FormatNaturalSign(-1 * robustAvg.Value) + " ms";
      }
      if (rawUR.HasValue && _lastUr != (rawUR, modifiedUR))
      {
        _lastUr = (rawUR, modifiedUR);
        RawUR = MathUtils.IsNaNWithNum(rawUR.Value).ToString("F2");
        ModifiedUR = " ( " + MathUtils.IsNaNWithNum(modifiedUR!.Value).ToString("F2") + " )";
      }
    }
  }
}
