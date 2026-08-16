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
  internal class PpCalculator(string file, int mode)
  {
    private Ruleset ruleset = RulesetHelper.GetRuleset(mode);
    private ProcessorWorkingBeatmap workingBeatmap = ProcessorWorkingBeatmap.FromFile(file);
    private List<TimedDifficultyAttributes>? currentDifficultyAttributes = null;
    private MapDifficultyAttributes? currentMapDifficultyAttributes = null;
    private MapPerformanceAttributes? currentMapPerformanceAttributes = null;
    private int totalHitObjectCount;
    private StrainList? _cachedStrainList = null;
    private IBeatmap? _cachedPlayableBeatmap = null;
    private string[] _cachedPlayableBeatmapMods = [];
    private Mod[] _cachedResolvedMods = [];
    private string[] _cachedResolvedModsKey = [];
    private int _lastDifficultyIndex;
    private const int StrainSectionLengthMs = 400;

    internal void SetMap(string file, int givenmode)
    {
      ruleset = RulesetHelper.GetRuleset(givenmode);
      mode = givenmode;

      workingBeatmap = ProcessorWorkingBeatmap.FromFile(file);

      InvalidateCaches();
    }

    internal void SetMode(int givenmode)
    {
      ruleset = RulesetHelper.GetRuleset(givenmode);
      mode = givenmode;

      InvalidateCaches();
    }

    private void InvalidateCaches()
    {
      currentDifficultyAttributes = null;
      currentMapDifficultyAttributes = null;
      currentMapPerformanceAttributes = null;
      _cachedStrainList = null;
      _cachedPlayableBeatmap = null;
      _cachedPlayableBeatmapMods = [];
      _cachedResolvedMods = [];
      _cachedResolvedModsKey = [];
      _lastDifficultyIndex = 0;
    }

    private Mod[] GetResolvedMods(string[] modAcronyms)
    {
      if (_cachedResolvedModsKey.SequenceEqual(modAcronyms))
        return _cachedResolvedMods;

      _cachedResolvedMods = RulesetHelper.GetMods(ruleset, modAcronyms);
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
      if (cache != null && !keySelector(cache).SequenceEqual(mods))
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
      HitsResult hits
    )
    {
      var mods = GetResolvedMods(args.Mods);
      var beatmap = GetCurrentPlayableBeatmap(args, mods);

      double speedMultiplier = RulesetHelper.GetSpeedMultiplier(args.Mods);
      var strainsData = GetStrainLists(args.Mods);
      int totalStrainCount =
        strainsData.Strains.Count > 0 ? strainsData.Strains.Max(l => l.Length) : 0;

      var statisticsSs = HitResultHelper.GenerateHitResultsForSs(beatmap, mode);

      var difficultyAttributes = GetCurrentMapDifficultyAttributes(args, beatmap);
      var performanceAttributes = GetCurrentMapPerformanceAttributes(
        args,
        beatmap,
        difficultyAttributes
      );

      var data = BuildBaseBeatmapData(
        beatmap,
        mods,
        difficultyAttributes,
        performanceAttributes,
        statisticsSs,
        speedMultiplier,
        totalStrainCount
      );

      var statisticsCurrent = HitResultHelper.GenerateHitResultsForCurrent(hits, mode);
      data.HitResults = statisticsCurrent;
      data.HitResultLossMode = statisticsCurrent;

      ApplyBpmInfo(data, beatmap, args.Time, speedMultiplier);

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
        statisticsSs,
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
      double speedMultiplier,
      int totalStrainCount
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
        FirstObjectTimeModified = (int)(GetFirstObjectTime() * speedMultiplier),
        LastObjectTimeModified = (int)(GetLastObjectTime() * speedMultiplier),
        StrainTimeModified = totalStrainCount * StrainSectionLengthMs,
        IfFcHitResult = statisticsSs,
        Bpm = (0, 0, 0),
        TotalHitObjectCount = totalHitObjectCount,
        OverallDifficulty = beatmapDifficulty.OverallDifficulty,

        ModifiedHitWindows = TimingHelper.GetModifiedHitWindows(
          mode,
          beatmapDifficulty.OverallDifficulty,
          mods
        ),
      };
    }

    private static void ApplyBpmInfo(
      BeatmapData data,
      IBeatmap beatmap,
      int? time,
      double speedMultiplier
    )
    {
      var timingPoints = beatmap.ControlPointInfo.TimingPoints;
      TimingControlPoint? lastTimingPoint = null;

      foreach (var tp in timingPoints)
      {
        if (tp is not null && tp.Time <= time)
          lastTimingPoint = tp;
      }

      if (lastTimingPoint == null)
        return;

      data.Bpm = (
        lastTimingPoint.BPM / speedMultiplier,
        beatmap.ControlPointInfo.BPMMinimum / speedMultiplier,
        beatmap.ControlPointInfo.BPMMaximum / speedMultiplier
      );
    }

    private BeatmapData BuildResultScreenData(
      BeatmapData data,
      CalculateArgs args,
      IBeatmap beatmap,
      Mod[] mods,
      DifficultyAttributes difficultyAttributes,
      Dictionary<HitResult, int> statisticsCurrent
    )
    {
      var resultScoreInfo = new ScoreInfo(beatmap.BeatmapInfo, ruleset.RulesetInfo)
      {
        Accuracy = args.Accuracy / 100,
        MaxCombo = args.Combo,
        Statistics = statisticsCurrent,
        Mods = mods,
        TotalScore = args.Score,
      };
      var performanceCalculator = ruleset.CreatePerformanceCalculator();
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
      Dictionary<HitResult, int> statisticsSs,
      DifficultyAttributes difficultyAttributes,
      Dictionary<HitResult, int> statisticsCurrent
    )
    {
      var performanceCalculator = ruleset.CreatePerformanceCalculator();
      if (performanceCalculator == null)
      {
        LogUtils.DebugLogger("PerformanceCalculator is null, returning empty BeatmapData.");
        return data;
      }

      var difficultyAttributesCurrent = GetCurrentDifficultyAttributes(args, args.Time);

      var currentScoreInfo = new ScoreInfo(beatmap.BeatmapInfo, ruleset.RulesetInfo)
      {
        Accuracy = ScoreHelper.GetAccuracy(statisticsCurrent, mode),
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

      if (mode is 1 or 3)
        ApplyLossModeData(
          data,
          args,
          beatmap,
          mods,
          hits,
          statisticsSs,
          difficultyAttributes,
          performanceCalculator
        );

      ApplyPredictedData(
        data,
        args,
        beatmap,
        mods,
        hits,
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
      var statisticsLoss = HitResultHelper.GenerateHitResultsForLossMode(statisticsSs, hits, mode);
      data.HitResultLossMode = statisticsLoss;

      var lossScoreInfo = new ScoreInfo(beatmap.BeatmapInfo, ruleset.RulesetInfo)
      {
        Accuracy = ScoreHelper.GetAccuracy(statisticsLoss, mode),
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
      DifficultyAttributes difficultyAttributes,
      PerformanceCalculator performanceCalculator
    )
    {
      var statisticsForPredicted = HitResultHelper.GenerateHitResultsForPredicted(
        beatmap,
        hits,
        mode
      );
      data.HitResultPredicted = statisticsForPredicted;

      var predictedScoreInfo = new ScoreInfo(beatmap.BeatmapInfo, ruleset.RulesetInfo)
      {
        Accuracy = ScoreHelper.GetAccuracy(statisticsForPredicted, mode),
        MaxCombo = ScoreHelper.GetMaxCombo(beatmap, mode),
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
      if (_cachedPlayableBeatmap != null && _cachedPlayableBeatmapMods.SequenceEqual(args.Mods))
        return _cachedPlayableBeatmap;

      _cachedPlayableBeatmap = workingBeatmap.GetPlayableBeatmap(ruleset.RulesetInfo, mods);
      _cachedPlayableBeatmapMods = args.Mods;
      return _cachedPlayableBeatmap;
    }

    private DifficultyAttributes GetCurrentDifficultyAttributes(CalculateArgs args, int? time)
    {
      time ??= 0;
      currentDifficultyAttributes ??= CalculateAllTimedDifficulties(args);
      if (currentDifficultyAttributes.Count == 0)
        return new DifficultyAttributes();

      _lastDifficultyIndex = Math.Clamp(
        _lastDifficultyIndex,
        0,
        currentDifficultyAttributes.Count - 1
      );
      while (
        _lastDifficultyIndex > 0 && currentDifficultyAttributes[_lastDifficultyIndex].Time > time
      )
        _lastDifficultyIndex--;
      while (
        _lastDifficultyIndex < currentDifficultyAttributes.Count - 1
        && currentDifficultyAttributes[_lastDifficultyIndex + 1].Time <= time
      )
        _lastDifficultyIndex++;

      return currentDifficultyAttributes[_lastDifficultyIndex].Attributes;
    }

    private DifficultyAttributes GetCurrentMapDifficultyAttributes(
      CalculateArgs args,
      IBeatmap beatmap
    )
    {
      var mapDifficultyAttributes = GetOrRecompute(
        ref currentMapDifficultyAttributes,
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
      currentDifficultyAttributes = null;
      _lastDifficultyIndex = 0;
    }

    private List<TimedDifficultyAttributes> CalculateAllTimedDifficulties(CalculateArgs args)
    {
      LogUtils.DebugLogger($"Calculating All DifficultyAttributes...");
      var currentTime = DateTime.Now;

      var mods = RulesetHelper.GetMods(ruleset, args);
      var difficultyCalculator = ruleset.CreateDifficultyCalculator(workingBeatmap);
      var difficultyAttributes = difficultyCalculator.CalculateTimed(mods);

      var elapsed = DateTime.Now - currentTime;
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
      var currentTime = DateTime.Now;

      var mods = RulesetHelper.GetMods(ruleset, args);
      var difficultyCalculator = ruleset.CreateDifficultyCalculator(workingBeatmap);
      var difficultyAttributes = difficultyCalculator.Calculate(mods);

      var elapsed = DateTime.Now - currentTime;
      LogUtils.DebugLogger(
        "Calculated Map DifficultyAttributes! (Total Time: "
          + elapsed.TotalMilliseconds
          + " milliseconds)"
      );

      totalHitObjectCount = ScoreHelper.CountTotalHitObjects(beatmap, mode);
      LogUtils.DebugLogger("Total HitObject Count: " + totalHitObjectCount);

      return new MapDifficultyAttributes
      {
        Mods = args.Mods,
        DifficultyAttributes = difficultyAttributes,
      };
    }

    private MapPerformanceAttributes GetCurrentMapPerformanceAttributes(
      CalculateArgs args,
      IBeatmap beatmap,
      DifficultyAttributes difficultyAttributes
    )
    {
      return GetOrRecompute(
        ref currentMapPerformanceAttributes,
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
      var currentTime = DateTime.Now;

      var mods = RulesetHelper.GetMods(ruleset, args);
      var scoreInfo = new ScoreInfo(beatmap.BeatmapInfo, ruleset.RulesetInfo)
      {
        Accuracy = 1,
        MaxCombo = ScoreHelper.GetMaxCombo(beatmap, mode),
        Statistics = HitResultHelper.GenerateHitResultsForSs(beatmap, mode),
        Mods = mods,
      };

      var performanceCalculator = ruleset.CreatePerformanceCalculator();
      var performanceAttributes = performanceCalculator?.Calculate(scoreInfo, difficultyAttributes);

      var elapsed = DateTime.Now - currentTime;
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
        LogUtils.DebugLogger("Error getting strain lists: " + e.Message, true);
        return new StrainList();
      }
    }

    private StrainList ComputeStrainList(string[] mods)
    {
      double speedMultiplier = RulesetHelper.GetSpeedMultiplier(mods);
      var resolvedMods = RulesetHelper.GetMods(ruleset, mods);
      var difficultyCalculator = RulesetHelper.GetExtendedDifficultyCalculator(
        ruleset.RulesetInfo,
        workingBeatmap
      );
      difficultyCalculator.Calculate(resolvedMods);

      if (difficultyCalculator is not IExtendedDifficultyCalculator extendedDifficultyCalculator)
        return new StrainList { Mods = mods };

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
      };
    }

    private double[] BuildTimeBinnedPeaks(
      IReadOnlyList<double> objectDifficulties,
      double speedMultiplier,
      int sectionLength = StrainSectionLengthMs
    )
    {
      var hitObjects = workingBeatmap.Beatmap.HitObjects;
      int count = Math.Min(objectDifficulties.Count, hitObjects.Count - 1);
      if (count <= 0)
        return [];

      double firstTime = hitObjects[1].StartTime * speedMultiplier;
      var buckets = new List<double>();

      for (int i = 0; i < count; i++)
      {
        int bucketIndex = (int)(
          (hitObjects[i + 1].StartTime * speedMultiplier - firstTime) / sectionLength
        );

        while (buckets.Count <= bucketIndex)
          buckets.Add(0);

        double value = objectDifficulties[i];
        if (value > buckets[bucketIndex])
          buckets[bucketIndex] = value;
      }

      return [.. buckets];
    }

    internal int GetFirstObjectTime()
    {
      var firstObject =
        workingBeatmap.Beatmap.HitObjects.Count > 1 ? workingBeatmap.Beatmap.HitObjects[1] : null;
      return (int)(firstObject?.StartTime ?? 0);
    }

    internal int GetLastObjectTime()
    {
      var lastObject =
        workingBeatmap.Beatmap.HitObjects.Count > 0 ? workingBeatmap.Beatmap.HitObjects[^1] : null;
      return (int)(lastObject?.GetEndTime() ?? 0);
    }
  }
}
