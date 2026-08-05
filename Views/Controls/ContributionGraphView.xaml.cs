using System.Windows.Controls;

namespace OsuMate.Views.Controls
{
    /// <summary>
    /// PlayLogView上部に表示するコントリビューショングラフ（日ごとの合計打数ヒートマップ）。
    /// ロジックは一切持たず、DataContext（ContributionGraphViewModel）が保持する
    /// Days コレクションをそのままバインドして描画するだけの、純粋な表示専用コンポーネント。
    /// </summary>
    public partial class ContributionGraphView : UserControl
    {
        public ContributionGraphView()
        {
            InitializeComponent();
        }
    }
}
