using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Threading;
using OsuMate.Models;
using OsuMate.Services;
using OsuMate.Services.Trainer;
using OsuMate.Utils;

namespace OsuMate.ViewModels
{
  public class BatchPreviewItem
  {
    public string RateText { get; set; } = "";
    public string BpmText { get; set; } = "";
    public string ArText { get; set; } = "";
    public string OdText { get; set; } = "";
    public string HpText { get; set; } = "";
    public string CsText { get; set; } = "";
  }

  public class TrainerViewModel : ObservableBase, IDisposable
  {
    private sealed class DifficultyParameter
    {
      private readonly Func<decimal, decimal, bool, bool, decimal>? _computeScaled;

      public decimal? Original { get; set; }
      public bool HasOriginal => Original.HasValue;

      private decimal _base;

      public decimal Base
      {
        get => _base;
        set => _base = TrainerCalculationService.ClampDifficulty(value);
      }

      public string BaseText => HasOriginal ? $"{Base:F1}" : "-";

      public bool ScaleEnabled { get; set; } = true;

      public DifficultyParameter(Func<decimal, decimal, bool, bool, decimal>? computeScaled = null)
      {
        _computeScaled = computeScaled;
      }

      public decimal Scaled(decimal rate) =>
        _computeScaled != null ? _computeScaled(Base, rate, ScaleEnabled, HasOriginal) : Base;
    }

    private readonly BeatmapTrainerService _trainerService;
    private readonly OsuMemoryService _memory;
    private readonly ConfigStore _config;
    private readonly Dispatcher _dispatcher;

    private readonly System.Threading.Timer _pollTimer;
    private System.Threading.CancellationTokenSource? _generationCts;

    private string _lastActualPath = "";
    private string _effectiveBeatmapPath = "";

    private int _mode = 0;

    private bool _isArCsEditable = true;

    public bool IsArCsEditable
    {
      get => _isArCsEditable;
      private set
      {
        if (_isArCsEditable == value)
          return;
        _isArCsEditable = value;
        OnPropertyChanged();
        OnPropertyChanged(nameof(CanGenerate));
      }
    }

    private readonly DifficultyParameter _ar = new(TrainerCalculationService.ComputeApproachRate);
    private readonly DifficultyParameter _od = new(
      TrainerCalculationService.ComputeOverallDifficulty
    );
    private readonly DifficultyParameter _hp = new();
    private readonly DifficultyParameter _cs = new();

    private decimal _originalBpm = 0M;
    private decimal _minBpm = 0M;
    private decimal _maxBpm = 0M;

    private string _originalBpm_ = "-";
    public string OriginalBpm
    {
      get => _originalBpm_;
      private set
      {
        _originalBpm_ = value;
        OnPropertyChanged();
      }
    }

    private string _originalBpmRange = "";

    public string OriginalBpmRange
    {
      get => _originalBpmRange;
      private set
      {
        _originalBpmRange = value;
        OnPropertyChanged();
      }
    }

    private void UpdateBpmTexts()
    {
      if (_originalBpm > 0)
      {
        OriginalBpm = _originalBpm.ToString("F1", CultureInfo.InvariantCulture);
        OriginalBpmRange = $" ( {_minBpm:F0} - {_maxBpm:F0} )";
      }
      else
      {
        OriginalBpm = "-";
        OriginalBpmRange = "";
      }
    }

    private decimal _batchStartRate = 1.05M;
    public decimal BatchStartRate
    {
      get => _batchStartRate;
      set
      {
        var clamped = Math.Max(0.5M, Math.Min(2.0M, value));
        if (_batchStartRate == clamped)
          return;
        _batchStartRate = clamped;
        OnPropertyChanged();
        UpdateBatchPreviews();
        SaveBatchStartRate(clamped);
      }
    }

    private void SaveBatchStartRate(decimal value) =>
      _config.Update(g => g.BatchStartRate = value);

    private decimal _batchStep = 0.05M;
    public decimal BatchStep
    {
      get => _batchStep;
      set
      {
        var clamped = Math.Max(0.01M, Math.Min(1.0M, value));
        if (_batchStep == clamped)
          return;
        _batchStep = clamped;
        OnPropertyChanged();
        UpdateBatchPreviews();
        SaveBatchStep(clamped);
      }
    }

    private void SaveBatchStep(decimal value) =>
      _config.Update(g => g.BatchStep = value);

    private int _batchCount = 4;
    public int BatchCount
    {
      get => _batchCount;
      set
      {
        var clamped = Math.Max(1, Math.Min(20, value));
        if (_batchCount == clamped)
          return;
        _batchCount = clamped;
        OnPropertyChanged();
        UpdateBatchPreviews();
        SaveBatchCount(clamped);
      }
    }

    private void SaveBatchCount(int value) =>
      _config.Update(g => g.BatchCount = value);

    private static decimal ClampBpmSetting(decimal value) =>
      Math.Clamp(value, OffsetMapBuilder.MinBpmSetting, OffsetMapBuilder.MaxBpmSetting);

    private decimal _offsetMapSixteenthMaxBpm = 300M;
    public decimal OffsetMapSixteenthMaxBpm
    {
      get => _offsetMapSixteenthMaxBpm;
      set
      {
        var clamped = ClampBpmSetting(value);
        bool changed = _offsetMapSixteenthMaxBpm != clamped;
        _offsetMapSixteenthMaxBpm = clamped;
        OnPropertyChanged();
        if (changed)
          SaveOffsetMapSixteenthMaxBpm(clamped);
      }
    }

    private void SaveOffsetMapSixteenthMaxBpm(decimal value) =>
      _config.Update(g => g.OffsetMapSixteenthMaxBpm = value);

    private decimal _offsetMapScrollBpm = 200M;
    public decimal OffsetMapScrollBpm
    {
      get => _offsetMapScrollBpm;
      set
      {
        var clamped = ClampBpmSetting(value);
        bool changed = _offsetMapScrollBpm != clamped;
        _offsetMapScrollBpm = clamped;
        OnPropertyChanged();
        if (changed)
          SaveOffsetMapScrollBpm(clamped);
      }
    }

    private void SaveOffsetMapScrollBpm(decimal value) =>
      _config.Update(g => g.OffsetMapScrollBpm = value);

    public ObservableCollection<BatchPreviewItem> BatchPreviews { get; } = new();

    private void UpdateBatchPreviews()
    {
      var request = new BatchPreviewRequest(
        StartRate: BatchStartRate,
        Step: BatchStep,
        Count: BatchCount,
        MaxRate: 2.0M,
        OriginalBpm: _originalBpm,
        MinBpm: _minBpm,
        MaxBpm: _maxBpm,
        ArBase: ArBase,
        ScaleAr: ScaleAR && IsArCsEditable,
        HasOriginalAr: _ar.HasOriginal,
        OdBase: OdBase,
        ScaleOd: ScaleOD,
        HasOriginalOd: _od.HasOriginal,
        HpBase: HpBase,
        HasOriginalHp: _hp.HasOriginal,
        CsBase: CsBase,
        HasOriginalCs: _cs.HasOriginal
      );

      var previews = TrainerCalculationService.ComputeBatchPreviews(request);

      BatchPreviews.Clear();
      foreach (var p in previews)
      {
        BatchPreviews.Add(
          new BatchPreviewItem
          {
            RateText = $"{p.Rate:0.00}x",
            BpmText = p.Bpm.HasValue ? $"{p.Bpm:F1} ( {p.MinBpm:F0} - {p.MaxBpm:F0} )" : "-",
            ArText = p.Ar.HasValue ? $"{p.Ar:F1}" : "-",
            OdText = p.Od.HasValue ? $"{p.Od:F1}" : "-",
            HpText = p.Hp.HasValue ? $"{p.Hp:F1}" : "-",
            CsText = p.Cs.HasValue ? $"{p.Cs:F1}" : "-",
          }
        );
      }
    }

    private string _beatmapTitle = "-";
    public string BeatmapTitle
    {
      get => _beatmapTitle;
      private set
      {
        _beatmapTitle = value;
        OnPropertyChanged();
      }
    }

    private string _beatmapArtist = "";
    public string BeatmapArtist
    {
      get => _beatmapArtist;
      private set
      {
        _beatmapArtist = value;
        OnPropertyChanged();
      }
    }

    private string _beatmapVersion = "";
    public string BeatmapVersion
    {
      get => _beatmapVersion;
      private set
      {
        _beatmapVersion = value;
        OnPropertyChanged();
      }
    }

    private string _redirectedMessage = "";
    public string RedirectedMessage
    {
      get => _redirectedMessage;
      private set
      {
        _redirectedMessage = value;
        OnPropertyChanged();
        OnPropertyChanged(nameof(IsRedirected));
      }
    }
    public bool IsRedirected => !string.IsNullOrEmpty(_redirectedMessage);

    private bool _isOriginalMapMissing = false;

    public bool IsOriginalMapMissing
    {
      get => _isOriginalMapMissing;
      private set
      {
        _isOriginalMapMissing = value;
        OnPropertyChanged();
        OnPropertyChanged(nameof(CanGenerate));
      }
    }

    public decimal ArBase
    {
      get => _ar.Base;
      set
      {
        _ar.Base = value;
        OnPropertyChanged();
        OnPropertyChanged(nameof(ArBaseText));
        OnPropertyChanged(nameof(CanGenerate));
        UpdateBatchPreviews();
      }
    }
    public string ArBaseText => _ar.BaseText;

    private decimal ArScaledFor(decimal rate) => IsArCsEditable ? _ar.Scaled(rate) : _ar.Base;

    public bool ScaleAR
    {
      get => _ar.ScaleEnabled;
      set
      {
        _ar.ScaleEnabled = value;
        OnPropertyChanged();
        OnPropertyChanged(nameof(CanGenerate));
        UpdateBatchPreviews();
      }
    }

    public decimal OdBase
    {
      get => _od.Base;
      set
      {
        _od.Base = value;
        OnPropertyChanged();
        OnPropertyChanged(nameof(OdBaseText));
        OnPropertyChanged(nameof(CanGenerate));
        UpdateBatchPreviews();
      }
    }
    public string OdBaseText => _od.BaseText;

    public bool ScaleOD
    {
      get => _od.ScaleEnabled;
      set
      {
        _od.ScaleEnabled = value;
        OnPropertyChanged();
        OnPropertyChanged(nameof(CanGenerate));
        UpdateBatchPreviews();
      }
    }

    public decimal HpBase
    {
      get => _hp.Base;
      set
      {
        _hp.Base = value;
        OnPropertyChanged();
        OnPropertyChanged(nameof(HpBaseText));
        OnPropertyChanged(nameof(CanGenerate));
        UpdateBatchPreviews();
      }
    }
    public string HpBaseText => _hp.BaseText;

    public decimal CsBase
    {
      get => _cs.Base;
      set
      {
        _cs.Base = value;
        OnPropertyChanged();
        OnPropertyChanged(nameof(CsBaseText));
        OnPropertyChanged(nameof(CanGenerate));
        UpdateBatchPreviews();
      }
    }
    public string CsBaseText => _cs.BaseText;

    private bool _isRandomEnabled = false;
    public bool IsRandomEnabled
    {
      get => _isRandomEnabled;
      set
      {
        if (_isRandomEnabled == value)
          return;
        _isRandomEnabled = value;
        OnPropertyChanged();
        SaveIsRandomEnabled(value);
      }
    }

    private void SaveIsRandomEnabled(bool value) =>
      _config.Update(g => g.IsRandomEnabled = value);

    private bool _disableSv = false;
    public bool DisableSv
    {
      get => _disableSv;
      set
      {
        if (_disableSv == value)
          return;
        _disableSv = value;
        OnPropertyChanged();
        SaveDisableSv(value);
      }
    }

    private void SaveDisableSv(bool value) =>
      _config.Update(g => g.DisableSv = value);

    private bool _isRandomAvailable = true;
    public bool IsRandomAvailable
    {
      get => _isRandomAvailable;
      private set
      {
        if (_isRandomAvailable == value)
          return;
        _isRandomAvailable = value;
        OnPropertyChanged();
      }
    }

    private bool _isDisableSvAvailable = true;
    public bool IsDisableSvAvailable
    {
      get => _isDisableSvAvailable;
      private set
      {
        if (_isDisableSvAvailable == value)
          return;
        _isDisableSvAvailable = value;
        OnPropertyChanged();
      }
    }

    private bool _adjustPitchWithSpeed = false;
    public bool AdjustPitchWithSpeed
    {
      get => _adjustPitchWithSpeed;
      set
      {
        if (_adjustPitchWithSpeed == value)
          return;
        _adjustPitchWithSpeed = value;
        OnPropertyChanged();
        SaveAdjustPitchWithSpeed(value);
      }
    }

    private void SaveAdjustPitchWithSpeed(bool value) =>
      _config.Update(g => g.AdjustPitchWithSpeed = value);

    private bool _isGenerating = false;
    public bool IsGenerating
    {
      get => _isGenerating;
      private set
      {
        _isGenerating = value;
        OnPropertyChanged();
        OnPropertyChanged(nameof(CanGenerate));
      }
    }

    private string _statusMessage = "Please select a beatmap in osu!";
    public string StatusMessage
    {
      get => _statusMessage;
      set
      {
        _statusMessage = value;
        OnPropertyChanged();
      }
    }

    private bool _isBeatmapLoaded = false;
    public bool IsBeatmapLoaded
    {
      get => _isBeatmapLoaded;
      private set
      {
        _isBeatmapLoaded = value;
        OnPropertyChanged();
        OnPropertyChanged(nameof(CanGenerate));
      }
    }

    public bool CanGenerate => IsBeatmapLoaded && !IsGenerating && !IsOriginalMapMissing;

    public TrainerViewModel(
      BeatmapTrainerService trainerService,
      OsuMemoryService memory,
      ConfigStore config
    )
    {
      _trainerService = trainerService;
      _memory = memory;
      _config = config;
      _dispatcher = Dispatcher.CurrentDispatcher;

      var globalConfig = _config.Global;
      _adjustPitchWithSpeed = globalConfig.AdjustPitchWithSpeed;
      _isRandomEnabled = globalConfig.IsRandomEnabled;
      _disableSv = globalConfig.DisableSv;
      _batchStartRate = globalConfig.BatchStartRate;
      _batchStep = globalConfig.BatchStep;
      _batchCount = globalConfig.BatchCount;
      _offsetMapSixteenthMaxBpm = ClampBpmSetting(globalConfig.OffsetMapSixteenthMaxBpm);
      _offsetMapScrollBpm = ClampBpmSetting(globalConfig.OffsetMapScrollBpm);

      _pollTimer = new System.Threading.Timer(PollBeatmap, null, 2000, 2000);
    }

    public void PausePolling() =>
      _pollTimer.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);

    public void ResumePolling() => _pollTimer.Change(0, 2000);

    private void PollBeatmap(object? _)
    {
      try
      {
        string? path = _trainerService.GetCurrentBeatmapPath();
        if (path == null || path == _lastActualPath)
          return;

        OsuBeatmapFile bm;
        try
        {
          bm = OsuBeatmapFile.Load(path);
        }
        catch (Exception ex)
        {
          LogUtils.DebugLogger($"[Trainer] Failed to load beatmap: {ex}", true);
          return;
        }

        _lastActualPath = path;

        if (bm.IsOsuTrainerMap)
        {
          string? origPath = BeatmapTrainerService.FindOriginalMap(path);
          if (origPath != null)
          {
            OsuBeatmapFile origBm;
            try
            {
              origBm = OsuBeatmapFile.Load(origPath);
            }
            catch (Exception ex)
            {
              LogUtils.DebugLogger(
                $"[Trainer] Failed to load original beatmap: {ex}",
                true
              );
              _lastActualPath = string.Empty;
              return;
            }
            string msg = $"↩ Trainer map detected -> Switched to original map";
            _effectiveBeatmapPath = origPath;
            LoadBeatmapInfo(origBm, msg, originalMissing: false);
            return;
          }

          _effectiveBeatmapPath = path;
          LoadBeatmapInfo(
            bm,
            "⚠ Trainer map detected (Original map not found) - Generation disabled",
            originalMissing: true
          );
          return;
        }

        _effectiveBeatmapPath = path;
        LoadBeatmapInfo(bm, "", originalMissing: false);
      }
      catch (Exception ex)
      {
        LogUtils.DebugLogger($"[Trainer] Unexpected exception in PollBeatmap: {ex}", true);
      }
    }

    private void LoadBeatmapInfo(OsuBeatmapFile bm, string redirectedMessage, bool originalMissing)
    {
      _dispatcher.BeginInvoke(() =>
      {
        BeatmapTitle = bm.Title;
        BeatmapArtist = bm.Artist;
        BeatmapVersion = bm.Version;
        _originalBpm = bm.DominantBpm;
        _minBpm = bm.MinBpm;
        _maxBpm = bm.MaxBpm;

        _mode = bm.Mode;
        IsArCsEditable = !bm.IsTaikoOrMania;
        IsRandomAvailable = !bm.IsCatch;
        IsDisableSvAvailable = bm.IsTaikoOrMania;

        _ar.Original = bm.ApproachRate >= 0 ? bm.ApproachRate : (decimal?)null;
        _od.Original = bm.OverallDifficulty >= 0 ? bm.OverallDifficulty : (decimal?)null;
        _hp.Original = bm.HPDrainRate >= 0 ? bm.HPDrainRate : (decimal?)null;
        _cs.Original = bm.CircleSize >= 0 ? bm.CircleSize : (decimal?)null;

        if (_ar.HasOriginal)
          ArBase = _ar.Original!.Value;
        if (_od.HasOriginal)
          OdBase = _od.Original!.Value;
        if (_hp.HasOriginal)
          HpBase = _hp.Original!.Value;
        if (_cs.HasOriginal)
          CsBase = _cs.Original!.Value;

        RedirectedMessage = redirectedMessage;
        IsOriginalMapMissing = originalMissing;
        IsBeatmapLoaded = true;

        UpdateBpmTexts();

        StatusMessage = string.IsNullOrEmpty(redirectedMessage)
          ? "Beatmap loaded"
          : redirectedMessage;

        UpdateBatchPreviews();
      });
    }

    public Task GenerateAsync() =>
      RunGenerationAsync(
        (beatmapPath, ct) =>
          _trainerService.GenerateBeatmapsBatchAsync(
            beatmapPath,
            BuildBatchRequests(),
            AdjustPitchWithSpeed,
            IsRandomEnabled && IsRandomAvailable,
            DisableSv && IsDisableSvAvailable,
            ReportStatus,
            ct
          )
      );

    public Task GenerateOffsetMapAsync() =>
      RunGenerationAsync(
        (beatmapPath, ct) =>
          _trainerService.GenerateOffsetMapAsync(
            beatmapPath,
            new OffsetMapRequest(OffsetMapSixteenthMaxBpm, OffsetMapScrollBpm),
            ReportStatus,
            ct
          )
      );

    private List<BatchGenerationRequest> BuildBatchRequests()
    {
      var requests = new List<BatchGenerationRequest>();
      for (int i = 0; i < BatchCount; i++)
      {
        decimal rate = BatchStartRate + (BatchStep * i);
        if (rate > 2.0M)
          break;

        requests.Add(
          new BatchGenerationRequest
          {
            Rate = rate,
            ArOverride = TrainerCalculationService.ResolveOverride(
              _ar.Original,
              ArScaledFor(rate)
            ),
            OdOverride = TrainerCalculationService.ResolveOverride(
              _od.Original,
              _od.Scaled(rate)
            ),
            HpOverride = TrainerCalculationService.ResolveOverride(_hp.Original, HpBase),
            CsOverride = TrainerCalculationService.ResolveOverride(_cs.Original, CsBase),
          }
        );
      }
      return requests;
    }

    private void ReportStatus(string message) =>
      _dispatcher.BeginInvoke(() => StatusMessage = message);

    private async Task RunGenerationAsync(Func<string, CancellationToken, Task> generate)
    {
      string beatmapPath = _effectiveBeatmapPath;
      if (string.IsNullOrEmpty(beatmapPath))
      {
        StatusMessage = "Please select a beatmap in osu!";
        return;
      }

      IsGenerating = true;
      StatusMessage = "Generating...";
      _generationCts = new System.Threading.CancellationTokenSource();
      try
      {
        await generate(beatmapPath, _generationCts.Token);
      }
      catch (OperationCanceledException)
      {
        StatusMessage = "Cancelled.";
      }
      catch (Exception ex)
      {
        StatusMessage = $"Error: {ex.Message}";
      }
      finally
      {
        IsGenerating = false;
        _generationCts?.Dispose();
        _generationCts = null;
      }
    }

    private bool _disposed;

    public void Dispose()
    {
      if (_disposed)
        return;
      _disposed = true;

      _pollTimer.Dispose();
      _generationCts?.Cancel();
      _generationCts?.Dispose();
      GC.SuppressFinalize(this);
    }
  }
}
