using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Scoring;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OsuMate.Services.Osu;

internal static class TimingHelper
{
    public static Dictionary<HitResult, double> GetModifiedHitWindows(
     int mode,
     double overallDifficulty,
     Mod[] mods)
    {
        var windows = Enum.GetValues<HitResult>()
            .ToDictionary(r => r, _ => 0.0);
        double multiplier = RulesetHelper.GetSpeedMultiplier(mods.Select(m => m.Acronym.ToLower()).ToArray());

        switch (mode)
        {
            case 0: // osu!
                windows[HitResult.Great] = (Math.Floor(80 - overallDifficulty * 6) - 0.5) * multiplier;
                windows[HitResult.Ok] = (Math.Floor(140 - overallDifficulty * 8) - 0.5) * multiplier;
                windows[HitResult.Meh] = (Math.Floor(200 - overallDifficulty * 10) - 0.5) * multiplier;
                break;

            case 1: // taiko
                windows[HitResult.Great] = (Math.Floor(50 - overallDifficulty * 3) - 0.5) * multiplier;
                windows[HitResult.Ok] =
                    overallDifficulty < 5
                        ? (Math.Floor(120 - overallDifficulty * 8) - 0.5) * multiplier
                        : (Math.Floor(110 - overallDifficulty * 6) - 0.5) * multiplier;
                break;

            case 2: // catch
                    // catch は判定幅概念なし → 全部 0 のまま
                break;

            case 3: // mania
                double multiplier2;
                if (mods.Any(m => m is ModHardRock)) multiplier2 = 1 / 1.4;
                else if (mods.Any(m => m is ModEasy)) multiplier2 = 1.4;
                else multiplier2 = 1.0;
                windows[HitResult.Perfect] = (Math.Floor(16 * multiplier2 / multiplier) + 0.5) * multiplier;
                windows[HitResult.Great] = (Math.Floor((64 - overallDifficulty * 3) * multiplier2 / multiplier) + 0.5) * multiplier;
                windows[HitResult.Good] = (Math.Floor((97 - overallDifficulty * 3) * multiplier2 / multiplier) + 0.5) * multiplier;
                windows[HitResult.Ok] = (Math.Floor((127 - overallDifficulty * 3) * multiplier2 / multiplier) + 0.5) * multiplier;
                windows[HitResult.Meh] = (Math.Floor((151 - overallDifficulty * 3) * multiplier2 / multiplier) + 0.5) * multiplier;
                break;

            default:
                throw new ArgumentException($"Invalid mode: {mode}");
        }

        return windows;
    }

    // CalculateDetailedOffset / CalculateUR はここにあったが、HitErrorStatsAccumulator に
    // 置き換えた。Slow Lane(PpCalculationService) と Fast Lane(MainViewModel.UpdateFastUI)が
    // それぞれ独立にHitErrors全体を2パスで舐め直していたのを、両者で1つのインスタンスを
    // 共有してWelfordのオンラインアルゴリズムで増分更新する方式に変更している。
    // 計算内容（単純平均・母標準偏差、IQRトリムなし）自体は変えていない。
}
