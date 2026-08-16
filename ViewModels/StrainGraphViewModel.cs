using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using OsuMate.Models;
using OsuMate.Utils;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;

namespace OsuMate.ViewModels
{
  public class StrainGraphViewModel : ObservableBase, IThemeable
  {
    private OxyColor GetBlue() =>
      ColorUtils.OxyFromHsl(216, _theme.PlotSaturation, _theme.PlotLightness);

    private OxyColor GetGreen() =>
      ColorUtils.OxyFromHsl(144, _theme.PlotSaturation, _theme.PlotLightness);

    private OxyColor GetYellow() =>
      ColorUtils.OxyFromHsl(72, _theme.PlotSaturation, _theme.PlotLightness);

    private OxyColor GetRed() =>
      ColorUtils.OxyFromHsl(0, _theme.PlotSaturation, _theme.PlotLightness);

    private OxyColor GetPink() =>
      ColorUtils.OxyFromHsl(288, _theme.PlotSaturation, _theme.PlotLightness);

    public SolidColorBrush BlueBrush { get; private set; }
    public SolidColorBrush GreenBrush { get; private set; }
    public SolidColorBrush YellowBrush { get; private set; }
    public SolidColorBrush RedBrush { get; private set; }
    public SolidColorBrush PinkBrush { get; private set; }

    private List<float[]> _values = [];
    private string[] _labels = [];
    private double _currentTimeMs = 0;
    private double _lastStrainTimeModified = 0;
    private double _firstObjectTimeModified = 0;
    private double _speedMultiplier = 1.0;
    private LineSeries? _currentTimeLine;
    private float _lastMaxValue = 1f;
    private DateTime _lastRenderedAt = DateTime.MinValue;

    public bool RhythmVisible { get; set; } = true;
    public bool ReadingVisible { get; set; } = true;
    public bool ColourVisible { get; set; } = true;
    public bool Stamina1Visible { get; set; } = true;
    public bool Stamina2Visible { get; set; } = true;

    public string RhythmLabel => GetLabel(0);
    public string ReadingLabel => GetLabel(1);
    public string ColourLabel => GetLabel(2);
    public string Stamina1Label => GetLabel(3);
    public string Stamina2Label => GetLabel(4);

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

    public StrainGraphViewModel(ThemeSettings theme)
    {
      _theme = theme;
      BlueBrush = new SolidColorBrush(
        ColorUtils.FromHsl(216, _theme.PlotSaturation, _theme.PlotLightness)
      );
      GreenBrush = new SolidColorBrush(
        ColorUtils.FromHsl(144, _theme.PlotSaturation, _theme.PlotLightness)
      );
      YellowBrush = new SolidColorBrush(
        ColorUtils.FromHsl(72, _theme.PlotSaturation, _theme.PlotLightness)
      );
      RedBrush = new SolidColorBrush(
        ColorUtils.FromHsl(0, _theme.PlotSaturation, _theme.PlotLightness)
      );
      PinkBrush = new SolidColorBrush(
        ColorUtils.FromHsl(288, _theme.PlotSaturation, _theme.PlotLightness)
      );
    }

    public void ApplyTheme(ThemeSettings theme)
    {
      _theme = theme;
      RenderThemeOnly();
    }

    private void RenderThemeOnly()
    {
      BlueBrush = new SolidColorBrush(
        ColorUtils.FromHsl(216, _theme.PlotSaturation, _theme.PlotLightness)
      );
      GreenBrush = new SolidColorBrush(
        ColorUtils.FromHsl(144, _theme.PlotSaturation, _theme.PlotLightness)
      );
      YellowBrush = new SolidColorBrush(
        ColorUtils.FromHsl(72, _theme.PlotSaturation, _theme.PlotLightness)
      );
      RedBrush = new SolidColorBrush(
        ColorUtils.FromHsl(0, _theme.PlotSaturation, _theme.PlotLightness)
      );
      PinkBrush = new SolidColorBrush(
        ColorUtils.FromHsl(288, _theme.PlotSaturation, _theme.PlotLightness)
      );

      OnPropertyChanged(nameof(BlueBrush));
      OnPropertyChanged(nameof(GreenBrush));
      OnPropertyChanged(nameof(YellowBrush));
      OnPropertyChanged(nameof(RedBrush));
      OnPropertyChanged(nameof(PinkBrush));

      if (_plotModel.Series.Count == 0)
        return;

      _plotModel.TextColor = _theme.OxyTextColor;
      _plotModel.PlotAreaBorderColor = _theme.OxyBorderColor;
      _plotModel.DefaultFont = _theme.OxyFontFamily;

      foreach (var axis in _plotModel.Axes)
        axis.TicklineColor = _theme.OxyBorderColor;

      var skillColors = new[] { GetBlue(), GetGreen(), GetYellow(), GetRed(), GetPink() };
      foreach (var line in _plotModel.Series.OfType<LineSeries>())
      {
        if (line == _currentTimeLine)
          line.Color = _theme.OxyTextColor;
        else if (line.Tag is int skillIndex && skillIndex >= 0 && skillIndex < skillColors.Length)
          line.Color = skillColors[skillIndex];
      }

      _plotModel.InvalidatePlot(false);
    }

    public void SetData(
      List<float[]> values,
      string[] labels,
      double lastStrainTimeModified,
      double firstObjectTimeModified,
      double speedMultiplier
    )
    {
      _values = values;
      _labels = labels;
      _lastStrainTimeModified = lastStrainTimeModified;
      _firstObjectTimeModified = firstObjectTimeModified;
      _speedMultiplier = speedMultiplier;

      OnPropertyChanged(nameof(RhythmLabel));
      OnPropertyChanged(nameof(ReadingLabel));
      OnPropertyChanged(nameof(ColourLabel));
      OnPropertyChanged(nameof(Stamina1Label));
      OnPropertyChanged(nameof(Stamina2Label));

      Render();
    }

    public void Update(int time, int dataUpdateIntervalMs)
    {
      if (_values.Count == 0)
        return;
      if (_lastStrainTimeModified == 0)
        return;

      _currentTimeMs = Math.Max(0, time * _speedMultiplier - _firstObjectTimeModified);

      if (_currentTimeLine != null && _plotModel.Series.Count > 0)
      {
        _currentTimeLine.Points.Clear();
        _currentTimeLine.Points.Add(new DataPoint(_currentTimeMs / 1000.0, 0));
        _currentTimeLine.Points.Add(new DataPoint(_currentTimeMs / 1000.0, _lastMaxValue));
        if (TryMarkRendered(ref _lastRenderedAt, dataUpdateIntervalMs))
          _plotModel.InvalidatePlot(false);
        return;
      }

      Render();
    }

    public void Render()
    {
      if (_values.Count == 0)
      {
        _currentTimeLine = null;
        PlotModel = new PlotModel();
        return;
      }

      int totalCount = _values[0].Length > 0 ? _values.Max(v => v.Length) : 0;
      if (totalCount == 0)
        return;

      float maxValue = 0f;
      for (int i = 0; i < totalCount; i++)
      {
        for (int j = 0; j < 5; j++)
          if (GetValue(i, j) > maxValue)
            maxValue = GetValue(i, j);
      }
      if (maxValue <= 0)
        maxValue = 1;
      _lastMaxValue = maxValue;

      var model = new PlotModel
      {
        DefaultFont = _theme.OxyFontFamily,
        Background = OxyColors.Transparent,
        PlotAreaBackground = OxyColors.Transparent,
        TextColor = _theme.OxyTextColor,
        DefaultFontSize = 12F,
        PlotAreaBorderColor = _theme.OxyBorderColor,
        Padding = new OxyThickness(1, 1, 1, 1),
        IsLegendVisible = false,
      };

      model.Axes.Add(
        new LinearAxis
        {
          Position = AxisPosition.Bottom,
          Minimum = 0,
          Maximum = _lastStrainTimeModified / 1000.0,
          IsZoomEnabled = false,
          IsPanEnabled = false,
          TicklineColor = _theme.OxyBorderColor,
        }
      );
      model.Axes.Add(
        new LinearAxis
        {
          Position = AxisPosition.Left,
          Minimum = 0,
          Maximum = maxValue,
          IsZoomEnabled = false,
          IsPanEnabled = false,
          TicklineColor = _theme.OxyBorderColor,
        }
      );

      var series = new (LineSeries s, bool visible)[]
      {
        (
          new LineSeries
          {
            Color = GetBlue(),
            StrokeThickness = 2,
            MarkerType = MarkerType.None,
            Tag = 0,
          },
          RhythmVisible
        ),
        (
          new LineSeries
          {
            Color = GetGreen(),
            StrokeThickness = 2,
            MarkerType = MarkerType.None,
            Tag = 1,
          },
          ReadingVisible
        ),
        (
          new LineSeries
          {
            Color = GetYellow(),
            StrokeThickness = 2,
            MarkerType = MarkerType.None,
            Tag = 2,
          },
          ColourVisible
        ),
        (
          new LineSeries
          {
            Color = GetRed(),
            StrokeThickness = 2,
            MarkerType = MarkerType.None,
            Tag = 3,
          },
          Stamina1Visible
        ),
        (
          new LineSeries
          {
            Color = GetPink(),
            StrokeThickness = 2,
            MarkerType = MarkerType.None,
            Tag = 4,
          },
          Stamina2Visible
        ),
      };

      for (int i = 0; i < totalCount; i++)
      {
        double t = i * 400 / 1000.0;
        for (int j = 0; j < 5; j++)
          series[j].s.Points.Add(new DataPoint(t, GetValue(i, j)));
      }

      foreach (var (s, visible) in series)
        if (visible)
          model.Series.Add(s);

      _currentTimeLine = new LineSeries
      {
        Color = _theme.OxyBorderColor,
        StrokeThickness = 2,
        MarkerType = MarkerType.None,
      };
      _currentTimeLine.Points.Add(new DataPoint(_currentTimeMs / 1000.0, 0));
      _currentTimeLine.Points.Add(new DataPoint(_currentTimeMs / 1000.0, maxValue));
      model.Series.Add(_currentTimeLine);

      PlotModel = model;
    }

    private string GetLabel(int index) => _labels.Length > index ? _labels[index] : "-";

    private float GetValue(int index, int time)
    {
      if (_values.Count <= time)
        return 0;
      return _values[time].Length > index ? _values[time][index] : 0;
    }
  }
}
