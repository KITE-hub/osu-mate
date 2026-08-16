using OxyPlot.Axes;

namespace OsuMate.ViewModels
{
  internal sealed class FixedTickDateTimeAxis : DateTimeAxis
  {
    private readonly IReadOnlyList<double> _gridlineValues;
    private readonly IReadOnlyList<double> _labelValues;

    public FixedTickDateTimeAxis(
      IReadOnlyList<double> gridlineValues,
      IReadOnlyList<double> labelValues
    )
    {
      _gridlineValues = gridlineValues;
      _labelValues = labelValues;
    }

    public override void GetTickValues(
      out IList<double> majorLabelValues,
      out IList<double> majorTickValues,
      out IList<double> minorTickValues
    )
    {
      majorTickValues = _gridlineValues.ToList();
      majorLabelValues = _labelValues.ToList();
      minorTickValues = new List<double>();
    }
  }
}
