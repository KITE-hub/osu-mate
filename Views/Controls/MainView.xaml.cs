using OsuMate.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace OsuMate.Views.Controls
{
    public partial class MainView : UserControl
    {
        public MainView()
        {
            InitializeComponent();
            // DataContext（MainViewModel）はコンストラクタの実行時点ではまだ設定されていないため、
            // DataContextChanged で受け取ってから各子パネルへサブVMを配布する。
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is not MainViewModel vm) return;

            InfoPanel.DataContext = vm.Info;
            StrainGraphPanel.DataContext = vm.StrainGraph;
            URTimeGraph.DataContext = vm.URTimeGraph;
            URDistGraph.DataContext = vm.URDistGraph;
        }
    }
}
