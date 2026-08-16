using System;

namespace OsuMate.Models
{
  public class PlayLogEntry : ObservableBase
  {
    public DateTime PlayedAt { get; set; }

    public string DedupeKey { get; set; } = "";

    public long? OnlineScoreId { get; set; }
    public string? ReplayMd5 { get; set; }

    public int BeatmapId { get; set; }
    public int BeatmapSetId { get; set; }
    public string Artist { get; set; } = "";
    public string Title { get; set; } = "";
    public string DifficultyName { get; set; } = "";
    public string Creator { get; set; } = "";

    public string BeatmapMd5 { get; set; } = "";

    public string PlayerName { get; set; } = "";

    public int Mode { get; set; }

    public int? ManiaKeyCount { get; set; }

    public LogModeCategory ModeCategory => LogModeClassifier.Classify(Mode, ManiaKeyCount);

    private int _count300;
    public int Count300
    {
      get => _count300;
      set => SetField(ref _count300, value);
    }

    private int _count100;
    public int Count100
    {
      get => _count100;
      set => SetField(ref _count100, value);
    }

    private int _count50;
    public int Count50
    {
      get => _count50;
      set => SetField(ref _count50, value);
    }

    private int _countGeki;
    public int CountGeki
    {
      get => _countGeki;
      set => SetField(ref _countGeki, value);
    }

    private int _countKatu;
    public int CountKatu
    {
      get => _countKatu;
      set => SetField(ref _countKatu, value);
    }

    private int _countMiss;
    public int CountMiss
    {
      get => _countMiss;
      set => SetField(ref _countMiss, value);
    }

    private int _maxCombo;
    public int MaxCombo
    {
      get => _maxCombo;
      set => SetField(ref _maxCombo, value);
    }

    private int _totalScore;
    public int TotalScore
    {
      get => _totalScore;
      set => SetField(ref _totalScore, value);
    }

    private double _accuracy;

    public double Accuracy
    {
      get => _accuracy;
      set => SetField(ref _accuracy, value);
    }

    public double OverallDifficulty { get; set; }

    public string ModsString { get; set; } = "NM";

    public int ModsRaw { get; set; }

    private bool _isCompleted;

    public bool IsCompleted
    {
      get => _isCompleted;
      set => SetField(ref _isCompleted, value);
    }

    private bool _isProvisional;

    public bool IsProvisional
    {
      get => _isProvisional;
      set => SetField(ref _isProvisional, value);
    }

    private double? _starRating;

    public double? StarRating
    {
      get => _starRating;
      set => SetField(ref _starRating, value);
    }

    private double? _pp;

    public double? Pp
    {
      get => _pp;
      set => SetField(ref _pp, value);
    }

    private bool _isCalculationFailed;

    public bool IsCalculationFailed
    {
      get => _isCalculationFailed;
      set => SetField(ref _isCalculationFailed, value);
    }
  }
}
