using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using OsuMate.ViewModels;

namespace OsuMate.Views.Controls
{
    public partial class StrainGraphPanel : UserControl
    {
        private StrainGraphViewModel? ViewModel =>
            DataContext as StrainGraphViewModel;

        public StrainGraphPanel()
        {
            InitializeComponent();
        }

        private void CheckChanged(object sender, System.Windows.RoutedEventArgs e)
        {
            ViewModel?.Render();
        }
    }
}
