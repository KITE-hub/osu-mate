using osu.Game.Rulesets.Difficulty.Skills;

namespace OsuMate.Models;

internal interface IExtendedDifficultyCalculator
{
  Skill[] GetSkills();
}
