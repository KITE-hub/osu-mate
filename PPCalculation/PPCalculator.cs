using System.Diagnostics;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Difficulty.Skills;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using OsuMate.Models;
using OsuMate.Services.Osu;
using OsuMate.Utils;

namespace OsuMate.PPCalculation
{
  internal class PpCalculator
  {
    private Ruleset _ruleset;
    private ProcessorWorkingBeatmap _workingBeatmap;
    private int _mode;
    private Task<List<TimedDifficultyAttributes>>? _timedDifficultyTask;
    private string[] _timedDifficultyMods = [];
    private long _timedPrecomputeNotBeforeMs;
    private PerformanceCalculator? _performanceCalculator;
    private MapDifficultyAttributes? _currentMapDifficultyAttributes = null;
    private MapPerformanceAttributes? _currentMapPerformanceAttributes = null;
    private int _totalHitObjectCount;
    private StrainList? _cachedStrainList = null;
    private IBeatmap? _cachedPlayableBeatmap = null;
    private string[] _cachedPlayableBeatmapMods = [];
    private Mod[] _cachedResolvedMods = [];
    private string[] _cachedResolvedModsKey = [];
    private int _lastDifficultyIndex;
    private StaticMapInfo? _cachedStaticMapInfo;
    private const int StrainSectionLengthMs = 400;
    private const int TimedPrecomputeDelayMs = 800;

    internal PpCalculator(string file, int mode)
    {
      _mode = mode;
      _ruleset = RulesetHelper.GetRuleset(mode);
      _workingBeatmap = ProcessorWorkingBeatmap.FromFile(file);
    }

    private sealed record StaticMapInfo(
      string[] Mods,
      Dictionary<HitResult, int> StatisticsSs,
      Dictionary<HitResult, double> ModifiedHitWindows,
      Lazy<int> TotalStrainCount,
      double SpeedMultiplier,
      double BpmMinimum,
      double BpmMaximum,
      int MaxCombo,
      int PredictionTotal,
      int FirstObjectTimeModified,
      int LastObjectTimeModified
    );

    internal void SetMap(string file, int givenmode)
    {
      var newRuleset = RulesetHelper.GetRuleset(givenmode);
      var newWorkingBeatmap = ProcessorWorkingBeatmap.FromFile(file);

      _ruleset = newRuleset;
      _mode = givenmode;
      _workingBeatmap = newWorkingBeatmap;

      InvalidateCaches();
    }

    internal void SetMode(int givenmode)
    {
      _ruleset = RulesetHelper.GetRuleset(givenmode);
      _mode = givenmode;

      InvalidateCaches();
    }

    private void InvalidateCaches()
    {
      _timedDifficultyTask = null;
      _timedDifficultyMods = [];
      _timedPrecomputeNotBeforeMs = Environment.TickCount64 + TimedPrecomputeDelayMs;
      _performanceCalculator = null;
      _currentMapDifficultyAttributes = null;
      _currentMapPerformanceAttributes = null;
      _cachedStrainList = null;
      _cachedPlayableBeatmap = null;
      _cachedPlayableBeatmapMods = [];
      _cachedResolvedMods = [];
      _cachedResolvedModsKey = [];
      _cachedStaticMapInfo = null;
      _lastDifficultyIndex = 0;
    }

    private StaticMapInfo GetStaticMapInfo(string[] modAcronyms, IBeatmap beatmap, Mod[] mods)
    {
      if (_cachedStaticMapInfo != null && RulesetHelper.ModsEqual(_cachedStaticMapInfo.Mods, modAcronyms))
        return _cachedStaticMapInfo;

      string[] modKey = [.. modAcronyms];
      double speedMultiplier = RulesetHelper.GetTimeScale(modKey);
      var info = new StaticMapInfo(
        modKey,
        HitResultHelper.GenerateHitResultsForSs(beatmap, _mode),
        TimingHelper.GetModifiedHitWindows(_mode, beatmap.Difficulty.OverallDifficulty, mods),
        new Lazy<int>(() =>
        {
          var strainsData = GetStrainLists(modKey);
          return strainsData.Strains.Count > 0 ? strainsData.Strains.Max(l => l.Length) : 0;
        }),
        speedMultiplier,
        beatmap.ControlPointInfo.BPMMinimum / speedMultiplier,
        beatmap.ControlPointInfo.BPMMaximum / speedMultiplier,
        ScoreHelper.GetMaxCombo(beatmap, _mode),
        HitResultHelper.GetPredictionTotal(beatmap, _mode),
        (int)(GetFirstObjectTime() * speedMultiplier),
        (int)(GetLastObjectTime() * speedMultiplier)
      );
      _cachedStaticMapInfo = info;
      return info;
    }

    private Mod[] GetResolvedMods(string[] modAcronyms)
    {
      if (RulesetHelper.ModsEqual(_cachedResolvedModsKey, modAcronyms))
        return _cachedResolvedMods;

      _timedPrecomputeNotBeforeMs = Environment.TickCount64 + TimedPrecomputeDelayMs;
      _cachedResolvedMods = RulesetHelper.GetMods(_ruleset, modAcronyms);
      _cachedResolvedModsKey = modAcronyms;
      return _cachedResolvedMods;
    }

    private static TValue GetOrRecompute<TValue>(
      ref TValue? cache,
      string[] mods,
      Func<TValue, string[]> keySelector,
      Func<TValue> compute,
      Action? onInvalidate = null
    )
      where TValue : class
    {
      if (cache != null && !RulesetHelper.ModsEqual(keySelector(cache), mods))
      {
        onInvalidate?.Invoke();
        cache = null;
      }

      cache ??= compute();
      return cache;
    }

    internal BeatmapData Calculate(
      CalculateArgs args,
      bool playing,
      bool resultScreen,
      HitsResult hits,
      bool includeStrainInfo = true
    )
    {
      var mods = GetResolvedMods(args.Mods);
      var beatmap = GetCurrentPlayableBeatmap(args, mods);

      var staticInfo = GetStaticMapInfo(args.Mods, beatmap, mods);
      var statisticsSs = staticInfo.StatisticsSs;

      var difficultyAttributes = GetCurrentMapDifficultyAttributes(args, beatmap);
      var performanceAttributes = GetCurrentMapPerformanceAttributes(
        args,
        beatmap,
        difficultyAttributes
      );

      if (playing || Environment.TickCount64 >= _timedPrecomputeNotBeforeMs)
        EnsureTimedDifficultyTask(args.Mods);

      var data = BuildBaseBeatmapData(
        beatmap,
        mods,
        difficultyAttributes,
        performanceAttributes,
        statisticsSs,
        staticInfo,
        includeStrainInfo
      );

      var statisticsCurrent = HitResultHelper.GenerateHitResultsForCurrent(hits, _mode);
      data.HitResults = statisticsCurrent;
      data.HitResultLossMode = new Dictionary<HitResult, int>(statisticsCurrent);

      ApplyBpmInfo(data, beatmap, args.Time, staticInfo);

      if (resultScreen)
        return BuildResultScreenData(
          data,
          args,
          beatmap,
          mods,
          difficultyAttributes,
          statisticsCurrent
        );

      if (!playing)
        return data;

      return BuildPlayingData(
        data,
        args,
        beatmap,
        mods,
        hits,
        staticInfo,
        difficultyAttributes,
        statisticsCurrent
      );
    }

    private BeatmapData BuildBaseBeatmapData(
      IBeatmap beatmap,
      Mod[] mods,
      DifficultyAttributes difficultyAttributes,
      MapPerformanceAttributes performanceAttributes,
      Dictionary<HitResult, int> statisticsSs,
      StaticMapInfo staticInfo,
      bool includeStrainInfo
    )
    {
      BeatmapDifficulty beatmapDifficulty = beatmap.Difficulty;

      return new BeatmapData()
      {
        DifficultyAttributes = difficultyAttributes,
        PerformanceAttributes = performanceAttributes.PerformanceAttributes,
        CurrentDifficultyAttributes = difficultyAttributes,
        CurrentPerformanceAttributes = performanceAttributes.PerformanceAttributes,
        DifficultyAttributesIffc = difficultyAttributes,
        PerformanceAttributesIffc = performanceAttributes.PerformanceAttributes,
        PerformanceAttributesPredicted = performanceAttributes.PerformanceAttributes,
        PerformanceAttributesLossMode = performanceAttributes.PerformanceAttributes,
        FirstObjectTimeModified = staticInfo.FirstObjectTimeModified,
        LastObjectTimeModified = staticInfo.LastObjectTimeModified,
        StrainTimeModified = includeStrainInfo
          ? staticInfo.TotalStrainCount.Value * StrainSectionLengthMs
          : 0,
        IfFcHitResult = statisticsSs,
        Bpm = (0, 0, 0),
        TotalHitObjectCount = _totalHitObjectCount,
        OverallDifficulty = beatmapDifficulty.OverallDifficulty,

        ModifiedHitWindows = staticInfo.ModifiedHitWindows,
      };
    }

    private static void ApplyBpmInfo(
      BeatmapData data,
      IBeatmap beatmap,
      int? time,
      StaticMapInfo staticInfo
    )
    {
      if (time is not { } currentTime)
        return;

      var timingPoints = beatmap.ControlPointInfo.TimingPoints;
      int low = 0;
      int high = timingPoints.Count - 1;
      int found = -1;

      while (low <= high)
      {
        int middle = (low + high) >>> 1;
        if (timingPoints[middle].Time <= currentTime)
        {
          found = middle;
          low = middle + 1;
        }
        else
          high = middle - 1;
      }

      if (found < 0)
        return;

      data.Bpm = (
        timingPoints[found].BPM / staticInfo.SpeedMultiplier,
        staticInfo.BpmMinimum,
        staticInfo.BpmMaximum
      );
    }

    internal BeatmapData RefreshTimeDependentData(BeatmapData source, CalculateArgs args)
    {
      var mods = GetResolvedMods(args.Mods);
      var beatmap = GetCurrentPlayableBeatmap(args, mods);
      var staticInfo = GetStaticMapInfo(args.Mods, beatmap, mods);

      var data = source.ShallowCopy();
      ApplyBpmInfo(data, beatmap, args.Time, staticInfo);
      return data;
    }

    private PerformanceCalculator? GetPerformanceCalculator() =>
      _performanceCalculator ??= _ruleset.CreatePerformanceCalculator();

    private BeatmapData BuildResultScreenData(
      BeatmapData data,
      CalculateArgs args,
      IBeatmap beatmap,
      Mod[] mods,
      DifficultyAttributes difficultyAttributes,
      Dictionary<HitResult, int> statisticsCurrent
    )
    {
      var resultScoreInfo = new ScoreInfo(beatmap.BeatmapInfo, _ruleset.RulesetInfo)
      {
        Accuracy = args.Accuracy / 100,
        MaxCombo = args.Combo,
        Statistics = statisticsCurrent,
        Mods = mods,
        TotalScore = args.Score,
      };
      var performanceCalculator = GetPerformanceCalculator();
      var performanceAttributesResult = performanceCalculator?.Calculate(
        resultScoreInfo,
        difficultyAttributes
      );
      data.CurrentPerformanceAttributes = performanceAttributesResult;

      data.PerformanceAttributesLossMode = performanceAttributesResult;
      data.PerformanceAttributesPredicted = performanceAttributesResult;
      data.HitResultPredicted = statisticsCurrent;

      return data;
    }

    private BeatmapData BuildPlayingData(
      BeatmapData data,
      CalculateArgs args,
      IBeatmap beatmap,
      Mod[] mods,
      HitsResult hits,
      StaticMapInfo staticInfo,
      DifficultyAttributes difficultyAttributes,
      Dictionary<HitResult, int> statisticsCurrent
    )
    {
      var performanceCalculator = GetPerformanceCalculator();
      if (performanceCalculator == null)
      {
        LogUtils.DebugLogger("PerformanceCalculator is null, returning empty BeatmapData.");
        return data;
      }

      var difficultyAttributesCurrent = GetCurrentDifficultyAttributes(args, args.Time);

      var currentScoreInfo = new ScoreInfo(beatmap.BeatmapInfo, _ruleset.RulesetInfo)
      {
        Accuracy = ScoreHelper.GetAccuracy(statisticsCurrent, _mode),
        MaxCombo = args.Combo,
        Statistics = statisticsCurrent,
        Mods = mods,
        TotalScore = args.Score,
      };
      var performanceAttributesCurrent = performanceCalculator.Calculate(
        currentScoreInfo,
        difficultyAttributesCurrent
      );

      data.CurrentDifficultyAttributes = difficultyAttributesCurrent;
      data.CurrentPerformanceAttributes = performanceAttributesCurrent;

      if (_mode is OsuMode.Taiko or OsuMode.Mania)
        ApplyLossModeData(
          data,
          args,
          beatmap,
          mods,
          hits,
          staticInfo.StatisticsSs,
          difficultyAttributes,
          performanceCalculator
        );

      ApplyPredictedData(
        data,
        args,
        beatmap,
        mods,
        hits,
        staticInfo,
        difficultyAttributes,
        performanceCalculator
      );

      return data;
    }

    private void ApplyLossModeData(
      BeatmapData data,
      CalculateArgs args,
      IBeatmap beatmap,
      Mod[] mods,
      HitsResult hits,
      Dictionary<HitResult, int> statisticsSs,
      DifficultyAttributes difficultyAttributes,
      PerformanceCalculator performanceCalculator
    )
    {
      var statisticsLoss = HitResultHelper.GenerateHitResultsForLossMode(statisticsSs, hits, _mode);
      data.HitResultLossMode = statisticsLoss;

      var lossScoreInfo = new ScoreInfo(beatmap.BeatmapInfo, _ruleset.RulesetInfo)
      {
        Accuracy = ScoreHelper.GetAccuracy(statisticsLoss, _mode),
        MaxCombo = args.Combo,
        Statistics = statisticsLoss,
        Mods = mods,
        TotalScore = args.Score,
      };

      data.PerformanceAttributesLossMode = performanceCalculator.Calculate(
        lossScoreInfo,
        difficultyAttributes
      );
    }

    private void ApplyPredictedData(
      BeatmapData data,
      CalculateArgs args,
      IBeatmap beatmap,
      Mod[] mods,
      HitsResult hits,
      StaticMapInfo staticInfo,
      DifficultyAttributes difficultyAttributes,
      PerformanceCalculator performanceCalculator
    )
    {
      var statisticsForPredicted = HitResultHelper.GenerateHitResultsForPredicted(
        staticInfo.PredictionTotal,
        hits,
        _mode
      );
      data.HitResultPredicted = statisticsForPredicted;

      var predictedScoreInfo = new ScoreInfo(beatmap.BeatmapInfo, _ruleset.RulesetInfo)
      {
        Accuracy = ScoreHelper.GetAccuracy(statisticsForPredicted, _mode),
        MaxCombo = staticInfo.MaxCombo,
        Statistics = statisticsForPredicted,
        Mods = mods,
        TotalScore = args.Score,
      };

      data.PerformanceAttributesPredicted = performanceCalculator.Calculate(
        predictedScoreInfo,
        difficultyAttributes
      );
    }

    private IBeatmap GetCurrentPlayableBeatmap(CalculateArgs args, Mod[] mods)
    {
      if (
        _cachedPlayableBeatmap != null
        && RulesetHelper.ModsEqual(_cachedPlayableBeatmapMods, args.Mods)
      )
        return _cachedPlayableBeatmap;

      _cachedPlayableBeatmap = _workingBeatmap.GetPlayableBeatmap(_ruleset.RulesetInfo, mods);
      _cachedPlayableBeatmapMods = args.Mods;
      return _cachedPlayableBeatmap;
    }

    private DifficultyAttributes GetCurrentDifficultyAttributes(CalculateArgs args, int? time)
    {
      time ??= 0;
      var timedAttributes = EnsureTimedDifficultyTask(args.Mods).GetAwaiter().GetResult();
      if (timedAttributes.Count == 0)
        return new DifficultyAttributes();

      _lastDifficultyIndex = FindDifficultyIndex(timedAttributes, _lastDifficultyIndex, time.Value);

      return timedAttributes[_lastDifficultyIndex].Attributes;
    }

    private static int FindDifficultyIndex(
      List<TimedDifficultyAttributes> timedAttributes,
      int hintIndex,
      int time
    )
    {
      int lastIndex = timedAttributes.Count - 1;
      int index = Math.Clamp(hintIndex, 0, lastIndex);

      bool atOrAfterCurrent = index == 0 || timedAttributes[index].Time <= time;
      bool beforeNext = index == lastIndex || timedAttributes[index + 1].Time > time;
      if (atOrAfterCurrent && beforeNext)
        return index;

      int low = 0;
      int high = lastIndex;
      int found = 0;

      while (low <= high)
      {
        int middle = (low + high) >>> 1;
        if (timedAttributes[middle].Time <= time)
        {
          found = middle;
          low = middle + 1;
        }
        else
          high = middle - 1;
      }

      return found;
    }

    private DifficultyAttributes GetCurrentMapDifficultyAttributes(
      CalculateArgs args,
      IBeatmap beatmap
    )
    {
      var mapDifficultyAttributes = GetOrRecompute(
        ref _currentMapDifficultyAttributes,
        args.Mods,
        v => v.Mods,
        () => CalculateMapDifficultyAttributes(args, beatmap),
        InvalidateDifficultyIndexCache
      );
      return mapDifficultyAttributes.DifficultyAttributes;
    }

    private void InvalidateDifficultyIndexCache()
    {
      LogUtils.DebugLogger("Mods changed, recalculating Map DifficultyAttributes...");
      _timedDifficultyTask = null;
      _lastDifficultyIndex = 0;
    }

    private Task<List<TimedDifficultyAttributes>> EnsureTimedDifficultyTask(string[] mods)
    {
      if (_timedDifficultyTask != null && RulesetHelper.ModsEqual(_timedDifficultyMods, mods))
        return _timedDifficultyTask;

      var ruleset = _ruleset;
      var workingBeatmap = _workingBeatmap;
      var resolvedMods = RulesetHelper.GetMods(ruleset, mods);

      _timedDifficultyMods = mods;
      _timedDifficultyTask = Task.Run(() =>
        CalculateAllTimedDifficulties(ruleset, workingBeatmap, resolvedMods)
      );
      return _timedDifficultyTask;
    }

    private static List<TimedDifficultyAttributes> CalculateAllTimedDifficulties(
      Ruleset ruleset,
      ProcessorWorkingBeatmap workingBeatmap,
      Mod[] mods
    )
    {
      LogUtils.DebugLogger($"Calculating All DifficultyAttributes...");
      var startTimestamp = Stopwatch.GetTimestamp();

      var difficultyCalculator = ruleset.CreateDifficultyCalculator(workingBeatmap);
      var difficultyAttributes = difficultyCalculator.CalculateTimed(mods);

      var elapsed = Stopwatch.GetElapsedTime(startTimestamp);
      LogUtils.DebugLogger(
        $"Calculated All DifficultyAttributes! (Total Time: "
          + elapsed.TotalMilliseconds
          + " milliseconds)"
      );

      return difficultyAttributes;
    }

    private MapDifficultyAttributes CalculateMapDifficultyAttributes(
      CalculateArgs args,
      IBeatmap beatmap
    )
    {
      LogUtils.DebugLogger("Calculating Map DifficultyAttributes...");

      var difficultyAttributes =
        GetStrainLists(args.Mods).DifficultyAttributes ?? CalculateStandaloneDifficulty(args);

      _totalHitObjectCount = ScoreHelper.CountTotalHitObjects(beatmap, _mode);
      LogUtils.DebugLogger("Total HitObject Count: " + _totalHitObjectCount);

      return new MapDifficultyAttributes
      {
        Mods = args.Mods,
        DifficultyAttributes = difficultyAttributes,
      };
    }

    private DifficultyAttributes CalculateStandaloneDifficulty(CalculateArgs args)
    {
      var startTimestamp = Stopwatch.GetTimestamp();

      var mods = RulesetHelper.GetMods(_ruleset, args);
      var difficultyCalculator = _ruleset.CreateDifficultyCalculator(_workingBeatmap);
      var difficultyAttributes = difficultyCalculator.Calculate(mods);

      var elapsed = Stopwatch.GetElapsedTime(startTimestamp);
      LogUtils.DebugLogger(
        "Calculated Map DifficultyAttributes! (Total Time: "
          + elapsed.TotalMilliseconds
          + " milliseconds)"
      );

      return difficultyAttributes;
    }

    private MapPerformanceAttributes GetCurrentMapPerformanceAttributes(
      CalculateArgs args,
      IBeatmap beatmap,
      DifficultyAttributes difficultyAttributes
    )
    {
      return GetOrRecompute(
        ref _currentMapPerformanceAttributes,
        args.Mods,
        v => v.Mods,
        () => CalculateMapPerformanceAttributes(args, beatmap, difficultyAttributes),
        () => LogUtils.DebugLogger("Mods changed, recalculating Map PerformanceAttributes...")
      );
    }

    private MapPerformanceAttributes CalculateMapPerformanceAttributes(
      CalculateArgs args,
      IBeatmap beatmap,
      DifficultyAttributes difficultyAttributes
    )
    {
      LogUtils.DebugLogger("Calculating Map PerformanceAttributes...");
      var startTimestamp = Stopwatch.GetTimestamp();

      var mods = RulesetHelper.GetMods(_ruleset, args);
      var scoreInfo = new ScoreInfo(beatmap.BeatmapInfo, _ruleset.RulesetInfo)
      {
        Accuracy = 1,
        MaxCombo = ScoreHelper.GetMaxCombo(beatmap, _mode),
        Statistics = HitResultHelper.GenerateHitResultsForSs(beatmap, _mode),
        Mods = mods,
      };

      var performanceCalculator = _ruleset.CreatePerformanceCalculator();
      var performanceAttributes = performanceCalculator?.Calculate(scoreInfo, difficultyAttributes);

      var elapsed = Stopwatch.GetElapsedTime(startTimestamp);
      LogUtils.DebugLogger(
        "Calculated Map PerformanceAttributes! (Total Time: "
          + elapsed.TotalMilliseconds
          + " milliseconds)"
      );

      return new MapPerformanceAttributes
      {
        Mods = args.Mods,
        PerformanceAttributes = performanceAttributes,
      };
    }

    internal StrainList GetStrainLists(string[] mods)
    {
      try
      {
        return GetOrRecompute(
          ref _cachedStrainList,
          mods,
          v => v.Mods,
          () => ComputeStrainList(mods)
        );
      }
      catch (Exception e)
      {
        LogUtils.DebugLogger("Error getting strain lists: " + e, true);
        var fallback = new StrainList { Mods = mods };
        _cachedStrainList = fallback;
        return fallback;
      }
    }

    private StrainList ComputeStrainList(string[] mods)
    {
      double speedMultiplier = RulesetHelper.GetTimeScale(mods);
      var resolvedMods = RulesetHelper.GetMods(_ruleset, mods);
      var difficultyCalculator = RulesetHelper.GetExtendedDifficultyCalculator(
        _ruleset.RulesetInfo,
        _workingBeatmap
      );
      var difficultyAttributes = difficultyCalculator.Calculate(resolvedMods);

      if (difficultyCalculator is not IExtendedDifficultyCalculator extendedDifficultyCalculator)
        return new StrainList { Mods = mods, DifficultyAttributes = difficultyAttributes };

      var skills = extendedDifficultyCalculator
        .GetSkills()
        .Where(skill =>
          skill is not osu.Game.Rulesets.Osu.Difficulty.Skills.Aim aim || aim.IncludeSliders
        )
        .ToArray();

      List<float[]> strainLists = [];

      foreach (var skill in skills)
      {
        double[] strains = skill switch
        {
          StrainSkill strainSkill => [.. strainSkill.GetCurrentStrainPeaks()],
          _ => BuildTimeBinnedPeaks(skill.GetObjectDifficulties(), speedMultiplier),
        };

        var skillStrainList = new List<float>();

        for (int i = 0; i < strains.Length; i++)
        {
          double strain = strains[i];
          skillStrainList.Add((float)strain);
        }

        strainLists.Add([.. skillStrainList]);
      }

      return new StrainList
      {
        Strains = strainLists,
        SkillNames = [.. skills.Select(skill => skill.GetType().Name)],
        Mods = mods,
        DifficultyAttributes = difficultyAttributes,
      };
    }

    private double[] BuildTimeBinnedPeaks(
      IReadOnlyList<double> objectDifficulties,
      double speedMultiplier,
      int sectionLength = StrainSectionLengthMs
    )
    {
      var hitObjects = _workingBeatmap.Beatmap.HitObjects;
      if (objectDifficulties.Count != hitObjects.Count - 1)
      {
        LogUtils.DebugLogger(
          $"PpCalculator.BuildTimeBinnedPeaks: difficulty object count {objectDifficulties.Count} != hit objects {hitObjects.Count} - 1 (mode {_mode})"
        );
      }

      int count = Math.Min(objectDifficulties.Count, hitObjects.Count - 1);
      if (count <= 0)
        return [];

      double firstTime = hitObjects[1].StartTime * speedMultiplier;

      int BucketIndexOf(int objectIndex) =>
        (int)((hitObjects[objectIndex + 1].StartTime * speedMultiplier - firstTime) / sectionLength);

      int maxBucketIndex = -1;
      for (int i = 0; i < count; i++)
        maxBucketIndex = Math.Max(maxBucketIndex, BucketIndexOf(i));

      var buckets = new double[maxBucketIndex + 1];

      for (int i = 0; i < count; i++)
      {
        int bucketIndex = BucketIndexOf(i);
        double value = objectDifficulties[i];
        if (value > buckets[bucketIndex])
          buckets[bucketIndex] = value;
      }

      return buckets;
    }

    internal int GetFirstObjectTime()
    {
      var firstObject =
        _workingBeatmap.Beatmap.HitObjects.Count > 1 ? _workingBeatmap.Beatmap.HitObjects[1] : null;
      return (int)(firstObject?.StartTime ?? 0);
    }

    internal int GetLastObjectTime()
    {
      var lastObject =
        _workingBeatmap.Beatmap.HitObjects.Count > 0 ? _workingBeatmap.Beatmap.HitObjects[^1] : null;
      return (int)(lastObject?.GetEndTime() ?? 0);
    }
  }
}
