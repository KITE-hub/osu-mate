using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using osu.Game.Rulesets.Scoring;
using OsuMate.Models;
using OsuMate.Utils;
using OsuMate.Services.Osu;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.Series;

namespace OsuMate.ViewModels
{
  public class URTimeGraphViewModel : ObservableBase, IThemeable
  {
    private List<(double timeSec, double offsetMs)> _data = [];
    private double _totalTimeSec = 0;
    private double _currentTimeSec = 0;
    private double _maxWindow = 0;
    private HitJudgementWindows _windows;
    private int _previousDataCount = 0;
    private LineSeries _currentTimeLine = new();

    private ScatterSeries _perfectSeries = new();
    private ScatterSeries _greatSeries = new();
    private ScatterSeries _goodSeries = new();
    private ScatterSeries _okSeries = new();
    private ScatterSeries _mehSeries = new();
    private ScatterSeries _missSeries = new();
    private LineAnnotation _averageAnnotation = new();

    private double _offsetSum = 0;
    private DateTime _lastRenderedAt = DateTime.MinValue;

    private const double PlotAreaWidthPx = 250;
    private const int MaxRenderIntervalMs = 500;

    public int EffectiveRenderIntervalMs { get; private set; }

    private int ComputeRenderIntervalMs(int dataUpdateIntervalMs)
    {
      if (_totalTimeSec <= 0)
        return dataUpdateIntervalMs;

      double msPerPixel = _totalTimeSec * 1000.0 / PlotAreaWidthPx;
      double intervalMs = Math.Max(dataUpdateIntervalMs, msPerPixel);
      return (int)Math.Min(intervalMs, MaxRenderIntervalMs);
    }

    public bool IsPlaying { get; set; } = false;

    private PlotModel _plotModel = new();
    public PlotModel PlotModel
    {
      get => _plotModel;
      private set
      {
        _plotModel = value;
        OnPropertyChanged();
      }
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
      PlotThemeUtils.ApplyBaseColors(_plotModel, _theme);

      if (_plotModel.Series.Count == 0)
        return;

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
          var color = PlotThemeUtils.JudgementColor(judgement, _theme);
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

    public void SetData(Dictionary<HitResult, double> hitWindows, double totalTimeSec)
    {
      _windows = new HitJudgementWindows(hitWindows);
      _totalTimeSec = totalTimeSec;
      _maxWindow = _windows.Max;

      if (IsPlaying)
      {
        _data = [];
        _previousDataCount = 0;
        Render();
      }
    }

    public void Update(
      List<(double timeSec, double offsetMs)> currentData,
      double currentTimeSec,
      bool isPlaying,
      int dataUpdateIntervalMs
    )
    {
      bool wasPlaying = IsPlaying;
      int previousCount = _previousDataCount;
      _currentTimeSec = currentTimeSec;
      IsPlaying = isPlaying;
      int renderIntervalMs = ComputeRenderIntervalMs(dataUpdateIntervalMs);
      EffectiveRenderIntervalMs = renderIntervalMs;

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

      if (currentData.Count == previousCount && _plotModel.Series.Count > 0)
      {
        _data = currentData;
        if (TryMarkRendered(ref _lastRenderedAt, renderIntervalMs))
        {
          MoveCurrentTimeLine();
          _plotModel.InvalidatePlot(false);
        }
        return;
      }

      if (currentData.Count < previousCount || _plotModel.Series.Count == 0)
      {
        _data = currentData;
        _previousDataCount = currentData.Count;
        Render();
        return;
      }

      AppendNewPoints(currentData, previousCount, currentData.Count);
      _data = currentData;
      _previousDataCount = currentData.Count;
      if (TryMarkRendered(ref _lastRenderedAt, renderIntervalMs))
      {
        MoveCurrentTimeLine();
        _plotModel.InvalidatePlot(false);
      }
    }

    private void MoveCurrentTimeLine()
    {
      _currentTimeLine.Points.Clear();
      _currentTimeLine.Points.Add(new DataPoint(_currentTimeSec, -_maxWindow));
      _currentTimeLine.Points.Add(new DataPoint(_currentTimeSec, _maxWindow));
    }

    private void AppendNewPoints(
      List<(double timeSec, double offsetMs)> data,
      int fromIndex,
      int toIndex
    )
    {
      for (int i = fromIndex; i < toIndex; i++)
      {
        var (timeSec, offsetMs) = data[i];
        var point = new ScatterPoint(timeSec, offsetMs);

        var series = _windows.GetJudgement(offsetMs) switch
        {
          1 => _perfectSeries,
          2 => _greatSeries,
          3 => _goodSeries,
          4 => _okSeries,
          5 => _mehSeries,
          _ => _missSeries,
        };
        series.Points.Add(point);

        _offsetSum += offsetMs;
      }

      if (toIndex > 0)
        _averageAnnotation.Y = _offsetSum / toIndex;
    }

    private static ScatterSeries MakeSeries(int judgement, ThemeSettings theme) =>
      new()
      {
        MarkerType = MarkerType.Circle,
        MarkerSize = 1,
        MarkerFill = PlotThemeUtils.JudgementColor(judgement, theme),
        MarkerStroke = PlotThemeUtils.JudgementColor(judgement, theme),
        MarkerStrokeThickness = 1,
        Tag = judgement,
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

      if (!IsPlaying)
        return;

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
        DefaultFontSize = 11F,
        PlotAreaBorderColor = _theme.OxyBorderColor,
        Padding = new OxyThickness(1, 1, 1, 1),
        IsLegendVisible = false,
      };

      _currentTimeLine = new LineSeries
      {
        Color = _theme.OxyTextColor,
        StrokeThickness = 2,
        MarkerType = MarkerType.None,
      };
      _currentTimeLine.Points.Add(new DataPoint(_currentTimeSec, yMin));
      _currentTimeLine.Points.Add(new DataPoint(_currentTimeSec, yMax));
      model.Series.Add(_currentTimeLine);

      model.Axes.Add(
        new LinearAxis
        {
          Position = AxisPosition.Bottom,
          Title = "Time [s]",
          Minimum = 0,
          Maximum = _totalTimeSec,
          IsZoomEnabled = false,
          IsPanEnabled = false,
          TicklineColor = _theme.OxyBorderColor,
        }
      );
      model.Axes.Add(
        new LinearAxis
        {
          Position = AxisPosition.Left,
          Title = "Offset [ms]",
          TitleFontSize = 11F,
          Minimum = yMin,
          Maximum = yMax,
          IsZoomEnabled = false,
          IsPanEnabled = false,
          TicklineColor = _theme.OxyBorderColor,
        }
      );

      model.Series.Add(_missSeries);
      model.Series.Add(_mehSeries);
      model.Series.Add(_okSeries);
      model.Series.Add(_goodSeries);
      model.Series.Add(_greatSeries);
      model.Series.Add(_perfectSeries);

      var zeroLine = new LineSeries
      {
        Color = _theme.OxyBorderColor,
        StrokeThickness = 1,
        LineStyle = LineStyle.Dash,
        Dashes = [4, 4],
        Tag = -1,
      };
      zeroLine.Points.Add(new DataPoint(0, 0));
      zeroLine.Points.Add(new DataPoint(_totalTimeSec, 0));
      model.Series.Add(zeroLine);

      _offsetSum = 0;
      _averageAnnotation = new LineAnnotation
      {
        Type = LineAnnotationType.Horizontal,
        Y = 0,
        Color = _theme.OxyTextColor,
        StrokeThickness = 1,
        LineStyle = LineStyle.Solid,
        Tag = "average",
      };
      model.Annotations.Add(_averageAnnotation);

      model.Annotations.Add(
        new TextAnnotation
        {
          Text = "EARLY",
          TextPosition = new DataPoint(_totalTimeSec * 0.02, yMin * 0.95),
          TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Left,
          TextVerticalAlignment = VerticalAlignment.Bottom,
          Stroke = OxyColors.Transparent,
          Background = OxyColors.Transparent,
          TextColor = _theme.OxyTextColor,
          FontSize = 11F,
        }
      );
      model.Annotations.Add(
        new TextAnnotation
        {
          Text = "LATE",
          TextPosition = new DataPoint(_totalTimeSec * 0.02, yMax * 0.95),
          TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Left,
          TextVerticalAlignment = VerticalAlignment.Top,
          Stroke = OxyColors.Transparent,
          Background = OxyColors.Transparent,
          TextColor = _theme.OxyTextColor,
          FontSize = 11F,
        }
      );

      PlotModel = model;

      AppendNewPoints(_data, 0, _data.Count);
    }
  }
}
