using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using osu.Game.Rulesets.Scoring;
using OsuMate.Models;
using OsuMate.Services;
using OsuMate.Services.Osu;
using OsuMate.Services.PlayLog;

namespace OsuMate.ViewModels
{
  public class MainViewModel
  {
    public ThemeViewModel Theme { get; } = new();
    public InfoViewModel Info { get; } = new();
    public StrainGraphViewModel StrainGraph { get; }
    public URTimeGraphViewModel URTimeGraph { get; }
    public URDistGraphViewModel URDistGraph { get; }
    public URBarViewModel URBar { get; } = new();
    public InGameOverlayViewModel InGameOverlay { get; } = new();

    public event Action<bool>? IsPlayingChanged;
    public event Action<IntPtr>? OnOsuWindowFound;

    private readonly OsuMemoryService _memory;
    public bool IsPlaying => _memory.IsPlaying;
    private readonly PpCalculationService _ppService;
    private readonly SettingsViewModel _settings;
    private readonly PlayLogService _playLogService;
    private List<int> _enabledOverlayIds = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13];
    private bool _previousIsPlaying = false;
    private bool _previousIsResultScreen = false;

    private readonly BestPpTracker _bestPpTracker;

    private readonly CancellationTokenSource _cts = new();

    private readonly Dispatcher _primaryDispatcher;
    private Dispatcher? _uiDispatcher;

    public void AttachUiDispatcher(Dispatcher dispatcher)
    {
      _uiDispatcher = dispatcher;
    }

    public void SetOverlayFontSize(double fontSize)
    {
      InGameOverlay.FontSize = fontSize;
    }

    public void SetOverlayShowValueFirst(bool isShowValueFirst)
    {
      InGameOverlay.IsShowValueFirst = isShowValueFirst;
    }

    public void SetEnabledOverlayIds(List<int> ids)
    {
      _enabledOverlayIds = ids;
    }

    public MainViewModel(
      OsuMemoryService memory,
      PpCalculationService ppService,
      SettingsViewModel settings,
      PlayLogService playLogService,
      Dispatcher primaryDispatcher
    )
    {
      _memory = memory;
      _ppService = ppService;
      _settings = settings;
      _playLogService = playLogService;
      _primaryDispatcher = primaryDispatcher;
      _ppService.OnCalculated += UpdateUI;
      _memory.OnMemoryRead += UpdateFastUI;
      _memory.OnOsuWindowFound += handle => OnOsuWindowFound?.Invoke(handle);
      StrainGraph = new(Theme.Current);
      URTimeGraph = new(Theme.Current);
      URDistGraph = new(Theme.Current);
      _ppService.OnStrainDataUpdated += (data, strains, labels, speed) =>
      {
        _uiDispatcher?.BeginInvoke(() =>
        {
          StrainGraph.SetData(
            strains,
            labels,
            data.StrainTimeModified,
            data.FirstObjectTimeModified,
            speed
          );
          URTimeGraph.SetData(data.ModifiedHitWindows, data.StrainTimeModified / 1000.0);
        });
      };

      _bestPpTracker = new BestPpTracker(_playLogService, _settings.TargetPlayerNames);
      _bestPpTracker.BestPpChanged += OnBestPpChanged;
    }

    private void OnBestPpChanged(double? bestPp)
    {
      void Apply() => Info.UpdateBestPp(bestPp);

      var dispatcher = _uiDispatcher;
      if (dispatcher == null || dispatcher.CheckAccess())
        Apply();
      else
        dispatcher.BeginInvoke(Apply);
    }

    private IEnumerable<IThemeable> Themeables => [StrainGraph, URTimeGraph, URDistGraph];

    public void OnThemeChanged()
    {
      foreach (var t in Themeables)
        t.ApplyTheme(Theme.Current);
    }

    public void Start()
    {
      _memory.StartProcessMonitor(_cts.Token);

      _memory.StartMemoryReader(() => _settings.DataUpdateIntervalMs, _cts.Token);
      _ppService.Start(_primaryDispatcher, () => _settings.DataUpdateIntervalMs, _cts.Token);
    }

    public async Task StopAsync()
    {
      _cts.Cancel();
      await Task.WhenAll(_memory.StopAsync(), _ppService.StopAsync());
    }

    private readonly List<(double timeSec, double offsetMs)> _urTimelineAccumulated = [];

    private void UpdateUI(BeatmapData data, HitsResult hits)
    {
      if (_memory.IsPlaying != _previousIsPlaying)
      {
        _previousIsPlaying = _memory.IsPlaying;
        IsPlayingChanged?.Invoke(_memory.IsPlaying);
      }

      _bestPpTracker.RefreshIfChanged(_ppService.CurrentBeatmapMd5);

      var baseAddresses = _memory.GetBaseAddressSnapshot();

      double accuracy = _memory.ReadHitsAndAccuracy().Accuracy;

      bool isPlaying = _memory.IsPlaying;
      bool isResultScreen = _memory.IsResultScreen;
      int gamemode = _ppService.CurrentGamemode;
      double speedMultiplier = _ppService.CurrentSpeedMultiplier;
      double? bestPp = _bestPpTracker.CachedBestPp;
      int dataUpdateIntervalMs = _settings.DataUpdateIntervalMs;

      InGameOverlay.Update(
        data,
        hits,
        accuracy,
        gamemode,
        _enabledOverlayIds,
        baseAddresses.GeneralData.AudioTime,
        speedMultiplier,
        bestPp
      );

      var (urReset, urNewItems) = _memory.GetURTimelineSnapshot();

      double currentTimeSec =
        (baseAddresses.GeneralData.AudioTime * speedMultiplier - data.FirstObjectTimeModified)
        / 1000.0;

      var uiDispatcher = _uiDispatcher;
      if (uiDispatcher == null)
        return;

      uiDispatcher.BeginInvoke(() =>
      {
        Info.UpdateMapInfo(data, isPlaying, baseAddresses.GeneralData.AudioTime, speedMultiplier);
        Info.UpdatePp(data, isPlaying, isResultScreen);
        Info.UpdateJudge(data, hits, gamemode);
        StrainGraph.Update(baseAddresses.GeneralData.AudioTime, dataUpdateIntervalMs);

        if (urReset)
          _urTimelineAccumulated.Clear();
        _urTimelineAccumulated.AddRange(urNewItems);

        URTimeGraph.Update(_urTimelineAccumulated, currentTimeSec, isPlaying, dataUpdateIntervalMs);
        URDistGraph.Update(
          _urTimelineAccumulated,
          data.ModifiedHitWindows,
          isPlaying,
          dataUpdateIntervalMs
        );
      });
    }

    private int _fastUiDispatchPending;
    private int _fastInfoDispatchPending;

    private readonly ModifiedHitErrorCache _modifiedHitErrorCache = new();

    private void UpdateFastUI()
    {
      if (!_memory.IsPlaying && !_memory.IsResultScreen)
        return;

      var (hitErrorArray, hitErrorCount) = _memory.GetHitErrorsSnapshot();
      var hitErrors = new ArraySegment<int>(hitErrorArray, 0, hitErrorCount);
      double speedMultiplier = _ppService.CurrentSpeedMultiplier;

      _memory.SyncURTimeline(hitErrors, speedMultiplier);

      bool enteredResultScreen = _memory.IsResultScreen && !_previousIsResultScreen;
      _previousIsResultScreen = _memory.IsResultScreen;

      var (hits, accuracy) = _memory.ReadHitsAndAccuracy();

      int gamemode = _ppService.CurrentGamemode;
      bool isPlaying = _memory.IsPlaying;

      HitErrorStatsAccumulator.Result? stats =
        hitErrors.Count > 0 ? _ppService.GetHitErrorStats(speedMultiplier) : null;

      if (Interlocked.CompareExchange(ref _fastUiDispatchPending, 1, 0) == 0)
      {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
          try
          {
            var (hitErrorsModifiedInt, hitErrorsModifiedTotalCount) = _modifiedHitErrorCache.Sync(
              hitErrors,
              speedMultiplier
            );

            URBar.Update(
              hitErrorsModifiedInt,
              hitErrorsModifiedTotalCount,
              _ppService.CurrentHitWindows,
              isPlaying
            );

            if (stats is { } s)
              InGameOverlay.UpdateFast(
                hits,
                accuracy,
                s.ModifiedAvg,
                s.ModifiedStdev,
                s.RawUR,
                s.ModifiedUR,
                gamemode
              );
            else
              InGameOverlay.UpdateFast(hits, accuracy, null, null, null, null, gamemode);
          }
          finally
          {
            Interlocked.Exchange(ref _fastUiDispatchPending, 0);
          }
        });
      }

      var uiDispatcher = _uiDispatcher;
      if (
        uiDispatcher != null
        && Interlocked.CompareExchange(ref _fastInfoDispatchPending, 1, 0) == 0
      )
      {
        uiDispatcher.BeginInvoke(() =>
        {
          try
          {
            Info.UpdateHits(hits);

            if (stats is { } s)
              Info.UpdateAccFast(
                accuracy,
                isPlaying,
                s.RobustAvg,
                s.ModifiedAvg,
                s.ModifiedStdev,
                s.RawUR,
                s.ModifiedUR
              );
            else
              Info.UpdateAccFast(accuracy, isPlaying, null, null, null, null, null);
          }
          finally
          {
            Interlocked.Exchange(ref _fastInfoDispatchPending, 0);
          }
        });
      }
    }
  }
}
