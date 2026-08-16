using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OsuMate.ViewModels;

namespace OsuMate.Views.Controls
{
  public partial class TrainerView : UserControl
  {
    private TrainerViewModel? Vm => DataContext as TrainerViewModel;

    public TrainerView()
    {
      InitializeComponent();
    }

    public void SuspendBinding()
    {
      Vm?.PausePolling();
    }

    public void ResumeBinding()
    {
      Vm?.ResumePolling();
    }

    private void RateTextBox_KeyDown(object sender, KeyEventArgs e)
    {
      if (e.Key == Key.Enter && sender is TextBox tb)
      {
        var binding = tb.GetBindingExpression(TextBox.TextProperty);
        binding?.UpdateSource();

        e.Handled = true;
      }
    }

    private async void Generate_Click(object sender, RoutedEventArgs e)
    {
      if (Vm == null)
        return;
      await Vm.GenerateAsync();
    }
  }
}
