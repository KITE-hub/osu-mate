using System.ComponentModel;
using System.Runtime.CompilerServices;
using MathNet.Numerics.Interpolation;
using osu.Game.Rulesets.Scoring;
using OsuMate.Models;
using OsuMate.Services.Osu;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.Series;

namespace OsuMate.ViewModels
{
  public class URDistGraphViewModel : ObservableBase, IThemeable
  {
    private const int MaxSegments = 16;
    private const double SampleStepMs = 1.0;

    private List<(double timeSec, double offsetMs)> _data = [];
    private Dictionary<HitResult, double> _hitWindows = [];
    private double _maxWindow = 0;
    private int _previousDataCount = 0;

    private readonly Dictionary<double, int> _histogram = [];
    private double _offsetSum = 0;
    private DateTime _lastRenderedAt = DateTime.MinValue;

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

    private LinearAxis _xAxis = new();
    private LinearAxis _yAxis = new();
    private LineAnnotation _averageAnnotation = new();
    private TextAnnotation _earlyAnnotation = new();
    private TextAnnotation _lateAnnotation = new();
    private LineSeries _zeroLine = new();
    private readonly List<LineSeries> _segmentPool = [];

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

      if (_plotModel.Series.Count == 0)
        return;

      foreach (var axis in _plotModel.Axes)
        axis.TicklineColor = _theme.OxyBorderColor;

      foreach (var series in _plotModel.Series.OfType<LineSeries>())
      {
        if (series.Tag is int tag)
        {
          series.Color =
            tag == -1 ? _theme.OxyBorderColor : HitJudgementHelper.GetOxyColor(tag, _theme);
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
      Dictionary<HitResult, double> hitWindows
    )
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
      bool isPlaying,
      int dataUpdateIntervalMs
    )
    {
      IsPlaying = isPlaying;
      _hitWindows = hitWindows;
      _maxWindow = HitJudgementHelper.GetMaxWindow(hitWindows);

      if (data.Count == _previousDataCount)
      {
        _data = data;
        return;
      }

      if (data.Count < _previousDataCount)
      {
        _histogram.Clear();
        _offsetSum = 0;
        _previousDataCount = 0;
      }

      AccumulateNewData(data, _previousDataCount, data.Count);
      _previousDataCount = data.Count;
      _data = data;

      if (!TryMarkRendered(ref _lastRenderedAt, dataUpdateIntervalMs))
        return;

      if (_plotModel.Series.Count == 0)
        Render();
      else
        RefreshSeries();
    }

    private void AccumulateNewData(
      List<(double timeSec, double offsetMs)> data,
      int fromIndex,
      int toIndex
    )
    {
      for (int i = fromIndex; i < toIndex; i++)
      {
        double offset = data[i].offsetMs;
        double key = Math.Floor(offset);
        _histogram[key] = _histogram.TryGetValue(key, out int count) ? count + 1 : 1;
        _offsetSum += offset;
      }
    }

    private readonly record struct CurvePoint(double X, double Y, int Judgement);

    private bool TryBuildCurve(
      out double yMin,
      out double yMax,
      out double xMax,
      out double averageY,
      out List<CurvePoint> points
    )
    {
      yMin = 0;
      yMax = 0;
      xMax = 0;
      averageY = 0;
      points = [];

      int dataCount = _previousDataCount;
      if (dataCount == 0)
        return false;

      double maxWindow = _maxWindow;
      yMin = -maxWindow;
      yMax = maxWindow;

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
        return false;

      xMax = Math.Ceiling(xValues.Max() * 10) / 10;
      averageY = _offsetSum / dataCount;

      var spline = CubicSpline.InterpolateNatural(yValues, xValues);

      for (double y = yValues[0]; y <= yValues[^1]; y += SampleStepMs)
      {
        double x = spline.Interpolate(y);
        if (x < 0)
          x = 0;
        int judgement = HitJudgementHelper.GetJudgement(y, _hitWindows);
        points.Add(new CurvePoint(x, y, judgement));
      }

      return true;
    }

    private void EnsureSegmentPool()
    {
      _segmentPool.Clear();
      for (int i = 0; i < MaxSegments; i++)
        _segmentPool.Add(new LineSeries { StrokeThickness = 3 });
    }

    private int ApplySegments(List<CurvePoint> points)
    {
      int segmentIndex = 0;
      int currentJudgement = points[0].Judgement;
      LineSeries currentSeries = _segmentPool[0];
      currentSeries.Points.Clear();
      currentSeries.Color = HitJudgementHelper.GetOxyColor(currentJudgement, _theme);
      currentSeries.Tag = currentJudgement;

      for (int i = 0; i < points.Count; i++)
      {
        var point = points[i];
        if (point.Judgement != currentJudgement)
        {
          currentSeries.Points.Add(new DataPoint(point.X, point.Y));

          currentJudgement = point.Judgement;
          segmentIndex = Math.Min(segmentIndex + 1, MaxSegments - 1);
          currentSeries = _segmentPool[segmentIndex];
          currentSeries.Points.Clear();
          currentSeries.Color = HitJudgementHelper.GetOxyColor(currentJudgement, _theme);
          currentSeries.Tag = currentJudgement;
        }

        currentSeries.Points.Add(new DataPoint(point.X, point.Y));
      }

      int usedSegments = segmentIndex + 1;
      for (int i = usedSegments; i < _segmentPool.Count; i++)
        _segmentPool[i].Points.Clear();

      return usedSegments;
    }

    public void Render()
    {
      if (
        !TryBuildCurve(out double yMin, out double yMax, out double xMax, out double averageY, out var points)
      )
      {
        PlotModel = new PlotModel();
        return;
      }

      if (!IsPlaying)
        return;

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
        IsLegendVisible = false,
      };

      _xAxis = new LinearAxis
      {
        Position = AxisPosition.Bottom,
        Title = "Freq",
        Minimum = 0,
        Maximum = xMax,
        IsZoomEnabled = false,
        IsPanEnabled = false,
        MinorTickSize = 0,
        MaximumMajorIntervalCount = 3,
        MajorStep = xMax / 2,
        TicklineColor = _theme.OxyBorderColor,
      };
      model.Axes.Add(_xAxis);

      _yAxis = new LinearAxis
      {
        Position = AxisPosition.Left,
        Minimum = yMin,
        Maximum = yMax,
        IsZoomEnabled = false,
        IsPanEnabled = false,
        TicklineColor = _theme.OxyBorderColor,
        IsAxisVisible = false,
      };
      model.Axes.Add(_yAxis);

      _averageAnnotation = new LineAnnotation
      {
        Type = LineAnnotationType.Horizontal,
        Y = averageY,
        Color = _theme.OxyTextColor,
        StrokeThickness = 1,
        LineStyle = LineStyle.Solid,
        Tag = "average",
      };
      model.Annotations.Add(_averageAnnotation);

      _earlyAnnotation = new TextAnnotation
      {
        Text = "EARLY",
        TextPosition = new DataPoint(xMax * 0.1, yMin * 0.95),
        TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Left,
        TextVerticalAlignment = VerticalAlignment.Bottom,
        Stroke = OxyColors.Transparent,
        Background = OxyColors.Transparent,
        TextColor = _theme.OxyTextColor,
        FontSize = 10F,
      };
      model.Annotations.Add(_earlyAnnotation);

      _lateAnnotation = new TextAnnotation
      {
        Text = "LATE",
        TextPosition = new DataPoint(xMax * 0.1, yMax * 0.95),
        TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Left,
        TextVerticalAlignment = VerticalAlignment.Top,
        Stroke = OxyColors.Transparent,
        Background = OxyColors.Transparent,
        TextColor = _theme.OxyTextColor,
        FontSize = 10F,
      };
      model.Annotations.Add(_lateAnnotation);

      _zeroLine = new LineSeries
      {
        Color = _theme.OxyBorderColor,
        Tag = -1,
        StrokeThickness = 1,
        LineStyle = LineStyle.Dash,
        Dashes = [4, 4],
      };
      _zeroLine.Points.Add(new DataPoint(0, 0));
      _zeroLine.Points.Add(new DataPoint(xMax, 0));
      model.Series.Add(_zeroLine);

      EnsureSegmentPool();
      foreach (var series in _segmentPool)
        model.Series.Add(series);
      ApplySegments(points);

      PlotModel = model;
    }

    private void RefreshSeries()
    {
      if (
        !TryBuildCurve(out double yMin, out double yMax, out double xMax, out double averageY, out var points)
      )
      {
        PlotModel = new PlotModel();
        return;
      }

      if (!IsPlaying)
        return;

      _xAxis.Maximum = xMax;
      _xAxis.MajorStep = xMax / 2;

      _yAxis.Minimum = yMin;
      _yAxis.Maximum = yMax;

      _averageAnnotation.Y = averageY;
      _earlyAnnotation.TextPosition = new DataPoint(xMax * 0.1, yMin * 0.95);
      _lateAnnotation.TextPosition = new DataPoint(xMax * 0.1, yMax * 0.95);

      _zeroLine.Points.Clear();
      _zeroLine.Points.Add(new DataPoint(0, 0));
      _zeroLine.Points.Add(new DataPoint(xMax, 0));

      ApplySegments(points);

      _plotModel.InvalidatePlot(false);
    }
  }
}
