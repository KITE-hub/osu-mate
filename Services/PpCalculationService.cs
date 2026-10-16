using System.IO;
using System.Windows.Threading;
using osu.Game.Rulesets.Scoring;
using OsuMate.Models;
using OsuMate.PPCalculation;
using OsuMate.Services.Osu;
using OsuMate.Services.PlayLog;
using OsuMate.Services.Trainer;
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
    private const int MapRetryIntervalMs = 1000;
    private const int MissingFileRetryIntervalMs = 250;

    private string _resolvedPathFolder = string.Empty;
    private string _resolvedPathFile = string.Empty;
    private string _resolvedPathSongs = string.Empty;
    private string _resolvedPath = string.Empty;

    private string _missingBeatmapPath = string.Empty;
    private long _missingBeatmapRetryNotBeforeMs;

    private int? _parsedModsValue;
    private string[] _parsedCalculationMods = [];

    private bool _lastCalculationWasPlaying;

    private readonly record struct CalculationKey(
      bool IsPlaying,
      bool IsResultScreen,
      int OsuGamemode,
      int Gamemode,
      int AudioTime,
      double Accuracy,
      HitsResult Hits,
      string[] Mods,
      string MapPath,
      long HitErrorVersion,
      int UrTimelineVersion
    );

    private CalculationKey? _lastCalculationKey;
    private int _refreshRequested;

    private readonly object _notifyLock = new();
    private PendingNotification? _pending;

    private long _mapRetryNotBeforeMs;

    internal void RequestRefresh() => Interlocked.Exchange(ref _refreshRequested, 1);

    internal string[] PrevMods { get; private set; } = [];
    internal double CurrentSpeedMultiplier { get; private set; } = 1.0;
    internal int CurrentGamemode { get; private set; }
    internal int EffectiveGamemode =>
      _memory.IsPlaying || _memory.IsResultScreen || _preMapPath.Length == 0
        ? _memory.CurrentOsuGamemode
        : CurrentGamemode;
    internal int? CurrentManiaKeyCount { get; private set; }
    internal Dictionary<HitResult, double> CurrentHitWindows { get; private set; } = [];
    internal BeatmapData? LastCalculatedData { get; private set; }

    internal string CurrentBeatmapPath => _preMapPath;
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

      _hitErrorStats.Sync(new ArraySegment<int>(hitErrorArray, 0, hitErrorCount));
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
              LogUtils.DebugLogger(e.ToString(), true);
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
      if (beatmapPath != _preMapPath && !IsBeatmapFileAvailable(beatmapPath))
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

      var calculated = CalculatePp(strainUpdated);
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

    private bool IsBeatmapFileAvailable(string beatmapPath)
    {
      long now = Environment.TickCount64;
      if (beatmapPath == _missingBeatmapPath && now < _missingBeatmapRetryNotBeforeMs)
        return false;

      if (File.Exists(beatmapPath))
        return true;

      _missingBeatmapPath = beatmapPath;
      _missingBeatmapRetryNotBeforeMs = now + MissingFileRetryIntervalMs;
      return false;
    }

    private string ResolveBeatmapPath()
    {
      var beatmap = _memory.GetBaseAddressSnapshot().Beatmap;
      string folder = beatmap.FolderName ?? "";
      string file = beatmap.OsuFileName ?? "";
      string songsPath = _memory.SongsPath;

      if (
        folder == _resolvedPathFolder
        && file == _resolvedPathFile
        && songsPath == _resolvedPathSongs
      )
        return _resolvedPath;

      try
      {
        _resolvedPath = Path.Combine(songsPath, folder.Trim(), file.Trim());
        _resolvedPathFolder = folder;
        _resolvedPathFile = file;
        _resolvedPathSongs = songsPath;
        return _resolvedPath;
      }
      catch (Exception ex)
      {
        LogUtils.DebugLogger($"PpCalculationService.ResolveBeatmapPath failed: {ex}", true);
        return string.Empty;
      }
    }

    private string[] ParseCalculationMods(int modsValue)
    {
      if (_parsedModsValue == modsValue)
        return _parsedCalculationMods;

      _parsedCalculationMods = OsuUtils.ParseMods(modsValue).Calculation;
      _parsedModsValue = modsValue;
      return _parsedCalculationMods;
    }

    private string[] ResolveCurrentMods()
    {
      var baseAddresses = _memory.GetBaseAddressSnapshot();
      return _memory.CurrentStatus switch
      {
        OsuMemoryStatus.Playing => ParseCalculationMods(baseAddresses.Player.Mods.Value),
        OsuMemoryStatus.ResultsScreen => ParseCalculationMods(
          baseAddresses.ResultsScreen.Mods.Value
        ),
        _ => ParseCalculationMods(baseAddresses.GeneralData.Mods),
      };
    }

    private string[] ResolveStableMods()
    {
      var observed = ResolveCurrentMods();

      if (RulesetHelper.ModsEqual(observed, _lastObservedMods))
      {
        if (_modsStableTickCount < ModsStableTicksRequired)
          _modsStableTickCount++;
      }
      else
      {
        _lastObservedMods = observed;
        _modsStableTickCount = 1;
      }

      return _modsStableTickCount >= ModsStableTicksRequired ? _lastObservedMods : PrevMods;
    }

    private void RefreshSpeedMultiplierIfModsChanged()
    {
      if (RulesetHelper.ModsEqual(_lastSpeedMultiplierMods, PrevMods))
        return;

      _lastSpeedMultiplierMods = PrevMods;
      CurrentSpeedMultiplier = RulesetHelper.GetTimeScale(PrevMods);
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
      if (Environment.TickCount64 < _mapRetryNotBeforeMs)
        return false;

      int gamemode = OsuBeatmapFile.TryReadMode(beatmapPath);
      if (gamemode is not (0 or 1 or 2 or 3))
      {
        _mapRetryNotBeforeMs = Environment.TickCount64 + MapRetryIntervalMs;
        return false;
      }

      int currentGamemode = gamemode == OsuMode.Osu ? _memory.CurrentOsuGamemode : gamemode;
      try
      {
        if (_calculator == null)
          _calculator = new PpCalculator(beatmapPath, currentGamemode);
        else
          _calculator.SetMap(beatmapPath, currentGamemode);
      }
      catch (Exception e)
      {
        _mapRetryNotBeforeMs = Environment.TickCount64 + MapRetryIntervalMs;
        LogUtils.DebugLogger(
          $"PpCalculationService.DetectMapChange failed: {beatmapPath}: {e}",
          true
        );
        return false;
      }

      _preMapPath = beatmapPath;
      LogUtils.DebugLogger($"Map changed: {beatmapPath}");

      _currentBeatmapGamemode = gamemode;
      CurrentManiaKeyCount = gamemode == OsuMode.Mania
        ? BeatmapPathResolver.ReadManiaKeyCountFromFile(beatmapPath)
        : null;
      _currentGamemode = currentGamemode;
      CurrentGamemode = currentGamemode;
      CurrentBeatmapMd5 = BeatmapPathResolver.ComputeMd5(beatmapPath);

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

      if (_calculator != null && _currentBeatmapGamemode == OsuMode.Osu)
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
      if (
        strainUpdated
        || _calculator == null
        || RulesetHelper.ModsEqual(PrevMods, _prevStrainMods)
      )
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

    private (BeatmapData Data, HitsResult Hits, double SpeedMultiplier)? CalculatePp(
      bool strainUpdated
    )
    {
      var (hits, accuracy) = _memory.ReadHitsAndAccuracy();
      bool isPlaying = _memory.IsPlaying;

      int audioTime = _memory.GetBaseAddressSnapshot().GeneralData.AudioTime;

      var key = new CalculationKey(
        isPlaying,
        _memory.IsResultScreen,
        _memory.CurrentOsuGamemode,
        _currentGamemode,
        audioTime,
        accuracy,
        hits,
        PrevMods,
        _preMapPath,
        _hitErrorStats.Version,
        _memory.UrTimelineVersion
      );

      bool refreshRequested = Interlocked.Exchange(ref _refreshRequested, 0) == 1;
      if (
        !refreshRequested
        && !strainUpdated
        && _lastCalculationKey is { } lastKey
        && lastKey.Equals(key)
      )
        return null;
      _lastCalculationKey = key;

      var args = new CalculateArgs
      {
        Mods = PrevMods,
        Time = audioTime,
        Combo = hits.Combo,
        Score = hits.Score,
        Accuracy = accuracy,
      };

      if (
        !strainUpdated
        && isPlaying
        && _lastCalculationWasPlaying
        && !hits.IsEmpty()
        && hits.Equals(_previousHits)
        && LastCalculatedData is { } lastData
      )
      {
        var refreshed = _calculator!.RefreshTimeDependentData(lastData, args);
        LastCalculatedData = refreshed;
        return (refreshed, hits, CurrentSpeedMultiplier);
      }

      if (isPlaying)
        _previousHits = hits.Clone();
      _lastCalculationWasPlaying = isPlaying;

      var data = _calculator!.Calculate(args, isPlaying, _memory.IsResultScreen, hits);
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

      bool schedule;
      lock (_notifyLock)
      {
        var next = new PendingNotification(
          data,
          hits,
          strainUpdated,
          strains,
          skillNames,
          speedMultiplier
        );
        if (!strainUpdated && _pending is { StrainUpdated: true } previous)
        {
          next = next with
          {
            StrainUpdated = true,
            Strains = previous.Strains,
            SkillNames = previous.SkillNames,
            SpeedMultiplier = previous.SpeedMultiplier,
          };
        }

        schedule = _pending == null;
        _pending = next;
      }

      if (!schedule)
        return;

      dispatcher.BeginInvoke(() =>
      {
        PendingNotification? current;
        lock (_notifyLock)
        {
          current = _pending;
          _pending = null;
        }

        if (current == null)
          return;

        OnCalculated?.Invoke(current.Data, current.Hits);
        if (current.StrainUpdated)
          OnStrainDataUpdated?.Invoke(
            current.Data,
            current.Strains,
            current.SkillNames,
            current.SpeedMultiplier
          );
      });
    }

    private sealed record PendingNotification(
      BeatmapData Data,
      HitsResult Hits,
      bool StrainUpdated,
      List<float[]> Strains,
      string[] SkillNames,
      double SpeedMultiplier
    );
  }
}
