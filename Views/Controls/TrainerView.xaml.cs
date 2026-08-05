using OsuMate.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace OsuMate.Views.Controls
{
    public partial class TrainerView : UserControl
    {
        private TrainerViewModel? Vm => DataContext as TrainerViewModel;

        public TrainerView()
        {
            InitializeComponent();
        }

        /// <summary>他のタブ表示中に呼び出し、ポーリングを一時停止する。</summary>
        public void SuspendBinding()
        {
            Vm?.PausePolling();
        }

        /// <summary>Trainerタブに戻ったとき呼び出し、ポーリングを再開する。</summary>
        public void ResumeBinding()
        {
            Vm?.ResumePolling();
        }

        /// <summary>
        /// UpdateSourceTrigger=LostFocusのTextBoxでEnterキーを押したときバインディングを確定
        /// </summary>
        private void RateTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && sender is TextBox tb)
            {
                // フォーカスを一瞬別に移してバインド更新を発火させる
                var binding = tb.GetBindingExpression(TextBox.TextProperty);
                binding?.UpdateSource();
                // Enterキーを処理済みとしてバブルを止める
                e.Handled = true;
            }
        }

        private async void Generate_Click(object sender, RoutedEventArgs e)
        {
            if (Vm == null) return;
            await Vm.GenerateAsync();
        }
    }
}
