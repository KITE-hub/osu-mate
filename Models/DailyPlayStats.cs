namespace OsuMate.Models
{
    /// <summary>
    /// 1日分の SR / pp / Acc 統計。PlayStatsAggregationService.AggregateDailyStats() の戻り値の1行分。
    /// 3指標は同じ日（同じX軸の点）に属するため、バラバラの配列にせず1つのまとまりとして持つ
    /// （PlayStatsChartViewModel 側で3つのグラフを作る際、同じ日のデータを都度突き合わせる必要がなくなる）。
    /// </summary>
    public sealed class DailyPlayStats
    {
        /// <summary>対象日。</summary>
        public DateOnly Date { get; }

        /// <summary>その日の Star Rating（譜面の★）統計。</summary>
        public MetricStat StarRating { get; }

        /// <summary>その日の pp 統計。</summary>
        public MetricStat Pp { get; }

        /// <summary>その日の Accuracy（%）統計。</summary>
        public MetricStat Accuracy { get; }

        public DailyPlayStats(DateOnly date, MetricStat starRating, MetricStat pp, MetricStat accuracy)
        {
            Date = date;
            StarRating = starRating;
            Pp = pp;
            Accuracy = accuracy;
        }
    }
}
