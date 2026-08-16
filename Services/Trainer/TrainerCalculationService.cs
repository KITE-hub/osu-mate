namespace OsuMate.Services.Trainer
{
  public readonly record struct BatchPreviewData(
    decimal Rate,
    decimal? Bpm,
    decimal? MinBpm,
    decimal? MaxBpm,
    decimal? Ar,
    decimal? Od,
    decimal? Hp,
    decimal? Cs
  );

  public readonly record struct BatchPreviewRequest(
    decimal StartRate,
    decimal Step,
    int Count,
    decimal MaxRate,
    decimal OriginalBpm,
    decimal MinBpm,
    decimal MaxBpm,
    decimal ArBase,
    bool ScaleAr,
    bool HasOriginalAr,
    decimal OdBase,
    bool ScaleOd,
    bool HasOriginalOd,
    decimal HpBase,
    bool HasOriginalHp,
    decimal CsBase,
    bool HasOriginalCs
  );

  public static class TrainerCalculationService
  {
    private const decimal MinDifficulty = 0.0M;
    private const decimal MaxDifficulty = 10.0M;

    public static decimal ClampDifficulty(decimal value) =>
      Math.Max(MinDifficulty, Math.Min(MaxDifficulty, value));

    public static decimal? ResolveOverride(decimal? original, decimal newValue) =>
      original.HasValue
      && Math.Abs(newValue - original.Value) > BeatmapTrainerService.DifficultyChangeThreshold
        ? newValue
        : null;

    public static decimal ComputeApproachRate(
      decimal arBase,
      decimal rate,
      bool scaleEnabled,
      bool hasOriginal
    ) => scaleEnabled && hasOriginal ? OsuBeatmapFile.ComputeNewAR(arBase, rate) : arBase;

    public static decimal ComputeOverallDifficulty(
      decimal odBase,
      decimal rate,
      bool scaleEnabled,
      bool hasOriginal
    ) => scaleEnabled && hasOriginal ? OsuBeatmapFile.ComputeNewOD(odBase, rate) : odBase;

    public static List<BatchPreviewData> ComputeBatchPreviews(BatchPreviewRequest request)
    {
      var results = new List<BatchPreviewData>();

      for (int i = 0; i < request.Count; i++)
      {
        decimal rate = request.StartRate + (request.Step * i);
        if (rate > request.MaxRate)
          break;

        decimal? bpm = null,
          minBpm = null,
          maxBpm = null;
        if (request.OriginalBpm > 0)
        {
          bpm = request.OriginalBpm * rate;
          minBpm = request.MinBpm * rate;
          maxBpm = request.MaxBpm * rate;
        }

        decimal? ar = request.HasOriginalAr
          ? ComputeApproachRate(request.ArBase, rate, request.ScaleAr, hasOriginal: true)
          : null;
        decimal? od = request.HasOriginalOd
          ? ComputeOverallDifficulty(request.OdBase, rate, request.ScaleOd, hasOriginal: true)
          : null;

        decimal? hp = request.HasOriginalHp ? request.HpBase : null;
        decimal? cs = request.HasOriginalCs ? request.CsBase : null;

        results.Add(new BatchPreviewData(rate, bpm, minBpm, maxBpm, ar, od, hp, cs));
      }

      return results;
    }
  }
}
