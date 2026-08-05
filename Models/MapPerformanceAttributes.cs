using osu.Game.Rulesets.Difficulty;

namespace OsuMate.Models;

internal class MapPerformanceAttributes
{
    internal string[] Mods { get; set; } = [];
    internal PerformanceAttributes? PerformanceAttributes { get; set; }
}
