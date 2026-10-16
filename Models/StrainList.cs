using osu.Game.Rulesets.Difficulty;

namespace OsuMate.Models
{

  internal class StrainList
  {
    internal List<float[]> Strains { get; set; } = [];
    internal string[] SkillNames { get; set; } = [];
    internal string[] Mods { get; set; } = [];
    internal DifficultyAttributes? DifficultyAttributes { get; set; }
  }
}
