using System.IO;
using System.Windows.Threading;
using osu.Game.Rulesets.Scoring;
using OsuMate.Models;
using OsuMate.PPCalculation;
using OsuMate.Services.Osu;
using OsuMate.Services.PlayLog;
using OsuMate.Utils;
using OsuMemoryDataProvider;

namespace OsuMate.Services
{
  public class PpCalculationService
  {
    private readonly OsuMemoryService _memory;

    public PpCalculationService(OsuMemoryService memory)
    {
      _memory = memory;
      _memory.OnMemoryRead += SyncHitErrorStats;
    }

    private PpCalculator? _calculator;
    private string _preMapPath = string.Empty;
    private int _currentBeatmapGamemode;
    private int _currentGamemode;
    private int _preOsuGamemode;
    private string[] _prevStrainMods = [];
    private string[] _lastSpeedMultiplierMods = [];
    private HitsResult _previousHits = new();
    private string[] _lastObservedMods = [];
    private int _modsStableTickCount;

    private const int ModsStableTicksRequired = 3;

    internal string[] PrevMods { get; private set; } = [];
    internal double CurrentSpeedMultiplier { get; private set; } = 1.0;
    internal int CurrentGamemode { get; private set; }
    internal Dictionary<HitResult, double> CurrentHitWindows { get; private set; } = [];
    internal BeatmapData? LastCalculatedData { get; private set; }

    internal string CurrentBeatmapMd5 { get; private set; } = string.Empty;
    internal event Action<BeatmapData, HitsResult>? OnCalculated;
    internal event Action<BeatmapData, List<float[]>, string[], double>? OnStrainDataUpdated;

    private readonly HitErrorStatsAccumulator _hitErrorStats = new();

    internal HitErrorStatsAccumulator.Result GetHitErrorStats(double speedMultiplier) =>
      _hitErrorStats.GetCurrent(speedMultiplier);

    private void SyncHitErrorStats()
    {
      if (!_memory.IsPlaying && !_memory.IsResultScreen)
        return;

      var (hitErrorArray, hitErrorCount) = _memory.GetHitErrorsSnapshot();
      if (hitErrorCount == 0)
        return;

      _hitErrorStats.Sync(
        new ArraySegment<int>(hitErrorArray, 0, hitErrorCount),
        CurrentSpeedMultiplier
      );
    }

    private Task? _tickTask;

    internal void Start(
      Dispatcher dispatcher,
      Func<int>? intervalMsProvider = null,
      CancellationToken ct = default
    )
    {
      var getIntervalMs = intervalMsProvider ?? (() => 16);

      _tickTask = Task.Run(
        async () =>
        {
          while (!ct.IsCancellationRequested)
          {
            if (dispatcher.HasShutdownStarted)
              break;
            try
            {
              await Task.Delay(Math.Max(1, getIntervalMs()), ct).ConfigureAwait(false);
              ProcessTick(dispatcher, ct);
            }
            catch (TaskCanceledException)
            {
              break;
            }
            catch (Exception e)
            {
              LogUtils.DebugLogger(e.Message, true);
            }
          }
        },
        ct
      );
    }

    internal Task StopAsync() => _tickTask ?? Task.CompletedTask;

    private void ProcessTick(Dispatcher dispatcher, CancellationToken ct)
    {
      if (!_memory.IsOsuRunning || !_memory.IsDirectoryLoaded)
        return;

      string beatmapPath = ResolveBeatmapPath();
      if (!File.Exists(beatmapPath))
        return;

      PrevMods = ResolveStableMods();
      RefreshSpeedMultiplierIfModsChanged();

      bool strainUpdated = false;
      List<float[]> strains = [];
      string[] skillNames = [];

      if (!DetectMapChange(beatmapPath, ref strainUpdated, ref strains, ref skillNames))
        return;

      DetectGamemodeChange(ref strainUpdated, ref strains, ref skillNames);

      DetectModChange(ref strainUpdated, ref strains, ref skillNames);

      if (_calculator == null)
        return;

      var calculated = CalculatePp();
      if (calculated == null)
        return;

      NotifyUi(
        dispatcher,
        calculated.Value.Data,
        calculated.Value.Hits,
        strainUpdated,
        strains,
        skillNames,
        calculated.Value.SpeedMultiplier,
        ct
      );
    }

    private string ResolveBeatmapPath()
    {
      var beatmap = _memory.GetBaseAddressSnapshot().Beatmap;
      try
      {
        return Path.Combine(
          _memory.SongsPath,
          beatmap.FolderName?.Trim() ?? "",
          beatmap.OsuFileName?.Trim() ?? ""
        );
      }
      catch (Exception ex)
      {
        LogUtils.DebugLogger($"PpCalculationService.ResolveBeatmapPath failed: {ex.Message}", true);
        return string.Empty;
      }
    }

    private string[] ResolveCurrentMods()
    {
      var baseAddresses = _memory.GetBaseAddressSnapshot();
      return _memory.CurrentStatus switch
      {
        OsuMemoryStatus.Playing => OsuUtils.ParseMods(baseAddresses.Player.Mods.Value).Calculation,
        OsuMemoryStatus.ResultsScreen => OsuUtils
          .ParseMods(baseAddresses.ResultsScreen.Mods.Value)
          .Calculation,
        _ => OsuUtils.ParseMods(baseAddresses.GeneralData.Mods).Calculation,
      };
    }

    private string[] ResolveStableMods()
    {
      var observed = ResolveCurrentMods();

      if (observed.SequenceEqual(_lastObservedMods))
      {
        if (_modsStableTickCount < ModsStableTicksRequired)
          _modsStableTickCount++;
      }
      else
      {
        _lastObservedMods = observed;
        _modsStableTickCount = 1;
      }

      return _modsStableTickCount >= ModsStableTicksRequired ? observed : PrevMods;
    }

    private void RefreshSpeedMultiplierIfModsChanged()
    {
      if (_lastSpeedMultiplierMods.SequenceEqual(PrevMods))
        return;

      _lastSpeedMultiplierMods = PrevMods;
      CurrentSpeedMultiplier = RulesetHelper.GetSpeedMultiplier(PrevMods);
    }

    private bool DetectMapChange(
      string beatmapPath,
      ref bool strainUpdated,
      ref List<float[]> strains,
      ref string[] skillNames
    )
    {
      if (_preMapPath == beatmapPath)
        return true;

      _preMapPath = beatmapPath;
      LogUtils.DebugLogger($"Map changed: {beatmapPath}");

      int gamemode = OsuUtils.GetMapMode(beatmapPath);
      if (gamemode is -1 or not (0 or 1 or 2 or 3))
        return false;

      _currentBeatmapGamemode = gamemode;
      _currentGamemode =
        _currentBeatmapGamemode == 0 ? _memory.CurrentOsuGamemode : _currentBeatmapGamemode;
      CurrentGamemode = _currentGamemode;
      CurrentBeatmapMd5 = BeatmapPathResolver.ComputeMd5(beatmapPath);

      if (_calculator == null)
        _calculator = new PpCalculator(beatmapPath, _currentGamemode);
      else
        _calculator.SetMap(beatmapPath, _currentGamemode);

      RefreshStrainData(ref strainUpdated, ref strains, ref skillNames);
      return true;
    }

    private void DetectGamemodeChange(
      ref bool strainUpdated,
      ref List<float[]> strains,
      ref string[] skillNames
    )
    {
      if (_memory.CurrentOsuGamemode == _preOsuGamemode)
        return;

      if (_calculator != null && _currentBeatmapGamemode == 0)
      {
        _calculator.SetMode(_memory.CurrentOsuGamemode);
        _currentGamemode = _memory.CurrentOsuGamemode;
        CurrentGamemode = _currentGamemode;
        RefreshStrainData(ref strainUpdated, ref strains, ref skillNames);
      }
      _preOsuGamemode = _memory.CurrentOsuGamemode;
    }

    private void DetectModChange(
      ref bool strainUpdated,
      ref List<float[]> strains,
      ref string[] skillNames
    )
    {
      if (strainUpdated || _calculator == null || PrevMods.SequenceEqual(_prevStrainMods))
        return;
      RefreshStrainData(ref strainUpdated, ref strains, ref skillNames);
    }

    private void RefreshStrainData(
      ref bool strainUpdated,
      ref List<float[]> strains,
      ref string[] skillNames
    )
    {
      var strainsData = _calculator!.GetStrainLists(PrevMods);
      strains = strainsData.Strains;
      skillNames = strainsData.SkillNames;
      strainUpdated = true;
      _prevStrainMods = PrevMods;
    }

    private (BeatmapData Data, HitsResult Hits, double SpeedMultiplier)? CalculatePp()
    {
      var (hits, accuracy) = _memory.ReadHitsAndAccuracy();

      if (hits.Equals(_previousHits) && _memory.IsPlaying && !hits.IsEmpty())
        return null;
      if (_memory.IsPlaying)
        _previousHits = hits.Clone();

      var args = new CalculateArgs
      {
        Mods = PrevMods,
        Time = _memory.GetBaseAddressSnapshot().GeneralData.AudioTime,
        Combo = hits.Combo,
        Score = hits.Score,
        Accuracy = accuracy,
      };

      var data = _calculator!.Calculate(args, _memory.IsPlaying, _memory.IsResultScreen, hits);
      _memory.FirstObjectTimeModified = data.FirstObjectTimeModified;
      CurrentHitWindows = data.ModifiedHitWindows;

      double speedMultiplier = CurrentSpeedMultiplier;

      var hitErrorStats = _hitErrorStats.GetCurrent(speedMultiplier);
      data.DetailedOffset = (
        hitErrorStats.RawAvg,
        hitErrorStats.ModifiedAvg,
        hitErrorStats.ModifiedStdev,
        hitErrorStats.RobustAvg
      );
      data.UR = (hitErrorStats.RawUR, hitErrorStats.ModifiedUR);

      LastCalculatedData = data;

      return (data, hits, speedMultiplier);
    }

    private void NotifyUi(
      Dispatcher dispatcher,
      BeatmapData data,
      HitsResult hits,
      bool strainUpdated,
      List<float[]> strains,
      string[] skillNames,
      double speedMultiplier,
      CancellationToken ct
    )
    {
      if (dispatcher.HasShutdownStarted || ct.IsCancellationRequested)
        return;

      dispatcher.BeginInvoke(() =>
      {
        OnCalculated?.Invoke(data, hits);
        if (strainUpdated)
          OnStrainDataUpdated?.Invoke(data, strains, skillNames, speedMultiplier);
      });
    }
  }
}
