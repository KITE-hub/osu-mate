using System.Windows.Controls;

namespace OsuMate.Views.Controls
{
    /// <summary>
    /// ContributionGraphView の直下に表示する、対象月の日ごとの合計打数（MISS以外のHit数の合計）の
    /// 推移グラフ（OxyPlot）。ロジックは一切持たず、DataContext（ContributionChartViewModel）が
    /// 保持する HitsPlotModel をそのままバインドして描画するだけの、純粋な表示専用コンポーネント。
    /// </summary>
    public partial class ContributionChartView : UserControl
    {
        public ContributionChartView()
        {
            InitializeComponent();
        }
    }
}
