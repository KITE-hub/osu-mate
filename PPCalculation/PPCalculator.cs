using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Difficulty.Skills;
using osu.Game.Rulesets.Objects;
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
        private string[] _cachedStrainMods = [];


        internal void SetMap(string file, int givenmode)
        {
            ruleset = RulesetHelper.GetRuleset(givenmode);
            mode = givenmode;

            workingBeatmap = ProcessorWorkingBeatmap.FromFile(file);

            currentDifficultyAttributes = null;
            currentMapDifficultyAttributes = null;
            currentMapPerformanceAttributes = null;
            _cachedStrainList = null;
            _cachedStrainMods = [];
        }

        internal void SetMode(int givenmode)
        {
            ruleset = RulesetHelper.GetRuleset(givenmode);
            mode = givenmode;

            currentDifficultyAttributes = null;
            currentMapDifficultyAttributes = null;
            currentMapPerformanceAttributes = null;
            _cachedStrainList = null;
            _cachedStrainMods = [];
        }

        internal BeatmapData Calculate(CalculateArgs args, bool playing, bool resultScreen, HitsResult hits)
        {
            var mods = RulesetHelper.GetMods(ruleset, args);
            var beatmap = workingBeatmap.GetPlayableBeatmap(ruleset.RulesetInfo, mods);

            double speedMultiplier = RulesetHelper.GetSpeedMultiplier(args.Mods);
            var strainsData = GetStrainLists(args.Mods);
            int totalCount = strainsData.Strains.Count > 0 ? strainsData.Strains.Max(l => l.Length) : 0;

            var staticsSs = HitResultHelper.GenerateHitResultsForSs(beatmap, mode);

            var difficultyAttributes = GetCurrentMapDifficultyAttributes(args, beatmap);
            var performanceAttributes = GetCurrentMapPerformanceAttributes(args, beatmap, difficultyAttributes);
            BeatmapDifficulty beatmapDifficulty = beatmap.Difficulty;

            var data = new BeatmapData()
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
                strainTimeModified = (int)(totalCount * 400 * speedMultiplier),
                IfFcHitResult = staticsSs,
                ExpectedManiaScore = 0,
                Bpm = (0, 0, 0),
                TotalHitObjectCount = totalHitObjectCount,
                OverallDifficulty = beatmapDifficulty.OverallDifficulty,
                // DetailedOffset / UR はここでは計算しない。呼び出し元(PpCalculationService.Start)が
                // Slow Lane / Fast Lane共有のHitErrorStatsAccumulatorで増分計算した値を、
                // Calculate()の戻り値に対して直接上書きする。
                ModifiedHitWindows = TimingHelper.GetModifiedHitWindows(mode, beatmapDifficulty.OverallDifficulty, mods)
            };

            var statisticsCurrent = HitResultHelper.GenerateHitResultsForCurrent(hits, mode);
            data.HitResults = statisticsCurrent;
            data.HitResultLossMode = statisticsCurrent;

            var timingPoints = beatmap.ControlPointInfo.TimingPoints;
            TimingControlPoint? lastTimingPoint = null;

            foreach (var tp in timingPoints)
            {
                if (tp is not null && tp.Time <= args.Time)
                {
                    lastTimingPoint = tp;
                }
            }

            if (lastTimingPoint != null)
                data.Bpm = (lastTimingPoint.BPM / speedMultiplier, beatmap.ControlPointInfo.BPMMinimum / speedMultiplier, beatmap.ControlPointInfo.BPMMaximum / speedMultiplier);

            if (resultScreen)
            {
                var resultScoreInfo = new ScoreInfo(beatmap.BeatmapInfo, ruleset.RulesetInfo)
                {
                    Accuracy = args.Accuracy / 100,
                    MaxCombo = args.Combo,
                    Statistics = statisticsCurrent,
                    Mods = mods,
                    TotalScore = args.Score
                };
                var performanceCalculator = ruleset.CreatePerformanceCalculator();
                var performanceAttributesResult = performanceCalculator?.Calculate(resultScoreInfo, difficultyAttributes);
                data.CurrentPerformanceAttributes = performanceAttributesResult;

                // Loss mode pp / Predicted pp は「プレイ中に残りの譜面をどう叩くか」の予測値なので、
                // プレイが終わった時点ではもう「残り」が存在せず、実際の最終結果と同じになるべき。
                // ここで上書きしないと、data構築時にデフォルトとして入れていたSS pp
                // （PerformanceAttributes = 全譜面100%時のpp）がそのまま残ってしまい、
                // リザルト画面でLoss mode pp / Predicted ppだけSS ppを表示してしまう不具合になる。
                data.PerformanceAttributesLossMode = performanceAttributesResult;
                data.PerformanceAttributesPredicted = performanceAttributesResult;
                data.HitResultPredicted = statisticsCurrent;

                return data;
            }

            if (!playing) return data;
            {
                var performanceCalculator = ruleset.CreatePerformanceCalculator();
                // GetCurrentDifficultyAttributes は初回呼び出し時に CalculateAllTimedDifficulties で
                // 時刻ごとの難易度をまとめて計算しキャッシュするため、毎tickここを通っても
                // 譜面全体の難易度計算がゼロから再実行されることはない（Mods変更時はGetCurrentMapDifficultyAttributes
                // 側で既にキャッシュが破棄されている）。
                DifficultyAttributes difficultyAttributesCurrent = GetCurrentDifficultyAttributes(args, args.Time);

                var currentScoreInfo = new ScoreInfo(beatmap.BeatmapInfo, ruleset.RulesetInfo)
                {
                    Accuracy = ScoreHelper.GetAccuracy(statisticsCurrent, mode),
                    MaxCombo = args.Combo,
                    Statistics = statisticsCurrent,
                    Mods = mods,
                    TotalScore = args.Score
                };

                var performanceCalculatorCurrent = ruleset.CreatePerformanceCalculator();
                if (performanceCalculatorCurrent == null)
                {
                    LogUtils.DebugLogger("PerformanceCalculator is null, returning empty BeatmapData.");
                    return data;
                }
                var performanceAttributesCurrent = performanceCalculatorCurrent.Calculate(currentScoreInfo, difficultyAttributesCurrent);

                data.CurrentDifficultyAttributes = difficultyAttributesCurrent;
                data.CurrentPerformanceAttributes = performanceAttributesCurrent;

                // Calculation Loss Mode PP
                if (mode is 1 or 3)
                {
                    var staticsLoss = HitResultHelper.GenerateHitResultsForLossMode(staticsSs, hits, mode);
                    data.HitResultLossMode = staticsLoss;

                    var lossScoreInfo = new ScoreInfo(beatmap.BeatmapInfo, ruleset.RulesetInfo)
                    {
                        Accuracy = ScoreHelper.GetAccuracy(staticsLoss, mode),
                        MaxCombo = args.Combo,
                        Statistics = staticsLoss,
                        Mods = mods,
                        TotalScore = args.Score
                    };

                    var performanceAttributesLossMode = performanceCalculator?.Calculate(lossScoreInfo, difficultyAttributes);
                    data.PerformanceAttributesLossMode = performanceAttributesLossMode;
                    if (mode == 3) data.ExpectedManiaScore = ScoreHelper.ManiaScoreCalculator(beatmap, hits, args.Mods, args.Score);
                }

                // Calculation Predicted PP
                var staticsForPredicted = HitResultHelper.GenerateHitResultsForPredicted(beatmap, hits, mode);
                data.HitResultPredicted = staticsForPredicted;

                var predictedScoreInfo = new ScoreInfo(beatmap.BeatmapInfo, ruleset.RulesetInfo)
                {
                    Accuracy = ScoreHelper.GetAccuracy(staticsForPredicted, mode),
                    MaxCombo = ScoreHelper.GetMaxCombo(beatmap, mode),
                    Statistics = staticsForPredicted,
                    Mods = mods,
                    TotalScore = args.Score
                };

                var performanceAttributesPredicted = performanceCalculator?.Calculate(predictedScoreInfo, difficultyAttributes);
                data.PerformanceAttributesPredicted = performanceAttributesPredicted;

                return data;
            }
        }

        private DifficultyAttributes GetCurrentDifficultyAttributes(CalculateArgs args, int? time)
        {
            time ??= 0;
            currentDifficultyAttributes ??= CalculateAllTimedDifficulties(args);
            var difficultyAttributes = currentDifficultyAttributes.LastOrDefault(d => d.Time <= time);
            if (difficultyAttributes != null) return difficultyAttributes.Attributes;
            difficultyAttributes = currentDifficultyAttributes.FirstOrDefault();
            return difficultyAttributes?.Attributes ?? new DifficultyAttributes();
        }

        private DifficultyAttributes GetCurrentMapDifficultyAttributes(CalculateArgs args, IBeatmap beatmap)
        {
            if (currentMapDifficultyAttributes != null && !currentMapDifficultyAttributes.Mods.SequenceEqual(args.Mods))
            {
                LogUtils.DebugLogger("Mods changed, recalculating Map DifficultyAttributes...");
                currentMapDifficultyAttributes = null;
                currentDifficultyAttributes = null;
            }

            currentMapDifficultyAttributes ??= CalculateMapDifficultyAttributes(args, beatmap);
            return currentMapDifficultyAttributes.DifficultyAttributes;
        }

        private List<TimedDifficultyAttributes> CalculateAllTimedDifficulties(CalculateArgs args)
        {
            LogUtils.DebugLogger($"Calculating All DifficultyAttributes...");
            var currentTime = DateTime.Now;

            var mods = RulesetHelper.GetMods(ruleset, args);
            var difficultyCalculator = ruleset.CreateDifficultyCalculator(workingBeatmap);
            var difficultyAttributes = difficultyCalculator.CalculateTimed(mods);

            var elapsed = DateTime.Now - currentTime;
            LogUtils.DebugLogger($"Calculated All DifficultyAttributes! (Total Time: " + elapsed.Milliseconds + " milliseconds)");

            return difficultyAttributes;
        }

        private MapDifficultyAttributes CalculateMapDifficultyAttributes(CalculateArgs args, IBeatmap beatmap)
        {
            LogUtils.DebugLogger("Calculating Map DifficultyAttributes...");
            var currentTime = DateTime.Now;

            var mods = RulesetHelper.GetMods(ruleset, args);
            var difficultyCalculator = ruleset.CreateDifficultyCalculator(workingBeatmap);
            var difficultyAttributes = difficultyCalculator.Calculate(mods);

            var elapsed = DateTime.Now - currentTime;
            LogUtils.DebugLogger("Calculated Map DifficultyAttributes! (Total Time: " + elapsed.Milliseconds + " milliseconds)");

            totalHitObjectCount = ScoreHelper.CountTotalHitObjects(beatmap, mode);
            LogUtils.DebugLogger("Total HitObject Count: " + totalHitObjectCount);

            return new MapDifficultyAttributes
            {
                Mods = args.Mods,
                DifficultyAttributes = difficultyAttributes
            };
        }

        private MapPerformanceAttributes GetCurrentMapPerformanceAttributes(CalculateArgs args, IBeatmap beatmap, DifficultyAttributes difficultyAttributes)
        {
            if (currentMapPerformanceAttributes != null && !currentMapPerformanceAttributes.Mods.SequenceEqual(args.Mods))
            {
                LogUtils.DebugLogger("Mods changed, recalculating Map PerformanceAttributes...");
                currentMapPerformanceAttributes = null;
            }

            currentMapPerformanceAttributes ??= CalculateMapPerformanceAttributes(args, beatmap, difficultyAttributes);
            return currentMapPerformanceAttributes;
        }

        private MapPerformanceAttributes CalculateMapPerformanceAttributes(CalculateArgs args, IBeatmap beatmap, DifficultyAttributes difficultyAttributes)
        {
            LogUtils.DebugLogger("Calculating Map PerformanceAttributes...");
            var currentTime = DateTime.Now;

            var mods = RulesetHelper.GetMods(ruleset, args);
            var scoreInfo = new ScoreInfo(beatmap.BeatmapInfo, ruleset.RulesetInfo)
            {
                Accuracy = 1,
                MaxCombo = ScoreHelper.GetMaxCombo(beatmap, mode),
                Statistics = HitResultHelper.GenerateHitResultsForSs(beatmap, mode),
                Mods = mods
            };

            var performanceCalculator = ruleset.CreatePerformanceCalculator();
            var performanceAttributes = performanceCalculator?.Calculate(scoreInfo, difficultyAttributes);

            var elapsed = DateTime.Now - currentTime;
            LogUtils.DebugLogger("Calculated Map PerformanceAttributes! (Total Time: " + elapsed.Milliseconds + " milliseconds)");

            return new MapPerformanceAttributes
            {
                Mods = args.Mods,
                PerformanceAttributes = performanceAttributes
            };
        }

        // Copyright(c) 2019 ppy Pty Ltd <contact@ppy.sh>.
        // This code is borrowed from osu-tools(https://github.com/ppy/osu-tools)
        // osu-tools is licensed under the MIT License. https://github.com/ppy/osu-tools/blob/master/LICENCE
        internal StrainList GetStrainLists(string[] mods)
        {
            // この計算（譜面全体のStrainグラフ用データ）は選択中の譜面・モード・modsが変わらない限り
            // 結果が変化しないため、それらが変わるまでキャッシュを使い回す。
            // (Calculate()は毎tick呼ばれ得るため、ここをキャッシュしないと非常に重い処理が
            //  無条件に繰り返されてしまう)
            if (_cachedStrainList != null && _cachedStrainMods.SequenceEqual(mods)) return _cachedStrainList;

            try
            {
                var resolvedMods = RulesetHelper.GetMods(ruleset, mods);
                var difficultyCalculator = RulesetHelper.GetExtendedDifficultyCalculator(ruleset.RulesetInfo, workingBeatmap);
                difficultyCalculator.Calculate(resolvedMods);

                if (difficultyCalculator is IExtendedDifficultyCalculator extendedDifficultyCalculator)
                {
                    // osu!std の Aim は「本来の判定用(IncludeSliders=true)」と
                    // 「SliderFactor算出用の内部補助(IncludeSliders=false)」の2インスタンスが生成されるが、
                    // 後者はグラフ表示上ただの重複になるだけなので除外する。
                    var skills = extendedDifficultyCalculator.GetSkills()
                        .Where(skill => skill is not osu.Game.Rulesets.Osu.Difficulty.Skills.Aim aim || aim.IncludeSliders)
                        .ToArray();

                    List<float[]> strainLists = [];

                    foreach (var skill in skills)
                    {
                        // Skillの継承元によってピーク配列の取得方法・意味が異なる。
                        // ・StrainSkill系(StrainDecaySkill等の派生含む): GetCurrentStrainPeaks() が
                        //   IEnumerable<double> を、処理順(=時系列順)の固定長区間ピークとして返す。
                        // ・それ以外(osu!std の Aim=VariableLengthStrainSkill、Speed/Reading=HarmonicSkill等):
                        //   これらのSkillが公開する GetCurrentStrainPeaks() 等のAPIは、そもそも区間の概念を
                        //   持たなかったり(HarmonicSkill)、内部の難易度計算(降順の重み付け合計)のために
                        //   値の大きい順へ並び替え済みで時系列情報を持たない(VariableLengthStrainSkill)ため、
                        //   そのままではグラフ用の時系列データとして使えない。
                        //   そこで、基底のSkillクラスが公開する GetObjectDifficulties()(各HitObjectごとの値、
                        //   処理順=時系列順)と譜面のHitObjectの開始時刻を組み合わせて、StrainSkillと同様の
                        //   固定長区間ごとのピークを自前で再構築する。
                        double[] strains = skill switch
                        {
                            StrainSkill strainSkill => [.. strainSkill.GetCurrentStrainPeaks()],
                            _ => BuildTimeBinnedPeaks(skill.GetObjectDifficulties())
                        };

                        var skillStrainList = new List<float>();

                        for (int i = 0; i < strains.Length; i++)
                        {
                            double strain = strains[i];
                            skillStrainList.Add((float)strain);
                        }

                        strainLists.Add([.. skillStrainList]);
                    }

                    _cachedStrainList = new StrainList
                    {
                        Strains = strainLists,
                        SkillNames = [.. skills.Select(skill => skill.GetType().Name)]
                    };
                }
                else
                {
                    _cachedStrainList = new StrainList();
                }

                _cachedStrainMods = mods;
            }
            catch (Exception e)
            {
                LogUtils.DebugLogger("Error getting strain lists: " + e.Message, true);
                // 失敗時はキャッシュせず、次回呼び出し時に再試行できるようにする
                return new StrainList();
            }

            return _cachedStrainList;
        }

        /// <summary>
        /// GetCurrentStrainPeaks() に相当する「時系列順の固定長区間ピーク」を持たないSkill向けに、
        /// GetObjectDifficulties()(処理順=時系列順の各HitObjectごとの値)と譜面のHitObjectの開始時刻から、
        /// StrainSkillと同様の固定長区間([sectionLength]ms)ごとのピーク配列を再構築する。
        /// (osu!std の Aim=VariableLengthStrainSkill、Speed/Reading=HarmonicSkill など)
        /// osu!std の CreateDifficultyHitObjects は2番目以降のHitObjectを順に処理してDifficultyHitObjectを
        /// 作るため、objectDifficulties[i] は workingBeatmap.Beatmap.HitObjects[i + 1] の開始時刻に対応する。
        /// 各Skill固有の減衰係数は公開されておらず外部から再現できないため、HitObjectが1つも存在しない
        /// 区間は減衰カーブを模倣せず素直に0として扱う(直前値を引き継ぐと、何も起きていない区間に
        /// 高い値が残ってしまうため)。
        /// </summary>
        private double[] BuildTimeBinnedPeaks(IReadOnlyList<double> objectDifficulties, int sectionLength = 400)
        {
            var hitObjects = workingBeatmap.Beatmap.HitObjects;
            int count = Math.Min(objectDifficulties.Count, hitObjects.Count - 1);
            if (count <= 0) return [];

            double firstTime = hitObjects[1].StartTime;
            var buckets = new List<double>();

            for (int i = 0; i < count; i++)
            {
                int bucketIndex = (int)((hitObjects[i + 1].StartTime - firstTime) / sectionLength);
                // 区間内にHitObjectが存在しない箇所は0のまま(減衰の模倣はしない)
                while (buckets.Count <= bucketIndex)
                    buckets.Add(0);

                double value = objectDifficulties[i];
                if (value > buckets[bucketIndex]) buckets[bucketIndex] = value;
            }

            return [.. buckets];
        }

        internal int GetFirstObjectTime()
        {
            var firstObject = workingBeatmap.Beatmap.HitObjects.Count > 1 ? workingBeatmap.Beatmap.HitObjects[1] : null;
            return (int)(firstObject?.StartTime ?? 0);
        }

        internal int GetLastObjectTime()
        {
            var lastObject = workingBeatmap.Beatmap.HitObjects.LastOrDefault();
            return (int)(lastObject?.GetEndTime() ?? 0); // StartTimeだとスライダーエンドが含まれないのでGetEndTime()推奨
        }
    }
}
