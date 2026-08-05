using osu.Game.Beatmaps;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Catch;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Taiko;
using OsuMate.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OsuMate.Services.Osu;

internal static class RulesetHelper
{
    public static Ruleset GetRuleset(int mode)
    {
        return mode switch
        {
            0 => new OsuRuleset(),
            1 => new TaikoRuleset(),
            2 => new CatchRuleset(),
            3 => new ManiaRuleset(),
            _ => throw new ArgumentException("Invalid ruleset ID provided.")
        };
    }

    public static DifficultyCalculator GetExtendedDifficultyCalculator(RulesetInfo ruleset, IWorkingBeatmap working)
    {
        return ruleset.OnlineID switch
        {
            0 => new PPCalculation.ExtendedOsuDifficultyCalculator(ruleset, working),
            1 => new PPCalculation.ExtendedTaikoDifficultyCalculator(ruleset, working),
            2 => new PPCalculation.ExtendedCatchDifficultyCalculator(ruleset, working),
            3 => new PPCalculation.ExtendedManiaDifficultyCalculator(ruleset, working),
            _ => throw new ArgumentException("Invalid ruleset ID provided.")
        };
    }

    public static Mod[] GetMods(Ruleset ruleset, CalculateArgs args) => GetMods(ruleset, args.Mods);

    public static Mod[] GetMods(Ruleset ruleset, string[] modAcronyms)
    {
        var availableMods = ruleset.CreateAllMods();

        // 該当Rulesetにまだ実装が無い場合（例: Catch）は単純に付与しない。
        var classicMod = availableMods.OfType<ModClassic>().FirstOrDefault();

        if (modAcronyms.Length == 0)
        {
            return classicMod != null ? [classicMod] : [];
        }

        // Enumerable.Append を鎖状に呼ぶと、Append のたびにラッパーが1段ずつ積み重なり、
        // 最終的な列挙時に毎回それまでの全段を辿り直すことになる(Mod数に対してO(n^2))。
        // List<Mod> に直接追加すればこの問題は起きない。
        var mods = new List<Mod>();

        foreach (var modString in modAcronyms)
        {
            var mod = availableMods.FirstOrDefault(m => string.Equals(m.Acronym, modString, StringComparison.CurrentCultureIgnoreCase));
            if (mod != null)
            {
                mods.Add(mod);
            }
        }

        if (classicMod != null) mods.Add(classicMod);

        return [.. mods];
    }

    public static double GetSpeedMultiplier(string[] mods)
    {
        if (mods.Contains("dt") || mods.Contains("nc")) return 1.0 / 1.5;
        if (mods.Contains("ht")) return 1.0 / 0.75;
        return 1.0;
    }

    public static (double modMultiplier, double modDivider) ModMultiplierModDividerCalculator(string[] mods)
    {
        double modMultiplier = 1;
        double modDivider = 1;

        if (mods.Contains("ez")) modMultiplier *= 0.5;
        if (mods.Contains("nf")) modMultiplier *= 0.5;
        if (mods.Contains("ht")) modMultiplier *= 0.5;

        if (mods.Contains("hr")) modDivider /= 1.08;
        if (mods.Contains("dt")) modDivider /= 1.1;
        if (mods.Contains("nc")) modDivider /= 1.1;
        if (mods.Contains("fi")) modDivider /= 1.06;
        if (mods.Contains("hd")) modDivider /= 1.06;
        if (mods.Contains("fl")) modDivider /= 1.06;

        return (modMultiplier, modDivider);
    }
}
