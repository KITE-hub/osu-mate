using osu.Game.Rulesets.Scoring;
using OsuMate.Models;
using OsuMate.Services.Osu;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.Series;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Linq;

namespace OsuMate.ViewModels
{
    public class URTimeGraphViewModel : ObservableBase, IThemeable
    {
        private List<(double timeSec, double offsetMs)> _data = [];
        private double _totalTimeSec = 0;
        private double _currentTimeSec = 0;
        private double _maxWindow = 0;
        private Dictionary<HitResult, double> _hitWindows = [];
        private int _previousDataCount = 0;
        private LineSeries _currentTimeLine = new();

        // Render()で構築したシリーズ／平均ラインへの参照。判定ごとの差分追記（AppendNewPoints）で、
        // PlotModel/Seriesを作り直さずに既存インスタンスへ点を追加するために保持する。
        // Render()が呼ばれるたびに再生成されるため、常にnullではない状態で使われる想定。
        private ScatterSeries _perfectSeries = new();
        private ScatterSeries _greatSeries = new();
        private ScatterSeries _goodSeries = new();
        private ScatterSeries _okSeries = new();
        private ScatterSeries _mehSeries = new();
        private ScatterSeries _missSeries = new();
        private LineAnnotation _averageAnnotation = new();

        // 平均ライン算出用。_data全件を毎回Average()し直すのではなく、新規追加分のoffsetMsだけを
        // 加算していく（_previousDataCountと対で管理し、巻き戻り時はRender()内で一緒に0へリセットする）。
        private double _offsetSum = 0;

        public bool IsPlaying { get; set; } = false;

        private PlotModel _plotModel = new();
        public PlotModel PlotModel
        {
            get => _plotModel;
            private set { _plotModel = value; OnPropertyChanged(); }
        }

        private ThemeSettings _theme;
        public URTimeGraphViewModel(ThemeSettings theme)
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

            foreach (var series in _plotModel.Series)
            {
                if (series is LineSeries line)
                {
                    if (line == _currentTimeLine)
                        line.Color = _theme.OxyTextColor;
                    else if (series.Tag is int tag && tag == -1)
                        line.Color = _theme.OxyTextColor;
                }
                else if (series is ScatterSeries scatter && scatter.Tag is int judgement)
                {
                    var color = HitJudgementHelper.GetOxyColor(judgement, _theme);
                    scatter.MarkerFill = color;
                    scatter.MarkerStroke = color;
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
            Dictionary<HitResult, double> hitWindows,
            double totalTimeSec)
        {
            _hitWindows = hitWindows;
            _totalTimeSec = totalTimeSec;
            _maxWindow = HitJudgementHelper.GetMaxWindow(hitWindows);

            if (IsPlaying)
            {
                _data = [];
                _previousDataCount = 0;
                Render();
            }
        }

        public void Update(List<(double timeSec, double offsetMs)> currentData, double currentTimeSec, bool isPlaying)
        {
            bool wasPlaying = IsPlaying;
            int previousCount = _previousDataCount;
            _currentTimeSec = currentTimeSec;
            IsPlaying = isPlaying;

            if (!IsPlaying)
            {
                _data = currentData;
                return;
            }

            if (currentData.Count == 0)
            {
                _data = currentData;
                if (!wasPlaying || _plotModel.Series.Count > 0)
                {
                    PlotModel = new PlotModel();
                    _previousDataCount = 0;
                }
                return;
            }

            // データ増加なし → 縦線だけ更新（全系列の再構築は行わない）
            if (currentData.Count == previousCount && _plotModel.Series.Count > 0)
            {
                _data = currentData;
                MoveCurrentTimeLine();
                _plotModel.InvalidatePlot(false);
                return;
            }

            // 巻き戻り（リトライ）、または未構築の場合はゼロから作り直す
            if (currentData.Count < previousCount || _plotModel.Series.Count == 0)
            {
                _data = currentData;
                _previousDataCount = currentData.Count;
                Render();
                return;
            }

            // 新規追加分だけを既存シリーズへ差分反映する。件数分の全再分類・PlotModel/Series再構築は行わない。
            AppendNewPoints(currentData, previousCount, currentData.Count);
            _data = currentData;
            _previousDataCount = currentData.Count;
            MoveCurrentTimeLine();
            _plotModel.InvalidatePlot(false);
        }

        private void MoveCurrentTimeLine()
        {
            _currentTimeLine.Points.Clear();
            _currentTimeLine.Points.Add(new DataPoint(_currentTimeSec, -_maxWindow));
            _currentTimeLine.Points.Add(new DataPoint(_currentTimeSec, _maxWindow));
        }

        private (double perfect, double great, double good, double ok, double meh) GetWindows()
        {
            double GetWindow(HitResult r) => _hitWindows.TryGetValue(r, out var v) ? v : 0;
            return (GetWindow(HitResult.Perfect), GetWindow(HitResult.Great), GetWindow(HitResult.Good),
                    GetWindow(HitResult.Ok), GetWindow(HitResult.Meh));
        }

        /// <summary>
        /// data[fromIndex..toIndex) の新規追加分だけを対応するScatterSeriesへ振り分けて追加し、
        /// 平均ライン（_averageAnnotation.Y）を_offsetSumの差分更新から反映する。
        /// 呼び出し元は各シリーズ・_averageAnnotationがRender()で構築済みであることを保証すること。
        /// </summary>
        private void AppendNewPoints(List<(double timeSec, double offsetMs)> data, int fromIndex, int toIndex)
        {
            var (perfectWindow, greatWindow, goodWindow, okWindow, mehWindow) = GetWindows();

            for (int i = fromIndex; i < toIndex; i++)
            {
                var (timeSec, offsetMs) = data[i];
                double abs = Math.Abs(offsetMs);
                var point = new ScatterPoint(timeSec, offsetMs);

                if (perfectWindow > 0 && abs <= perfectWindow) _perfectSeries.Points.Add(point);
                else if (greatWindow > 0 && abs <= greatWindow) _greatSeries.Points.Add(point);
                else if (goodWindow > 0 && abs <= goodWindow) _goodSeries.Points.Add(point);
                else if (okWindow > 0 && abs <= okWindow) _okSeries.Points.Add(point);
                else if (mehWindow > 0 && abs <= mehWindow) _mehSeries.Points.Add(point);
                else _missSeries.Points.Add(point);

                _offsetSum += offsetMs;
            }

            if (toIndex > 0)
                _averageAnnotation.Y = _offsetSum / toIndex;
        }

        private static ScatterSeries MakeSeries(int judgement, ThemeSettings theme) => new()
        {
            MarkerType = MarkerType.Circle,
            MarkerSize = 1,
            MarkerFill = HitJudgementHelper.GetOxyColor(judgement, theme),
            MarkerStroke = HitJudgementHelper.GetOxyColor(judgement, theme),
            MarkerStrokeThickness = 1,
            Tag = judgement
        };

        public void Render()
        {
            if (_data.Count == 0)
            {
                PlotModel = new PlotModel();
                _previousDataCount = 0;
                _offsetSum = 0;
                return;
            }

            if (!IsPlaying) return;

            double yMin = -_maxWindow;
            double yMax = _maxWindow;

            _perfectSeries = MakeSeries(1, _theme);
            _greatSeries = MakeSeries(2, _theme);
            _goodSeries = MakeSeries(3, _theme);
            _okSeries = MakeSeries(4, _theme);
            _mehSeries = MakeSeries(5, _theme);
            _missSeries = MakeSeries(6, _theme);

            var model = new PlotModel
            {
                DefaultFont = _theme.OxyFontFamily,
                Background = OxyColors.Transparent,
                PlotAreaBackground = OxyColors.Transparent,
                TextColor = _theme.OxyTextColor,
                DefaultFontSize = 12F,
                PlotAreaBorderColor = _theme.OxyBorderColor,
                Padding = new OxyThickness(1, 1, 1, 1),
                IsLegendVisible = false
            };

            // 現在位置の縦線
            _currentTimeLine = new LineSeries
            {
                Color = _theme.OxyTextColor,
                StrokeThickness = 2,
                MarkerType = MarkerType.None
            };
            _currentTimeLine.Points.Add(new DataPoint(_currentTimeSec, yMin));
            _currentTimeLine.Points.Add(new DataPoint(_currentTimeSec, yMax));
            model.Series.Add(_currentTimeLine);

            model.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Bottom,
                Title = "Time [s]",
                Minimum = 0,
                Maximum = _totalTimeSec,
                IsZoomEnabled = false,
                IsPanEnabled = false,
                TicklineColor = _theme.OxyBorderColor
            });
            model.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Left,
                Title = "Offset [ms]",
                TitleFontSize = 11F,
                Minimum = yMin,
                Maximum = yMax,
                IsZoomEnabled = false,
                IsPanEnabled = false,
                TicklineColor = _theme.OxyBorderColor
            });

            // 差分追記（AppendNewPoints）で判定ごとにmodel.Seriesへの動的Add/Removeが要らないよう、
            // 6種類のScatterSeriesは中身が空でも常に登録しておく（空シリーズは何も描画しないため見た目への影響はない）
            model.Series.Add(_missSeries);
            model.Series.Add(_mehSeries);
            model.Series.Add(_okSeries);
            model.Series.Add(_goodSeries);
            model.Series.Add(_greatSeries);
            model.Series.Add(_perfectSeries);

            // ゼロライン
            var zeroLine = new LineSeries
            {
                Color = _theme.OxyBorderColor,
                StrokeThickness = 1,
                LineStyle = LineStyle.Dash,
                Dashes = [4, 4],
                Tag = -1
            };
            zeroLine.Points.Add(new DataPoint(0, 0));
            zeroLine.Points.Add(new DataPoint(_totalTimeSec, 0));
            model.Series.Add(zeroLine);

            // 平均ライン（Yは後段のAppendNewPointsが_offsetSumの差分更新から設定する）
            _offsetSum = 0;
            _averageAnnotation = new LineAnnotation
            {
                Type = LineAnnotationType.Horizontal,
                Y = 0,
                Color = _theme.OxyTextColor,
                StrokeThickness = 1,
                LineStyle = LineStyle.Solid,
                Tag = "average"
            };
            model.Annotations.Add(_averageAnnotation);

            // EARLY / LATE ラベル
            model.Annotations.Add(new TextAnnotation
            {
                Text = "EARLY",
                TextPosition = new DataPoint(_totalTimeSec * 0.02, yMin * 0.95),
                TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Left,
                TextVerticalAlignment = VerticalAlignment.Bottom,
                Stroke = OxyColors.Transparent,
                Background = OxyColors.Transparent,
                TextColor = _theme.OxyTextColor,
                FontSize = 11F
            });
            model.Annotations.Add(new TextAnnotation
            {
                Text = "LATE",
                TextPosition = new DataPoint(_totalTimeSec * 0.02, yMax * 0.95),
                TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Left,
                TextVerticalAlignment = VerticalAlignment.Top,
                Stroke = OxyColors.Transparent,
                Background = OxyColors.Transparent,
                TextColor = _theme.OxyTextColor,
                FontSize = 11F
            });

            PlotModel = model;

            // Render()はゼロから作り直すケースでのみ呼ばれるため、ここでは全件をAppendNewPointsへまとめて渡してよい
            // （AppendNewPointsは新規分だけを処理する前提の共通ロジックだが、fromIndex=0であれば全件処理と等価）
            AppendNewPoints(_data, 0, _data.Count);
        }
    }
}