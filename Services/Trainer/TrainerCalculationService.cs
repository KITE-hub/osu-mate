namespace OsuMate.Services.Trainer
{
    /// <summary>
    /// 1件分のプレビュー計算結果（フォーマット前の生値）。
    /// 文字列化（表示用フォーマット）は呼び出し側（ViewModel）の責務とする。
    /// </summary>
    public readonly record struct BatchPreviewData(
        decimal Rate,
        decimal? Bpm,
        decimal? MinBpm,
        decimal? MaxBpm,
        decimal? Ar,
        decimal? Od,
        decimal? Hp,
        decimal? Cs);

    /// <summary>プレビュー計算に必要な入力値一式。</summary>
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
        // HP/CS はScaling非対応のため、Base値をそのまま各Rate行に表示する。
        decimal HpBase,
        bool HasOriginalHp,
        decimal CsBase,
        bool HasOriginalCs);

    /// <summary>
    /// Trainer機能における Rate（再生速度）・BPM・AR/OD スケーリングの計算を担うドメインサービス。
    /// TrainerViewModel はUIバインディング用プロパティの提供に専念し、実際の計算式はここに集約する。
    /// 状態を持たないため、ViewModelからは静的メソッドとして直接呼び出せる。
    /// </summary>
    public static class TrainerCalculationService
    {
        private const decimal MinDifficulty = 0.0M;
        private const decimal MaxDifficulty = 10.0M;

        /// <summary>AR/OD/HP/CS の値を有効範囲（0〜10）に収める。</summary>
        public static decimal ClampDifficulty(decimal value)
            => Math.Max(MinDifficulty, Math.Min(MaxDifficulty, value));

        /// <summary>
        /// 元の値との差が閾値（<see cref="BeatmapTrainerService.DifficultyChangeThreshold"/>）を
        /// 超えていない場合は null（＝.osuを書き換えない）を返す共通ヘルパー。
        /// </summary>
        public static decimal? ResolveOverride(decimal? original, decimal newValue)
            => original.HasValue && Math.Abs(newValue - original.Value) > BeatmapTrainerService.DifficultyChangeThreshold
                ? newValue
                : null;

        /// <summary>指定した Rate における AR のスケール後の値を計算する。</summary>
        public static decimal ComputeApproachRate(decimal arBase, decimal rate, bool scaleEnabled, bool hasOriginal)
            => scaleEnabled && hasOriginal ? OsuBeatmapFile.ComputeNewAR(arBase, rate) : arBase;

        /// <summary>指定した Rate における OD のスケール後の値を計算する。</summary>
        public static decimal ComputeOverallDifficulty(decimal odBase, decimal rate, bool scaleEnabled, bool hasOriginal)
            => scaleEnabled && hasOriginal ? OsuBeatmapFile.ComputeNewOD(odBase, rate) : odBase;

        /// <summary>Batchモードで生成される各Rateのプレビュー用生データを計算する。</summary>
        public static List<BatchPreviewData> ComputeBatchPreviews(BatchPreviewRequest request)
        {
            var results = new List<BatchPreviewData>();

            for (int i = 0; i < request.Count; i++)
            {
                decimal rate = request.StartRate + (request.Step * i);
                if (rate > request.MaxRate) break;

                decimal? bpm = null, minBpm = null, maxBpm = null;
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

                // HP/CS はRateによるScalingが無いため、全Rate行で同じBase値を表示する。
                decimal? hp = request.HasOriginalHp ? request.HpBase : null;
                decimal? cs = request.HasOriginalCs ? request.CsBase : null;

                results.Add(new BatchPreviewData(rate, bpm, minBpm, maxBpm, ar, od, hp, cs));
            }

            return results;
        }
    }
}
