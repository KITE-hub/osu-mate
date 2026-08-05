using MathNet.Numerics.Interpolation;
using osu.Game.Rulesets.Scoring;
using OsuMate.Models;
using OsuMate.Services.Osu;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.Series;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace OsuMate.ViewModels
{
    public class URDistGraphViewModel : ObservableBase, IThemeable
    {
        private List<(double timeSec, double offsetMs)> _data = [];
        private Dictionary<HitResult, double> _hitWindows = [];
        private double _maxWindow = 0;
        private int _previousDataCount = 0;

        // offsetMsのヒストグラム（floor(offset) -> 件数）と合計値の永続キャッシュ。
        // 判定のたびに_data全件を舐めて作り直すのではなく、新規追加分（_previousDataCountから増えた分）だけを
        // 加算していく。巻き戻り（リトライ）検知時は_previousDataCountと一緒に0/Clearする
        // （MainViewModel.GetHitErrorsModified / HitErrorStatsAccumulator.Syncと同じ「差分追記・巻き戻りで作り直し」の方針）。
        // ウィンドウ幅（judgement window）には依存しない生カウントなので、mod変更等でウィンドウが変わっても
        // 作り直す必要はない（ウィンドウ依存の0件ビン補完はRender()側でローカルコピーに対して行う）。
        private readonly Dictionary<double, int> _histogram = [];
        private double _offsetSum = 0;

        public bool IsPlaying { get; set; } = false;

        private PlotModel _plotModel = new();
        public PlotModel PlotModel
        {
            get => _plotModel;
            private set { _plotModel = value; OnPropertyChanged(); }
        }

        private ThemeSettings _theme;

        public URDistGraphViewModel(ThemeSettings theme)
        {
            _theme = theme;
        }

        public void ApplyTheme(ThemeSettings theme)
        {
            _theme = theme;
            RenderThemeOnly();
        }

        private void RenderThemeOnly()
        {
            _plotModel.TextColor = _theme.OxyTextColor;
            _plotModel.PlotAreaBorderColor = _theme.OxyBorderColor;
            _plotModel.DefaultFont = _theme.OxyFontFamily;

            if (_plotModel.Series.Count == 0) return;

            foreach (var axis in _plotModel.Axes)
                axis.TicklineColor = _theme.OxyBorderColor;

            foreach (var series in _plotModel.Series.OfType<LineSeries>())
            {
                if (series.Tag is int tag)
                {
                    series.Color = tag == -1
                        ? _theme.OxyBorderColor
                        : HitJudgementHelper.GetOxyColor(tag, _theme);
                }
            }

            foreach (var annotation in _plotModel.Annotations)
            {
                if (annotation is LineAnnotation la)
                    la.Color = la.Tag is "average" ? _theme.OxyTextColor : _theme.OxyBorderColor;
                if (annotation is TextAnnotation ta)
                    ta.TextColor = _theme.OxyTextColor;
            }

            _plotModel.InvalidatePlot(false);
        }

        public void SetData(
            List<(double timeSec, double offsetMs)> data,
            Dictionary<HitResult, double> hitWindows)
        {
            _data = data;
            _hitWindows = hitWindows;
            _maxWindow = HitJudgementHelper.GetMaxWindow(hitWindows);

            _histogram.Clear();
            _offsetSum = 0;
            _previousDataCount = 0;
            AccumulateNewData(data, 0, data.Count);

            Render();
        }

        public void Update(
            List<(double timeSec, double offsetMs)> data,
            Dictionary<HitResult, double> hitWindows,
            bool isPlaying)
        {
            IsPlaying = isPlaying;
            _hitWindows = hitWindows;

            if (data.Count == _previousDataCount)
            {
                _data = data;
                return;
            }

            // リトライ等でHitErrorsが巻き戻った場合は、差分追記が安全でないため作り直す
            if (data.Count < _previousDataCount)
            {
                _histogram.Clear();
                _offsetSum = 0;
                _previousDataCount = 0;
            }

            AccumulateNewData(data, _previousDataCount, data.Count);
            _previousDataCount = data.Count;
            _data = data;

            Render();
        }

        /// <summary>
        /// data[fromIndex..toIndex) の新規追加分だけをヒストグラム(_histogram)と合計値(_offsetSum)へ積み上げる。
        /// _data全件を毎回舐め直す代わりに、新規分のみのO(増分件数)で済ませるための蓄積処理。
        /// </summary>
        private void AccumulateNewData(List<(double timeSec, double offsetMs)> data, int fromIndex, int toIndex)
        {
            for (int i = fromIndex; i < toIndex; i++)
            {
                double offset = data[i].offsetMs;
                double key = Math.Floor(offset);
                _histogram[key] = _histogram.TryGetValue(key, out int count) ? count + 1 : 1;
                _offsetSum += offset;
            }
        }

        public void Render()
        {
            if (_data.Count == 0)
            {
                PlotModel = new PlotModel();
                return;
            }

            if (!IsPlaying) return;

            // _previousDataCount / _histogram / _offsetSum は SetData / Update 側で
            // 新規追加分だけをすでに積み上げ済み（差分蓄積、_data.Countと同期している）。
            // ここで_data全件を舐め直すことはしない。
            int dataCount = _previousDataCount;

            double perfectWindow = HitJudgementHelper.GetWindow(_hitWindows, HitResult.Perfect);
            double greatWindow = HitJudgementHelper.GetWindow(_hitWindows, HitResult.Great);
            double goodWindow = HitJudgementHelper.GetWindow(_hitWindows, HitResult.Good);
            double okWindow = HitJudgementHelper.GetWindow(_hitWindows, HitResult.Ok);
            double mehWindow = HitJudgementHelper.GetWindow(_hitWindows, HitResult.Meh);
            double maxWindow = HitJudgementHelper.GetMaxWindow(_hitWindows);

            double yMin = -maxWindow;
            double yMax = maxWindow;

            // _histogramをそのまま書き換えると、ウィンドウ幅ぶんの0件ビン（下のTryAdd）が永続キャッシュに
            // 混入してしまう（次回Render時のウィンドウ幅が変わった場合に古いビンが残り得る）ため、
            // ここではローカルコピーに対してのみ0件ビンを補う。コピー自体のコストはヒストグラムの
            // キー数（≒ウィンドウ幅の範囲）に比例し、判定数（dataCount）には依存しない。
            var dict = new Dictionary<double, int>(_histogram);

            for (double i = Math.Floor(yMin); i <= Math.Ceiling(yMax); i += 1.0)
                dict.TryAdd(i, 0);

            var sortedDict = dict.OrderBy(x => x.Key).ToList();

            var compressedDict = new Dictionary<double, int>();
            for (int i = 0; i < sortedDict.Count; i += 5)
            {
                double yValue = sortedDict[i].Key;
                int sum = sortedDict.Skip(i).Take(5).Sum(x => x.Value);
                compressedDict[yValue] = sum;
            }

            var yValues = compressedDict.Select(x => x.Key).ToArray();
            var xValues = compressedDict.Select(x => (double)x.Value / dataCount).ToArray();

            if (yValues.Length < 2)
            {
                PlotModel = new PlotModel();
                return;
            }

            var spline = CubicSpline.InterpolateNatural(yValues, xValues);

            var allPoints = new List<(double x, double y, int judgement)>();
            const double step = 0.05;
            for (double y = yValues.First(); y <= yValues.Last(); y += step)
            {
                double x = spline.Interpolate(y);
                if (x < 0) x = 0;
                int judgement = HitJudgementHelper.GetJudgement(y, _hitWindows);
                allPoints.Add((x, y, judgement));
            }

            var seriesList = new List<LineSeries>();
            int currentJudgement = allPoints[0].judgement;
            LineSeries? currentSeries = null;

            for (int i = 0; i < allPoints.Count; i++)
            {
                var point = allPoints[i];
                if (point.judgement != currentJudgement || currentSeries == null)
                {
                    if (currentSeries != null && i > 0)
                        currentSeries.Points.Add(new DataPoint(point.x, point.y));

                    currentJudgement = point.judgement;
                    currentSeries = new LineSeries
                    {
                        Color = HitJudgementHelper.GetOxyColor(currentJudgement, _theme),
                        Tag = currentJudgement,
                        StrokeThickness = 3
                    };
                    seriesList.Add(currentSeries);
                    currentSeries.Points.Add(new DataPoint(point.x, point.y));
                }
                else
                {
                    currentSeries.Points.Add(new DataPoint(point.x, point.y));
                }
            }

            var zeroLine = new LineSeries
            {
                Color = _theme.OxyBorderColor,
                Tag = -1,
                StrokeThickness = 1,
                LineStyle = LineStyle.Dash,
                Dashes = [4, 4]
            };
            zeroLine.Points.Add(new DataPoint(0, 0));
            zeroLine.Points.Add(new DataPoint(Math.Ceiling(xValues.Max() * 10) / 10, 0));

            double averageY = _offsetSum / dataCount;

            var model = new PlotModel
            {
                PlotType = PlotType.XY,
                Background = OxyColors.Transparent,
                PlotAreaBackground = OxyColors.Transparent,
                TextColor = _theme.OxyTextColor,
                DefaultFont = _theme.OxyFontFamily,
                DefaultFontSize = 12F,
                PlotAreaBorderColor = _theme.OxyBorderColor,
                Padding = new OxyThickness(1, 1, 1, 1),
                IsLegendVisible = false
            };

            model.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Bottom,
                Title = "Freq",
                Minimum = 0,
                Maximum = Math.Ceiling(xValues.Max() * 10) / 10,
                IsZoomEnabled = false,
                IsPanEnabled = false,
                MinorTickSize = 0,
                MaximumMajorIntervalCount = 3,
                MajorStep = Math.Ceiling(xValues.Max() * 10) / 20,
                TicklineColor = _theme.OxyBorderColor
            });
            model.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Left,
                Minimum = yMin,
                Maximum = yMax,
                IsZoomEnabled = false,
                IsPanEnabled = false,
                TicklineColor = _theme.OxyBorderColor,
                // 左隣のURTimeGraphと同じOffset[ms]軸を重複表示するだけなので非表示にする
                // (Minimum/Maximumはスケール計算に使うため保持し、見た目のみ消す)
                IsAxisVisible = false
            });

            model.Annotations.Add(new LineAnnotation
            {
                Type = LineAnnotationType.Horizontal,
                Y = averageY,
                Color = _theme.OxyTextColor,
                StrokeThickness = 1,
                LineStyle = LineStyle.Solid,
                Tag = "average"   // ← 追加
            });

            model.Annotations.Add(new TextAnnotation
            {
                Text = "EARLY",
                TextPosition = new DataPoint(Math.Ceiling(xValues.Max() * 10) * 0.1 / 10, yMin * 0.95),
                TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Left,
                TextVerticalAlignment = VerticalAlignment.Bottom,
                Stroke = OxyColors.Transparent,
                Background = OxyColors.Transparent,
                TextColor = _theme.OxyTextColor,
                FontSize = 10F
            });
            model.Annotations.Add(new TextAnnotation
            {
                Text = "LATE",
                TextPosition = new DataPoint(Math.Ceiling(xValues.Max() * 10) * 0.1 / 10, yMax * 0.95),
                TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Left,
                TextVerticalAlignment = VerticalAlignment.Top,
                Stroke = OxyColors.Transparent,
                Background = OxyColors.Transparent,
                TextColor = _theme.OxyTextColor,
                FontSize = 10F
            });

            model.Series.Add(zeroLine);
            foreach (var series in seriesList)
                model.Series.Add(series);

            PlotModel = model;
        }
    }
}