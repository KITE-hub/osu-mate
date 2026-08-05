namespace OsuMate.Models
{
    /// <summary>
    /// ある指標（SR / pp / Acc など）1ヶ月分の平均・標準偏差・サンプル数。
    /// PlayStatsAggregationService の集計結果を構成する部品として使う、不変な値の入れ物。
    ///
    /// サンプル数が極端に少ない月（1件のみなど）は標本標準偏差が 0 になり、
    /// 「その月はブレがなかった」ように見えてしまうが、その扱い（しきい値未満は
    /// 非表示にする／サンプル数に応じて帯の不透明度を下げる等）は現時点では未実装で、
    /// 意図的に将来の課題として残している。ここでは算出結果をそのまま保持するのみ。
    /// </summary>
    public sealed class MetricStat
    {
        /// <summary>平均値。</summary>
        public double Mean { get; }

        /// <summary>標本標準偏差（n-1 で割った不偏分散の平方根）。サンプル数が1件以下なら 0。</summary>
        public double StdDev { get; }

        /// <summary>この月に含まれるサンプル数（当該指標が計算済みのプレイ数）。</summary>
        public int SampleCount { get; }

        public MetricStat(double mean, double stdDev, int sampleCount)
        {
            Mean = mean;
            StdDev = stdDev;
            SampleCount = sampleCount;
        }

        /// <summary>サンプルが1件もない場合の既定値。</summary>
        public static MetricStat Empty { get; } = new MetricStat(0, 0, 0);
    }
}
