namespace OsuMate.Models
{
    /// <summary>
    /// コントリビューショングラフ（PlayLogView上部のカレンダー）の1マス分のデータ。
    /// ContributionGraphViewModel が集計結果から都度コレクションごと作り直す
    /// イミュータブルな表示用データのため、PlayLogEntry 等と異なり
    /// INotifyPropertyChanged は実装しない（生成後に値が書き換わることはない）。
    /// </summary>
    public sealed class ContributionDay
    {
        /// <summary>対象日（時刻は持たない）。プレースホルダーセルの場合は default 値。</summary>
        public DateOnly Date { get; }

        /// <summary>その日の合計打数（Count300 + Count100 + Count50 + CountGeki + CountKatu）。</summary>
        public int TotalHits { get; }

        /// <summary>
        /// 色分け表示用のレベル。0 = プレイなし、1〜4 = 打数に応じて段階的に濃くなる。
        /// </summary>
        public int Level { get; }

        /// <summary>
        /// このセルが今日の日付かどうか。View 側で強調表示（枠線など）に使用する。
        /// </summary>
        public bool IsToday { get; }

        /// <summary>
        /// 月の先頭の空白埋め用プレースホルダーかどうか。
        /// true の場合はツールチップなし・透明セルとして描画する。
        /// </summary>
        public bool IsPlaceholder { get; }

        /// <summary>その日の Star Rating（譜面の★）の平均・標準偏差。データがない日は <see cref="MetricStat.Empty"/>。</summary>
        public MetricStat StarRating { get; }

        /// <summary>その日の pp の平均・標準偏差。データがない日は <see cref="MetricStat.Empty"/>。</summary>
        public MetricStat Pp { get; }

        /// <summary>その日の Accuracy（%）の平均・標準偏差。データがない日は <see cref="MetricStat.Empty"/>。</summary>
        public MetricStat Accuracy { get; }

        /// <summary>
        /// セルへのマウスホバーで表示するツールチップ文言。
        /// 1行目は当日の合計打数（Miss判定を除く）、2〜4行目はSR/pp/Accそれぞれの
        /// 平均±標準偏差（"F2"精度）。サンプルが1件もない指標は "-" と表示する。
        /// </summary>
        public string TooltipText =>
            $"Valid Hits: {TotalHits}\n" +
            $"SR: {FormatMetric(StarRating)}\n" +
            $"pp: {FormatMetric(Pp)}\n" +
            $"Acc: {FormatMetric(Accuracy, "%")}";

        public ContributionDay(DateOnly date, int totalHits, int level, bool isToday,
            MetricStat starRating, MetricStat pp, MetricStat accuracy)
        {
            Date = date;
            TotalHits = totalHits;
            Level = level;
            IsToday = isToday;
            IsPlaceholder = false;
            StarRating = starRating;
            Pp = pp;
            Accuracy = accuracy;
        }

        /// <summary>月先頭の空白埋め用プレースホルダーセルを生成する。</summary>
        public static ContributionDay Placeholder { get; } = new ContributionDay();

        private ContributionDay()
        {
            IsPlaceholder = true;
            StarRating = MetricStat.Empty;
            Pp = MetricStat.Empty;
            Accuracy = MetricStat.Empty;
        }

        /// <summary>平均±標準偏差を "平均±標準偏差" 形式（"F2"精度）の文字列に整形する。サンプルが1件もない場合は "-"。</summary>
        private static string FormatMetric(MetricStat stat, string suffix = "")
            => stat.SampleCount == 0 ? "-" : $"{stat.Mean:F2}±{stat.StdDev:F2}{suffix}";
    }
}
