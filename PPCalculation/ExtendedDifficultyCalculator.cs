using osu.Game.Beatmaps;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Catch.Difficulty;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Difficulty.Skills;
using osu.Game.Rulesets.Mania.Difficulty;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Difficulty;
using osu.Game.Rulesets.Taiko.Difficulty;
using OsuMate.Models;

namespace OsuMate.PPCalculation;

internal class ExtendedOsuDifficultyCalculator(IRulesetInfo ruleset, IWorkingBeatmap beatmap)
  : OsuDifficultyCalculator(ruleset, beatmap),
    IExtendedDifficultyCalculator
{
  private Skill[] skills = [];

  public Skill[] GetSkills() => skills;

  protected override DifficultyAttributes CreateDifficultyAttributes(
    IBeatmap beatmap,
    Mod[] mods,
    Skill[] skills
  )
  {
    this.skills = skills;
    return base.CreateDifficultyAttributes(beatmap, mods, skills);
  }
}

internal class ExtendedTaikoDifficultyCalculator(IRulesetInfo ruleset, IWorkingBeatmap beatmap)
  : TaikoDifficultyCalculator(ruleset, beatmap),
    IExtendedDifficultyCalculator
{
  private Skill[] skills = [];

  public Skill[] GetSkills() => skills;

  protected override DifficultyAttributes CreateDifficultyAttributes(
    IBeatmap beatmap,
    Mod[] mods,
    Skill[] skills
  )
  {
    this.skills = skills;
    return base.CreateDifficultyAttributes(beatmap, mods, skills);
  }
}

internal class ExtendedCatchDifficultyCalculator(IRulesetInfo ruleset, IWorkingBeatmap beatmap)
  : CatchDifficultyCalculator(ruleset, beatmap),
    IExtendedDifficultyCalculator
{
  private Skill[] skills = [];

  public Skill[] GetSkills() => skills;

  protected override DifficultyAttributes CreateDifficultyAttributes(
    IBeatmap beatmap,
    Mod[] mods,
    Skill[] skills
  )
  {
    this.skills = skills;
    return base.CreateDifficultyAttributes(beatmap, mods, skills);
  }
}

internal class ExtendedManiaDifficultyCalculator(IRulesetInfo ruleset, IWorkingBeatmap beatmap)
  : ManiaDifficultyCalculator(ruleset, beatmap),
    IExtendedDifficultyCalculator
{
  private Skill[] skills = [];

  public Skill[] GetSkills() => skills;

  protected override DifficultyAttributes CreateDifficultyAttributes(
    IBeatmap beatmap,
    Mod[] mods,
    Skill[] skills
  )
  {
    this.skills = skills;
    return base.CreateDifficultyAttributes(beatmap, mods, skills);
  }
}
