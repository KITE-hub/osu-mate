using OsuMate.Models;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;

namespace OsuMate.ViewModels
{
    /// <summary>
    /// ContributionGraph直下に表示する、対象月における日ごとの合計打数の推移グラフ用ViewModel。
    /// 集計や状態管理は行わず、受け取ったデータからOxyPlotのPlotModelへの変換・表示のみを担う。
    /// </summary>
    public class ContributionChartViewModel : ObservableBase, IThemeable
    {
        private ThemeSettings _theme;

        private PlotModel _hitsPlotModel = new() { Title = "Hits" };

        /// <summary>選択中の月の、日ごとの合計打数（MISS以外のHit数の合計）の推移。</summary>
        public PlotModel HitsPlotModel
        {
            get => _hitsPlotModel;
            private set { _hitsPlotModel = value; OnPropertyChanged(); }
        }

        public ContributionChartViewModel(ThemeSettings theme)
        {
            _theme = theme;

            // フィールド初期化子で作った空モデル（Recalculate() が呼ばれるまでの初期表示用）にも
            // 起動時点のテーマを反映しておく。これをしないと、初回 Recalculate() までの間だけ
            // OxyPlot既定色（黒文字等）で一瞬表示されてしまう（PlayStatsChartViewModelと同じ対応）。
            ApplyThemeToModel(_hitsPlotModel);
        }

        /// <summary>
        /// 渡された日次ヒット数の集計結果から、<paramref name="month"/> 内の推移を
        /// 折れ線 + 散布図の PlotModel として作り直す。
        /// 呼び出し側（PlayLogViewModel）のフィルタ状態が変わったとき、および
        /// ContributionGraphViewModel.CurrentMonth（ComboBoxでの月選択）が変わったときの
        /// 両方で呼び出される想定（PlayStatsChartViewModel.Recalculateと同じ呼び出しタイミング）。
        /// </summary>
        /// <param name="dailyHits">
        /// 日付ごとの合計打数（MISS以外のHit数の合計）。ContributionGraphViewModel.DailyHits
        /// （= PlayLogAggregationService.AggregateDailyHits の結果）をそのまま渡す想定。
        /// 選択中モードで絞り込み済みの全期間分を含んでいてよく、ここで対象月分だけに絞り込む。
        /// </param>
        /// <param name="month">
        /// 横軸として表示する対象月（その月の1日を表す DateOnly）。
        /// ContributionGraphViewModel.CurrentMonth と同じ月を渡すことで、真上に表示される
        /// コントリビューショングラフと期間が揃うようにする。
        /// </param>
        public void Recalculate(IReadOnlyDictionary<DateOnly, int> dailyHits, DateOnly month)
        {
            HitsPlotModel = BuildPlotModel(dailyHits, month);
        }

        /// <summary>
        /// PlayStatsChartViewModel.ApplyTheme と同じ契約。
        /// データの再集計は行わず、既存モデルの「地の色」（文字・枠線・グリッド線・フォント）と
        /// 系列色（アクセントカラー）だけを差し替えて再描画する。
        /// </summary>
        public void ApplyTheme(ThemeSettings theme)
        {
            _theme = theme;
            ApplyThemeToModel(_hitsPlotModel);

            // 系列色（Line/Scatter）はテーマのアクセントカラーに連動するため、地の色だけでなく
            // 系列自体もテーマ変更のたびに作り直す必要がある。ApplyTheme時点では直近の
            // Recalculate引数を保持していないため、既存モデルのSeriesに設定済みの色を
            // 直接差し替える（PlotModelを作り直さない分、Recalculateの再実行は不要で済む）。
            foreach (var series in _hitsPlotModel.Series)
            {
                switch (series)
                {
                    case LineSeries line:
                        line.Color = _theme.OxyAccentColor;
                        break;
                    case ScatterSeries scatter:
                        scatter.MarkerFill = _theme.OxyAccentColor;
                        break;
                }
            }

            _hitsPlotModel.InvalidatePlot(false);
        }

        /// <summary>
        /// PlotModel 1枚分の、テーマに依存する「地の色」だけを差し替える。
        /// 系列色（アクセントカラー）は <see cref="ApplyTheme"/> 側で別途扱う。
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
        /// 対象月の、日ごとの合計打数を「折れ線(推移) + 散布図(日次の点)」を持つ PlotModel に変換する。
        /// 横軸は <paramref name="month"/> の月初〜翌月月初に固定する（PlayStatsChartViewModelと同じ
        /// 考え方。データが疎な月でも「1ヶ月分」の期間であることが見た目にも分かるようにするため）。
        /// </summary>
        private PlotModel BuildPlotModel(IReadOnlyDictionary<DateOnly, int> dailyHits, DateOnly month)
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
                // ContributionGraphViewの左端（曜日ヘッダー分の余白込み）とできる限り揃うよう、
                // PlayStatsChartViewModelと同じ固定余白を踏襲する。
                PlotMargins = new OxyThickness(52, double.NaN, 12, 28),
            };

            var monthStart = month.ToDateTime(TimeOnly.MinValue);
            var monthEnd = month.AddMonths(1).ToDateTime(TimeOnly.MinValue);

            // 罫線(目盛線)は5日ごと。PlayStatsChartViewModel.BuildPlotModelと同じ間隔にすることで、
            // PlayLogView内に並ぶグラフ同士の見た目を揃える。
            var gridlineValues = new List<double>();
            for (var d = monthStart; d < monthEnd; d = d.AddDays(5))
            {
                gridlineValues.Add(DateTimeAxis.ToDouble(d));
            }

            // ラベルは 1日・10日・20日・30日 のみ（PlayStatsChartViewModelと同じ）。
            var labelValues = new List<double>();
            foreach (var day in new[] { 1, 10, 20, 30 })
            {
                var candidate = monthStart.AddDays(day - 1);
                if (candidate < monthEnd)
                {
                    labelValues.Add(DateTimeAxis.ToDouble(candidate));
                }
            }

            // Minimum/Maximumを明示指定しているため、対象月にプレイが1件も無い場合でも
            // 横軸は常にこの月の範囲で表示される。
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
                Title = "Valid Hits",
                FontSize = 9,
                TitleFontSize = 10,
                AxisTitleDistance = 7,
                Minimum = 0,
                IsZoomEnabled = false,
                IsPanEnabled = false,
                MajorGridlineStyle = LineStyle.Solid,
                MajorGridlineColor = OxyColor.FromAColor(40, _theme.OxyBorderColor),
                TicklineColor = _theme.OxyBorderColor,
            });

            // 折れ線(推移)。
            var line = new LineSeries
            {
                Color = _theme.OxyAccentColor,
                StrokeThickness = 1.5,
            };

            // 日次の点。ContributionGraphの各マスと1対1に対応する値を強調するため、
            // 折れ線の上に重ねて表示する。
            var scatter = new ScatterSeries
            {
                MarkerType = MarkerType.Circle,
                MarkerSize = 2,
                MarkerFill = _theme.OxyAccentColor,
                MarkerStroke = OxyColors.Transparent,
            };

            // ContributionGraphViewModel.RebuildDaysと同じ「当月表示時、今日より未来の日は
            // 作成しない」というルールに合わせる。プレイが無い日も0として点を打つことで、
            // 「その月内でどれだけ活動量に波があったか」が折れ線としてそのまま見える
            // （ContributionGraphのヒートマップは色の濃淡、こちらは折れ線という違う切り口で
            //   同じ日次データを補完し合う関係にしている）。
            var today = DateOnly.FromDateTime(DateTime.Now);
            var daysInMonth = DateTime.DaysInMonth(month.Year, month.Month);

            for (int day = 1; day <= daysInMonth; day++)
            {
                var date = new DateOnly(month.Year, month.Month, day);
                if (date > today) break;

                var hits = dailyHits.GetValueOrDefault(date);
                var x = DateTimeAxis.ToDouble(date.ToDateTime(TimeOnly.MinValue));

                line.Points.Add(new DataPoint(x, hits));
                scatter.Points.Add(new ScatterPoint(x, hits));
            }

            // Points.Count==0 の Series を model.Series に追加しない
            // （PlayStatsChartViewModel.BuildPlotModelと同じ理由。対象月がまだ1日も
            //   経過していない等でループが1回も回らない場合の保険）。
            if (line.Points.Count > 0) model.Series.Add(line);
            if (scatter.Points.Count > 0) model.Series.Add(scatter);

            return model;
        }
    }
}
