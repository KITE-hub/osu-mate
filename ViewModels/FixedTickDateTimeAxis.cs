using OxyPlot.Axes;

namespace OsuMate.ViewModels
{
    /// <summary>
    /// 罫線(目盛線)の位置とラベルの表示位置を別々に固定指定できるDateTimeAxis派生クラス。
    /// 
    /// 標準のDateTimeAxisでは「罫線は等間隔・ラベルは特定の日に固定」といった要件を満たせないため、
    /// <see cref="GetTickValues"/> をオーバーライドして事前計算済みの値を返し、自動間隔計算をバイパスする。
    /// </summary>
    internal sealed class FixedTickDateTimeAxis : DateTimeAxis
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
            //   両者を別リストにできるのが GetTickValues をオーバーライドする狙い。
            // minorTickValues: 副目盛は使わないので空。
            majorTickValues = _gridlineValues.ToList();
            majorLabelValues = _labelValues.ToList();
            minorTickValues = new List<double>();
        }
    }
}
