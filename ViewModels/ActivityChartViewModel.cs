using OsuMate.Models;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;

namespace OsuMate.ViewModels
{
  public class ActivityChartViewModel : ObservableBase, IThemeable
  {
    private ThemeSettings _theme;

    private PlotModel _hitsPlotModel = new() { Title = "Hits" };

    public PlotModel HitsPlotModel
    {
      get => _hitsPlotModel;
      private set
      {
        _hitsPlotModel = value;
        OnPropertyChanged();
      }
    }

    public ActivityChartViewModel(ThemeSettings theme)
    {
      _theme = theme;

      ApplyThemeToModel(_hitsPlotModel);
    }

    public void Recalculate(IReadOnlyDictionary<DateOnly, int> dailyHits, DateOnly month)
    {
      HitsPlotModel = BuildPlotModel(dailyHits, month);
    }

    public void ApplyTheme(ThemeSettings theme)
    {
      _theme = theme;
      ApplyThemeToModel(_hitsPlotModel);

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

        PlotMargins = new OxyThickness(52, double.NaN, 12, 28),
      };

      var monthStart = month.ToDateTime(TimeOnly.MinValue);
      var monthEnd = month.AddMonths(1).ToDateTime(TimeOnly.MinValue);

      var gridlineValues = new List<double>();
      for (var d = monthStart; d < monthEnd; d = d.AddDays(5))
      {
        gridlineValues.Add(DateTimeAxis.ToDouble(d));
      }

      var labelValues = new List<double>();
      foreach (var day in new[] { 1, 10, 20, 30 })
      {
        var candidate = monthStart.AddDays(day - 1);
        if (candidate < monthEnd)
        {
          labelValues.Add(DateTimeAxis.ToDouble(candidate));
        }
      }

      model.Axes.Add(
        new FixedTickDateTimeAxis(gridlineValues, labelValues)
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
        }
      );

      model.Axes.Add(
        new LinearAxis
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
        }
      );

      var line = new LineSeries
      {
        Color = _theme.OxyAccentColor,
        StrokeThickness = 1.5,
        TrackerFormatString = "Date: {2}\n{3}: {4}",
      };

      var scatter = new ScatterSeries
      {
        MarkerType = MarkerType.Circle,
        MarkerSize = 2,
        MarkerFill = _theme.OxyAccentColor,
        MarkerStroke = OxyColors.Transparent,
        TrackerFormatString = "Date: {2}\n{3}: {4}",
      };

      var today = DateOnly.FromDateTime(DateTime.Now);
      var daysInMonth = DateTime.DaysInMonth(month.Year, month.Month);

      for (int day = 1; day <= daysInMonth; day++)
      {
        var date = new DateOnly(month.Year, month.Month, day);
        if (date > today)
          break;

        var hits = dailyHits.GetValueOrDefault(date);
        var x = DateTimeAxis.ToDouble(date.ToDateTime(TimeOnly.MinValue));

        line.Points.Add(new DataPoint(x, hits));
        scatter.Points.Add(new ScatterPoint(x, hits));
      }

      if (line.Points.Count > 0)
        model.Series.Add(line);
      if (scatter.Points.Count > 0)
        model.Series.Add(scatter);

      return model;
    }
  }
}
