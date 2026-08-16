using System.Windows;
using System.Windows.Controls;
using OsuMate.ViewModels;

namespace OsuMate.Views.Controls
{
  public partial class MainView : UserControl
  {
    public MainView()
    {
      InitializeComponent();

      DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
      if (e.NewValue is not MainViewModel vm)
        return;

      InfoPanel.DataContext = vm.Info;
      StrainGraphPanel.DataContext = vm.StrainGraph;
      URTimeGraph.DataContext = vm.URTimeGraph;
      URDistGraph.DataContext = vm.URDistGraph;
    }
  }
}
