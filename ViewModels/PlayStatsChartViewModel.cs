using OsuMate.Models;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using System.Linq;

namespace OsuMate.ViewModels
{
    /// <summary>
    /// PlayLogView右側に表示する、月ごとのSR/pp/Acc（散布図 + 平均±標準偏差）推移グラフ用ViewModel。
    /// - 横軸は1ヶ月分（日単位）に固定。対象月は呼び出し元から受け取り、データが空のSeriesはPlotModelに追加しない。
    /// - <see cref="IThemeable"/> によりテーマに追従するが、3指標の系列色自体は固定とする。
    /// - 日次の平均・標準偏差（帯）と、個々のプレイの散布図を重ねて描画する。
    ///   描画順は band(帯) → scatter(個々の点) → meanLine(平均線) の順。
    /// - 集計は行わない。日次のSR/pp/Acc統計は ContributionGraphViewModel.DailyStats
    ///   （ContributionGraphのセルツールチップと同じ集計結果）をそのまま受け取り、対象月分に絞り込んで使う。
    /// </summary>
    public class PlayStatsChartViewModel : ObservableBase, IThemeable
    {
        // 3指標を視覚的に区別するためだけの固定割り当て。テーマ（Light/Dark）とは無関係。
        private static readonly OxyColor StarRatingColor = OxyColors.SteelBlue;
        private static readonly OxyColor PpColor = OxyColors.DarkOrange;
        private static readonly OxyColor AccuracyColor = OxyColors.MediumSeaGreen;

        private ThemeSettings _theme;

        private PlotModel _starRatingPlotModel = new() { Title = "Star Rating" };
        /// <summary>選択中の月の、日ごとの Star Rating 平均±標準偏差 + 個々のプレイの散布図。</summary>
        public PlotModel StarRatingPlotModel
        {
            get => _starRatingPlotModel;
            private set { _starRatingPlotModel = value; OnPropertyChanged(); }
        }

        private PlotModel _ppPlotModel = new() { Title = "pp" };
        /// <summary>選択中の月の、日ごとの pp 平均±標準偏差 + 個々のプレイの散布図。</summary>
        public PlotModel PpPlotModel
        {
            get => _ppPlotModel;
            private set { _ppPlotModel = value; OnPropertyChanged(); }
        }

        private PlotModel _accuracyPlotModel = new() { Title = "Accuracy" };
        /// <summary>選択中の月の、日ごとの Accuracy 平均±標準偏差 + 個々のプレイの散布図。</summary>
        public PlotModel AccuracyPlotModel
        {
            get => _accuracyPlotModel;
            private set { _accuracyPlotModel = value; OnPropertyChanged(); }
        }

        public PlayStatsChartViewModel(ThemeSettings theme)
        {
            _theme = theme;

            // フィールド初期化子で作った空モデル（Recalculate() が呼ばれるまでの初期表示用）にも
            // 起動時点のテーマを反映しておく。これをしないと、初回 Recalculate() までの間だけ
            // OxyPlot既定色（黒文字等）で一瞬表示されてしまう。
            ApplyThemeToModel(_starRatingPlotModel);
            ApplyThemeToModel(_ppPlotModel);
            ApplyThemeToModel(_accuracyPlotModel);
        }

        /// <summary>
        /// 渡された <paramref name="dailyStats"/> / <paramref name="filteredEntries"/> を
        /// <paramref name="month"/> 分だけに絞り込み、3枚の PlotModel を作り直す。
        /// 呼び出し側（PlayLogViewModel）のフィルタ状態が変わったとき、および
        /// ContributionGraphViewModel.CurrentMonth（ComboBoxでの月選択）が変わったときの
        /// 両方で呼び出される想定。
        /// </summary>
        /// <param name="filteredEntries">
        /// 現在選択中のモード（および他のフィルタ条件）で絞り込まれたエントリ（複数月分を含んでよい）。
        /// 散布図（個々のプレイの点）の元データとして使う。
        /// </param>
        /// <param name="dailyStats">
        /// 日付ごとのSR/pp/Acc統計（複数月分を含んでよい）。ContributionGraphViewModel.DailyStats
        /// （= PlayStatsAggregationService.AggregateDailyStats の結果）をそのまま渡す想定。
        /// ここで対象月分だけに絞り込む。
        /// </param>
        /// <param name="month">
        /// 横軸として表示する対象月（その月の1日を表す DateOnly）。
        /// </param>
        public void Recalculate(
            IEnumerable<PlayLogEntry> filteredEntries,
            IReadOnlyDictionary<DateOnly, DailyPlayStats> dailyStats,
            DateOnly month)
        {
            // 散布図の元データ(monthEntries)と帯・平均線の元データ(monthDailyStats)を、
            // それぞれ対象月分だけに絞り込む（集計自体はここでは行わない）。
            var monthEntries = filteredEntries
                .Where(e => e.PlayedAt.Year == month.Year && e.PlayedAt.Month == month.Month)
                .ToList();

            var monthDailyStats = dailyStats.Values
                .Where(s => s.Date.Year == month.Year && s.Date.Month == month.Month)
                .OrderBy(s => s.Date)
                .ToList();

            // 3枚は横軸(月内の日付)を完全に共有するが、見比べる際に各チャート単体でも
            // 日付が読めた方が良いため、横軸ラベルは3枚とも表示する。
            StarRatingPlotModel = BuildPlotModel(
                "SR", monthDailyStats, monthEntries, s => s.StarRating, e => e.StarRating, StarRatingColor, month);
            PpPlotModel = BuildPlotModel(
                "pp", monthDailyStats, monthEntries, s => s.Pp, e => e.Pp, PpColor, month);
            AccuracyPlotModel = BuildPlotModel(
                "Acc (%)", monthDailyStats, monthEntries, s => s.Accuracy, e => e.Accuracy, AccuracyColor, month);
        }

        /// <summary>
        /// URTimeGraphViewModel / URDistGraphViewModel と同じ IThemeable 契約。
        /// データの再集計は行わず、既存の3モデルの「地の色」（文字・枠線・グリッド線・フォント）
        /// だけを差し替えて再描画する。
        /// </summary>
        public void ApplyTheme(ThemeSettings theme)
        {
            _theme = theme;
            ApplyThemeToModel(_starRatingPlotModel);
            ApplyThemeToModel(_ppPlotModel);
            ApplyThemeToModel(_accuracyPlotModel);
        }

        /// <summary>
        /// PlotModel 1枚分の、テーマに依存する「地の色」だけを差し替える。
        /// 系列（band/scatter/meanLine）の色は指標ごとの固定色なのでここでは触らない。
        /// </summary>
        private void ApplyThemeToModel(PlotModel model)
        {
            model.TextColor = _theme.OxyTextColor;
            model.PlotAreaBorderColor = _theme.OxyBorderColor;
            model.DefaultFont = _theme.OxyFontFamily;

            foreach (var axis in model.Axes)
            {
                axis.TicklineColor = _theme.OxyBorderColor;
                axis.MajorGridlineColor = OxyColor.FromAColor(40, _theme.OxyBorderColor);
            }

            model.InvalidatePlot(false);
        }

        /// <summary>
        /// 1指標分のデータを、
        /// 「平均±標準偏差の帯 + 個々のプレイの散布図 + 平均の折れ線」を持つ PlotModel に変換する。
        /// 横軸は <paramref name="month"/> の月初〜翌月月初に固定する
        /// （データが疎な月でも、隣のコントリビューショングラフと同じ「1ヶ月分」の
        /// 期間であることが見た目にも分かるようにするため）。
        /// </summary>
        /// <param name="dailyStats">日ごとの平均±標準偏差（帯・平均線の元データ）。</param>
        /// <param name="monthEntries">対象月に絞り込み済みの生エントリ（散布図の元データ）。</param>
        /// <param name="dailyMetricSelector">dailyStats の1件から対象指標の MetricStat を取り出す関数。</param>
        /// <param name="rawMetricSelector">個々のエントリから対象指標の生値を取り出す関数（未計算ならnull）。</param>
        private PlotModel BuildPlotModel(
            string yAxisTitle,
            IReadOnlyList<DailyPlayStats> dailyStats,
            IReadOnlyList<PlayLogEntry> monthEntries,
            Func<DailyPlayStats, MetricStat> dailyMetricSelector,
            Func<PlayLogEntry, double?> rawMetricSelector,
            OxyColor seriesColor,
            DateOnly month)
        {
            var model = new PlotModel
            {
                Background = OxyColors.Transparent,
                PlotAreaBackground = OxyColors.Transparent,
                TextColor = _theme.OxyTextColor,
                DefaultFont = _theme.OxyFontFamily,
                DefaultFontSize = 12F,
                PlotAreaBorderColor = _theme.OxyBorderColor,
                Padding = new OxyThickness(1, 1, 1, 1),
                IsLegendVisible = false,
                // 3枚のプロットエリアの左端・下端を完全に揃えるため、余白を固定値にする
                PlotMargins = new OxyThickness(52, double.NaN, 12, 28),
            };

            var monthStart = month.ToDateTime(TimeOnly.MinValue);
            var monthEnd = month.AddMonths(1).ToDateTime(TimeOnly.MinValue);

            // 罫線(目盛線)は5日ごと。月の日数(28〜31日)に関わらず、1日から5日刻みで
            // その月の範囲(monthEnd未満)に収まる分だけ生成する。
            var gridlineValues = new List<double>();
            for (var d = monthStart; d < monthEnd; d = d.AddDays(5))
            {
                gridlineValues.Add(DateTimeAxis.ToDouble(d));
            }

            // ラベルは 1日・10日・20日・30日 のみ（年は表示しない。表記形式は"M/d"）。
            var labelValues = new List<double>();
            foreach (var day in new[] { 1, 10, 20, 30 })
            {
                var candidate = monthStart.AddDays(day - 1);
                if (candidate < monthEnd)
                {
                    labelValues.Add(DateTimeAxis.ToDouble(candidate));
                }
            }

            // Minimum/Maximumを明示指定しているため、Series側にデータが1件もない（=対象月に
            // 該当プレイが無い）場合でも、横軸は常にこの月の範囲で表示される。
            model.Axes.Add(new FixedTickDateTimeAxis(gridlineValues, labelValues)
            {
                Position = AxisPosition.Bottom,
                StringFormat = "M/d",
                FontSize = 10,
                Angle = 0,
                Minimum = DateTimeAxis.ToDouble(monthStart),
                Maximum = DateTimeAxis.ToDouble(monthEnd),
                IsZoomEnabled = false,
                IsPanEnabled = false,
                MajorGridlineStyle = LineStyle.Solid,
                MajorGridlineColor = OxyColor.FromAColor(40, _theme.OxyBorderColor),
                TicklineColor = _theme.OxyBorderColor,
            });

            model.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Left,
                Title = yAxisTitle,
                FontSize = 10,
                AxisTitleDistance = 7,
                IsZoomEnabled = false,
                IsPanEnabled = false,
                MajorGridlineStyle = LineStyle.Solid,
                MajorGridlineColor = OxyColor.FromAColor(40, _theme.OxyBorderColor),
                TicklineColor = _theme.OxyBorderColor,
            });

            // 平均±標準偏差の帯。
            var band = new AreaSeries
            {
                Fill = OxyColor.FromAColor(45, seriesColor),
                StrokeThickness = 0,
            };

            // 個々のプレイをそのままの再生時刻でプロットする散布図。
            var scatter = new ScatterSeries
            {
                MarkerType = MarkerType.Circle,
                MarkerSize = 1.25,
                MarkerFill = seriesColor,
                MarkerStroke = OxyColors.Transparent,
            };

            // 平均値の折れ線
            var meanLine = new LineSeries
            {
                Color = seriesColor,
                StrokeThickness = 1.5,
            };

            foreach (var stats in dailyStats)
            {
                var metric = dailyMetricSelector(stats);
                if (metric.SampleCount == 0) continue; // その日にその指標のデータがなければ点を打たない

                var x = DateTimeAxis.ToDouble(stats.Date.ToDateTime(TimeOnly.MinValue));
                band.Points.Add(new DataPoint(x, metric.Mean + metric.StdDev));
                band.Points2.Add(new DataPoint(x, metric.Mean - metric.StdDev));
                meanLine.Points.Add(new DataPoint(x, metric.Mean));
            }

            foreach (var entry in monthEntries)
            {
                var value = rawMetricSelector(entry);
                if (value is null) continue; // その指標が未計算のプレイは点を打たない

                var x = DateTimeAxis.ToDouble(entry.PlayedAt);
                scatter.Points.Add(new ScatterPoint(x, value.Value));
            }

            // Points.Count==0 の Series を model.Series に追加しない
            // 空のSeriesを混ぜると、対象月にデータが無いケースで描画が崩れる
            if (band.Points.Count > 0) model.Series.Add(band);
            if (scatter.Points.Count > 0) model.Series.Add(scatter);
            if (meanLine.Points.Count > 0) model.Series.Add(meanLine);

            return model;
        }

        /// <summary>
        /// 罫線とラベルの位置を個別に固定指定できるDateTimeAxis派生クラス。
        /// 標準の等間隔生成の制約を回避するため、<see cref="GetTickValues"/> をオーバーライドし、
        /// あらかじめ計算済みの値を返すことで自動間隔計算をバイパスする。
        /// </summary>
        private sealed class FixedTickDateTimeAxis : DateTimeAxis
        {
            private readonly IReadOnlyList<double> _gridlineValues;
            private readonly IReadOnlyList<double> _labelValues;

            public FixedTickDateTimeAxis(IReadOnlyList<double> gridlineValues, IReadOnlyList<double> labelValues)
            {
                _gridlineValues = gridlineValues;
                _labelValues = labelValues;
            }

            public override void GetTickValues(
                out IList<double> majorLabelValues, out IList<double> majorTickValues, out IList<double> minorTickValues)
            {
                // majorTickValues: 罫線・目盛線の位置（5日ごと）。
                // majorLabelValues: ラベル文字を描く位置（1日・10日・20日・30日）。
                majorTickValues = _gridlineValues.ToList();
                majorLabelValues = _labelValues.ToList();
                minorTickValues = new List<double>();
            }
        }
    }
}
