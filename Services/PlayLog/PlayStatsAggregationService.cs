using OsuMate.Models;
using System.Linq;

namespace OsuMate.Services.PlayLog
{
    /// <summary>
    /// プレイ履歴から日ごとのSR/pp/Accの平均・標準偏差を集計するステートレスなサービス。
    /// - <see cref="AggregateDailyStats"/> に渡すエントリ群は、呼び出し側で対象期間に絞り込み済みであることを前提とする。
    /// </summary>
    public class PlayStatsAggregationService
    {
        /// <summary>
        /// entries（呼び出し側で対象月に絞り込み済みであること）を日（年月日）ごとにグループ化して
        /// SR / pp / Acc それぞれの平均・標本標準偏差・サンプル数を算出する。昇順（月初→月末）で返す。
        /// entries が空なら空リストを返す。
        /// 指標が未計算（null）のエントリはその指標の集計対象から除外する。
        /// </summary>
        public IReadOnlyList<DailyPlayStats> AggregateDailyStats(IEnumerable<PlayLogEntry> entries)
        {
            return entries
                .GroupBy(e => DateOnly.FromDateTime(e.PlayedAt))
                .OrderBy(g => g.Key)
                .Select(g => new DailyPlayStats(
                    g.Key,
                    ComputeStat(g.Select(e => e.StarRating)),
                    ComputeStat(g.Select(e => e.Pp)),
                    ComputeStat(g.Select(e => (double?)e.Accuracy))))
                .ToList();
        }

        /// <summary>
        /// 平均・標本標準偏差（n-1）・サンプル数を算出する。
        /// null（未計算）の値は除外する。有効な値が0件なら <see cref="MetricStat.Empty"/>。
        /// </summary>
        private static MetricStat ComputeStat(IEnumerable<double?> values)
        {
            var list = values.Where(v => v.HasValue).Select(v => v!.Value).ToList();
            if (list.Count == 0) return MetricStat.Empty;

            var mean = list.Average();
            // サンプル数が1件以下だと0除算になるため、その場合は0として扱う
            var variance = list.Count > 1
                ? list.Sum(v => (v - mean) * (v - mean)) / (list.Count - 1)
                : 0;

            return new MetricStat(mean, Math.Sqrt(variance), list.Count);
        }
    }
}
