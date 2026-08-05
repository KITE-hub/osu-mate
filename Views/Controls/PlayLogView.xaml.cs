using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using OsuMate.Models;
using OsuMate.ViewModels;

namespace OsuMate.Views.Controls
{
    public partial class PlayLogView : UserControl
    {
        public PlayLogView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        /// <summary>Settings など他タブ表示中に DataGrid の ItemsSource を切り離してリスナーを解除する。</summary>
        public void SuspendBinding()
        {
            LogGrid.ItemsSource = null;
        }

        /// <summary>Log タブに戻ったとき ItemsSource を再接続する。</summary>
        public void ResumeBinding()
        {
            if (DataContext is PlayLogViewModel vm)
                LogGrid.ItemsSource = vm.FilteredEntries;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is PlayLogViewModel oldVm)
                oldVm.PropertyChanged -= OnVmPropertyChanged;

            if (e.NewValue is PlayLogViewModel newVm)
            {
                newVm.PropertyChanged += OnVmPropertyChanged;
                LogGrid.ItemsSource = newVm.FilteredEntries;
                BuildAllColumns(newVm);
                UpdateColumnVisibility(newVm.ActiveColumns);
                UpdateModeFilterButtons(newVm);
            }
        }

        private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (DataContext is not PlayLogViewModel vm) return;

            if (e.PropertyName == nameof(PlayLogViewModel.ActiveColumns))
            {
                UpdateColumnVisibility(vm.ActiveColumns);
            }
            else if (e.PropertyName == nameof(PlayLogViewModel.SelectedModeCategory))
            {
                UpdateModeFilterButtons(vm);
            }
        }

        private void ModeFilterButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not PlayLogViewModel vm || sender is not Button button ||
                button.CommandParameter is not string value ||
                !Enum.TryParse<LogModeCategory>(value, out var category)) return;

            vm.SelectModeCategory(category);
        }

        private void UpdateModeFilterButtons(PlayLogViewModel vm)
        {
            var buttons = new[]
            {
                StandardFilterButton, TaikoFilterButton, CatchFilterButton,
                Mania4KFilterButton, Mania7KFilterButton, ManiaOtherFilterButton,
            };

            foreach (var button in buttons)
            {
                button.Tag = button.CommandParameter is string value &&
                    Enum.TryParse<LogModeCategory>(value, out var category) &&
                    category == vm.SelectedModeCategory
                    ? "Active"
                    : null;
            }
        }
    }
}
