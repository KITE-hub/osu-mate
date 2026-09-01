using System.Linq;
using OsuMate.Models;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;

namespace OsuMate.ViewModels
{
  public class PlayStatsChartViewModel : ObservableBase, IThemeable
  {
    private static readonly OxyColor StarRatingColor = OxyColors.SteelBlue;
    private static readonly OxyColor PpColor = OxyColors.DarkOrange;
    private static readonly OxyColor AccuracyColor = OxyColors.MediumSeaGreen;

    private ThemeSettings _theme;

    private PlotModel _starRatingPlotModel = new();

    public PlotModel StarRatingPlotModel
    {
      get => _starRatingPlotModel;
      private set
      {
        _starRatingPlotModel = value;
        OnPropertyChanged();
      }
    }

    private PlotModel _ppPlotModel = new();

    public PlotModel PpPlotModel
    {
      get => _ppPlotModel;
      private set
      {
        _ppPlotModel = value;
        OnPropertyChanged();
      }
    }

    private PlotModel _accuracyPlotModel = new();

    public PlotModel AccuracyPlotModel
    {
      get => _accuracyPlotModel;
      private set
      {
        _accuracyPlotModel = value;
        OnPropertyChanged();
      }
    }

    public PlayStatsChartViewModel(ThemeSettings theme)
    {
      _theme = theme;

      ApplyThemeToModel(_starRatingPlotModel);
      ApplyThemeToModel(_ppPlotModel);
      ApplyThemeToModel(_accuracyPlotModel);
    }

    public void Recalculate(
      IEnumerable<PlayLogEntry> filteredEntries,
      IReadOnlyDictionary<DateOnly, DailyPlayStats> dailyStats,
      DateOnly month
    )
    {
      var monthEntries = filteredEntries
        .Where(e => e.PlayedAt.Year == month.Year && e.PlayedAt.Month == month.Month)
        .ToList();

      var monthDailyStats = dailyStats
        .Values.Where(s => s.Date.Year == month.Year && s.Date.Month == month.Month)
        .OrderBy(s => s.Date)
        .ToList();

      StarRatingPlotModel = BuildPlotModel(
        "SR",
        monthDailyStats,
        monthEntries,
        s => s.StarRating,
        e => e.StarRating,
        StarRatingColor,
        month
      );
      PpPlotModel = BuildPlotModel(
        "pp",
        monthDailyStats,
        monthEntries,
        s => s.Pp,
        e => e.Pp,
        PpColor,
        month
      );
      AccuracyPlotModel = BuildPlotModel(
        "Acc (%)",
        monthDailyStats,
        monthEntries,
        s => s.Accuracy,
        e => e.Accuracy,
        AccuracyColor,
        month
      );
    }

    public void ApplyTheme(ThemeSettings theme)
    {
      _theme = theme;
      ApplyThemeToModel(_starRatingPlotModel);
      ApplyThemeToModel(_ppPlotModel);
      ApplyThemeToModel(_accuracyPlotModel);
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

    private PlotModel BuildPlotModel(
      string yAxisTitle,
      IReadOnlyList<DailyPlayStats> dailyStats,
      IReadOnlyList<PlayLogEntry> monthEntries,
      Func<DailyPlayStats, MetricStat> dailyMetricSelector,
      Func<PlayLogEntry, double?> rawMetricSelector,
      OxyColor seriesColor,
      DateOnly month
    )
    {
      var model = new PlotModel
      {
        Background = OxyColors.Transparent,
        PlotAreaBackground = OxyColors.Transparent,
        TextColor = _theme.OxyTextColor,
        DefaultFont = _theme.OxyFontFamily,
        DefaultFontSize = 11F,
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
          Title = yAxisTitle,
          FontSize = 10,
          AxisTitleDistance = 7,
          IsZoomEnabled = false,
          IsPanEnabled = false,
          MajorGridlineStyle = LineStyle.Solid,
          MajorGridlineColor = OxyColor.FromAColor(40, _theme.OxyBorderColor),
          TicklineColor = _theme.OxyBorderColor,
        }
      );

      var band = new AreaSeries
      {
        Fill = OxyColor.FromAColor(45, seriesColor),
        StrokeThickness = 0,
        TrackerFormatString = "Date: {2}\n{3}: {4}",
      };

      var scatter = new ScatterSeries
      {
        MarkerType = MarkerType.Circle,
        MarkerSize = 1.25,
        MarkerFill = seriesColor,
        MarkerStroke = OxyColors.Transparent,
        TrackerFormatString = "Date: {2}\n{3}: {4}",
      };

      var meanLine = new LineSeries
      {
        Color = seriesColor,
        StrokeThickness = 1.5,
        TrackerFormatString = "Date: {2}\n{3}: {4}",
      };

      foreach (var stats in dailyStats)
      {
        var metric = dailyMetricSelector(stats);
        if (metric.SampleCount == 0)
          continue;

        var x = DateTimeAxis.ToDouble(stats.Date.ToDateTime(TimeOnly.MinValue));
        band.Points.Add(new DataPoint(x, metric.Mean + metric.StdDev));
        band.Points2.Add(new DataPoint(x, metric.Mean - metric.StdDev));
        meanLine.Points.Add(new DataPoint(x, metric.Mean));
      }

      foreach (var entry in monthEntries)
      {
        var value = rawMetricSelector(entry);
        if (value is null)
          continue;

        var x = DateTimeAxis.ToDouble(entry.PlayedAt);
        scatter.Points.Add(new ScatterPoint(x, value.Value));
      }

      if (band.Points.Count > 0)
        model.Series.Add(band);
      if (scatter.Points.Count > 0)
        model.Series.Add(scatter);
      if (meanLine.Points.Count > 0)
        model.Series.Add(meanLine);

      return model;
    }
  }
}
