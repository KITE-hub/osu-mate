using System.Windows.Controls;

namespace OsuMate.Views.Controls
{
    /// <summary>
    /// PlayLogView右側に表示する、月ごとの SR / pp / Acc（平均±標準偏差）推移グラフ（3枚, OxyPlot）。
    /// ロジックは一切持たず、DataContext（PlayStatsChartViewModel）が保持する
    /// 3つの PlotModel をそのままバインドして描画するだけの、純粋な表示専用コンポーネント。
    /// </summary>
    public partial class PlayStatsChartView : UserControl
    {
        public PlayStatsChartView()
        {
            InitializeComponent();
        }
    }
}
