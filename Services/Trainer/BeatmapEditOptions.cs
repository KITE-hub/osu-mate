namespace OsuMate.Services.Trainer
{
  internal sealed record BeatmapEditOptions(
    string Version,
    string AudioFilename,
    IReadOnlyList<string> Tags,
    decimal Rate,
    decimal? ArOverride = null,
    decimal? OdOverride = null,
    decimal? HpOverride = null,
    decimal? CsOverride = null,
    bool DisableSv = false,
    IReadOnlyList<string>? HitObjectLines = null
  );
}
