using osu.Game.Beatmaps;
using osu.Game.Rulesets.Catch.Objects;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Taiko.Objects;
using OsuMate.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OsuMate.Services.Osu;

internal static class ScoreHelper
{
    public static int CountTotalHitObjects(IBeatmap beatmap, int mode)
    {
        return mode switch
        {
            0 => beatmap.HitObjects.Count,
            1 => beatmap.HitObjects.OfType<Hit>().Count(),
            2 => beatmap.HitObjects.Count(h => h is Fruit) + beatmap.HitObjects.OfType<JuiceStream>()
                .SelectMany(j => j.NestedHitObjects)
                .Count(h => h is not TinyDroplet),
            3 => beatmap.HitObjects.Count,
            _ => throw new ArgumentException("Invalid ruleset ID provided.")
        };
    }

    public static int ManiaScoreCalculator(IBeatmap beatmap, HitsResult hits, string[] mods, int currentScore)
    {
        const int hitValue = 320;
        const int hitBonusValue = 32;
        const int hitBonus = 2;
        const int hitPunishment = 0;

        int totalNotes = hits.HitGeki + hits.Hit300 + hits.HitKatu + hits.Hit100 + hits.Hit50 + hits.HitMiss;
        int objectCount = beatmap.HitObjects.Count;

        const int maxScore = 1000000;
        double bonus = 100;
        double baseScore = 0;
        double bonusScore = 0;
        var (modMultiplier, modDivider) = RulesetHelper.ModMultiplierModDividerCalculator(mods);

        const double hitValueRatio = hitValue / 320.0;
        const double hitBounsValueRatio = hitBonusValue / 320.0;
        double objectCountRatio = 0.5 / objectCount;

        for (int i = 0; i < totalNotes; i++)
        {
            bonus = Math.Max(0, Math.Min(100, (bonus + hitBonus - hitPunishment) / modDivider));
            baseScore += maxScore * modMultiplier * objectCountRatio * hitValueRatio;
            bonusScore += maxScore * modMultiplier * objectCountRatio * hitBounsValueRatio * Math.Sqrt(bonus);
        }

        double ratio = (double)totalNotes / objectCount;
        double score = 0;
        if (totalNotes == hits.HitGeki)
        {
            score = (int)(maxScore * modMultiplier);
        }
        else if (totalNotes != hits.HitMiss)
        {
            score = Math.Max((int)((maxScore * modMultiplier) - Math.Round((Math.Round(baseScore + bonusScore) - currentScore) / ratio)), 0);
        }

        if (double.IsNaN(score)) score = 0;

        return (int)Math.Round(score);
    }

    public static double GetAccuracy(int count300, int count100, int count50, int countGeki, int countKatu, int countMiss, int mode)
    {
        var statistics = new Dictionary<HitResult, int>
        {
            { HitResult.Perfect, countGeki },
            { HitResult.Great, count300 },
            { HitResult.Good, countKatu },
            { HitResult.Ok, count100 },
            { HitResult.Meh, count50 },
            { HitResult.Miss, countMiss },
            { HitResult.LargeTickHit, count100 },
            { HitResult.SmallTickHit, count50 },
            { HitResult.SmallTickMiss, countKatu }
        };
        return GetAccuracy(statistics, mode);
    }

    public static double GetAccuracy(IReadOnlyDictionary<HitResult, int> statistics, int mode)
    {
        switch (mode)
        {
            case 0:
                {
                    var countGreat = statistics[HitResult.Great];
                    var countGood = statistics[HitResult.Ok];
                    var countMeh = statistics[HitResult.Meh];
                    var countMiss = statistics[HitResult.Miss];
                    var total = countGreat + countGood + countMeh + countMiss;

                    return (double)((6 * countGreat) + (2 * countGood) + countMeh) / (6 * total);
                }

            case 1:
                {
                    var countGreat = statistics[HitResult.Great];
                    var countGood = statistics[HitResult.Ok];
                    var countMiss = statistics[HitResult.Miss];
                    var total = countGreat + countGood + countMiss;

                    return (double)((2 * countGreat) + countGood) / (2 * total);
                }

            case 2:
                {
                    double hits = statistics[HitResult.Great] + statistics[HitResult.LargeTickHit] + statistics[HitResult.SmallTickHit];
                    double total = hits + statistics[HitResult.Miss] + statistics[HitResult.SmallTickMiss];

                    return hits / total;
                }

            case 3:
                {
                    double hits =
                        (6 * statistics[HitResult.Perfect]) +
                        (6 * statistics[HitResult.Great]) +
                        (4 * statistics[HitResult.Good]) +
                        (2 * statistics[HitResult.Ok]) +
                         statistics[HitResult.Meh];
                    double total = 6 * (statistics[HitResult.Meh] + statistics[HitResult.Ok] +
                                        statistics[HitResult.Great] + statistics[HitResult.Miss] +
                                        statistics[HitResult.Perfect] + statistics[HitResult.Good]);

                    return hits / total;
                }

            default:
                throw new ArgumentException("Invalid mode provided. Given mode: " + mode);
        }
    }

    public static string GetCurrentRank(IReadOnlyDictionary<HitResult, int> statistics, int mode, string[] mods)
    {
        string rank = "Unknown";
        bool silver = mods.Contains("hd") || mods.Contains("fl");

        switch (mode)
        {
            case 0:
                {
                    var h300 = statistics[HitResult.Great];
                    var h100 = statistics[HitResult.Ok];
                    var h50 = statistics[HitResult.Meh];
                    var h0 = statistics[HitResult.Miss];

                    int total = h300 + h100 + h50 + h0;
                    if (total == 0) return "Unknown";
                    
                    double r300 = (double)h300 / total;
                    double r50 = (double)h50 / total;

                    switch (r300)
                    {
                        case 1:
                            rank = silver ? "XH" : "X";
                            break;
                        case > 0.9 when r50 < 0.01 && h0 == 0:
                            rank = silver ? "SH" : "S";
                            break;
                        case > 0.8 when h0 == 0:
                        case > 0.9:
                            rank = "A";
                            break;
                        case > 0.7 when h0 == 0:
                        case > 0.8:
                            rank = "B";
                            break;
                        case > 0.6:
                            rank = "C";
                            break;
                        default:
                            rank = "D";
                            break;
                    }
                }
                break;

            case 1:
                {
                    var h300 = statistics[HitResult.Great];
                    var h100 = statistics[HitResult.Ok];
                    var h0 = statistics[HitResult.Miss];
                    int total = h300 + h100 + h0;
                    if (total == 0) return "Unknown";

                    double r300 = (double)h300 / total;

                    switch (r300)
                    {
                        case 1:
                            rank = silver ? "XH" : "X";
                            break;
                        case > 0.9 when h0 == 0:
                            rank = silver ? "SH" : "S";
                            break;
                        case > 0.8 when h0 == 0:
                        case > 0.9:
                            rank = "A";
                            break;
                        case > 0.7 when h0 == 0:
                        case > 0.8:
                            rank = "B";
                            break;
                        case > 0.6:
                            rank = "C";
                            break;
                        default:
                            rank = "D";
                            break;
                    }
                }
                break;

            case 2:
                {
                    var h300 = statistics[HitResult.Great];
                    var h100 = statistics[HitResult.LargeTickHit];
                    var h50 = statistics[HitResult.SmallTickHit];
                    var katu = statistics[HitResult.SmallTickMiss];
                    var h0 = statistics[HitResult.Miss];
                    int total = h300 + h100 + h50 + h0 + katu;
                    double acc = total > 0 ? (h50 + h100 + h300) / (double)total : 1;

                    rank = acc switch
                    {
                        1 => silver ? "XH" : "X",
                        > 0.98 => silver ? "SH" : "S",
                        > 0.94 => "A",
                        > 0.9 => "B",
                        > 0.85 => "C",
                        _ => "D"
                    };
                }
                break;

            case 3:
                {
                    var h300 = statistics[HitResult.Perfect];
                    var h100 = statistics[HitResult.Great];
                    var h50 = statistics[HitResult.Good];
                    var geki = statistics[HitResult.Ok];
                    var katu = statistics[HitResult.Meh];
                    var h0 = statistics[HitResult.Miss];
                    int total = h300 + h100 + h50 + h0 + geki + katu;
                    double acc = total > 0 ? ((h50 * 50) + (h100 * 100) + (katu * 200) + ((h300 + geki) * 300)) / (total * 300.0) : 1;

                    rank = acc switch
                    {
                        1 => silver ? "XH" : "X",
                        > 0.95 => silver ? "SH" : "S",
                        > 0.9 => "A",
                        > 0.8 => "B",
                        > 0.7 => "C",
                        _ => "D"
                    };
                }
                break;
        }

        return rank;
    }

    public static int GetMaxCombo(IBeatmap beatmap, int mode)
    {
        return mode switch
        {
            0 => beatmap.GetMaxCombo(),
            1 => beatmap.HitObjects.OfType<Hit>().Count(),
            2 => beatmap.HitObjects.Count(h => h is Fruit) + beatmap.HitObjects.OfType<JuiceStream>().SelectMany(j => j.NestedHitObjects).Count(h => h is not TinyDroplet),
            3 => beatmap.HitObjects.Count,
            _ => throw new ArgumentException("Invalid ruleset ID provided.")
        };
    }
}
