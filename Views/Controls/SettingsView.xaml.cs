using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OsuMate.Models;
using OsuMate.Services.Osu;
using OsuMate.Utils;
using OsuMate.ViewModels;

namespace OsuMate.Views.Controls
{
  public partial class SettingsView : UserControl
  {
    private SettingsViewModel Vm => (SettingsViewModel)DataContext;

    private readonly DragReorderController<OverlayItem> _chipDrag;
    private readonly DragReorderController<LogColumnItem> _logColumnDrag;

    public SettingsView()
    {
      InitializeComponent();

      _chipDrag = new DragReorderController<OverlayItem>(
        this,
        ChipList,
        () => Vm.Items,
        (from, to) => Vm.MoveItem(from, to)
      );

      _logColumnDrag = new DragReorderController<LogColumnItem>(
        this,
        LogColumnList,
        () => Vm.LogColumnSettings.LogColumnItems,
        (from, to) => Vm.LogColumnSettings.MoveLogColumnItem(from, to)
      );
    }

    private void URBarRotateLeft_Click(object sender, System.Windows.RoutedEventArgs e)
    {
      if (DataContext is SettingsViewModel vm)
        vm.URBarRotation -= 90;
    }

    private void URBarRotateRight_Click(object sender, System.Windows.RoutedEventArgs e)
    {
      if (DataContext is SettingsViewModel vm)
        vm.URBarRotation += 90;
    }

    private void GetAppPosition_Click(object sender, RoutedEventArgs e)
    {
      var window = Window.GetWindow(this);
      if (window != null)
      {
        Vm.SetAppPosition(window.Left, window.Top);
      }
    }

    private void ResetAppPosition_Click(object sender, RoutedEventArgs e)
    {
      var window = Window.GetWindow(this);
      if (window != null)
      {
        window.Left = Vm.AppX;
        window.Top = Vm.AppY;
      }
    }

    private void GetOsuPosition_Click(object sender, RoutedEventArgs e)
    {
      var (_, _, handle, _) = ProcessUtils.GetOsuProcess();
      if (handle != IntPtr.Zero)
      {
        if (Win32Interop.GetWindowRect(handle, out var rect))
        {
          Vm.SetOsuPosition(rect.Left, rect.Top);
        }
      }
      else
      {
        MessageBox.Show(
          "osu! is not running.",
          "osu mate",
          MessageBoxButton.OK,
          MessageBoxImage.Information
        );
      }
    }

    private void ResetOsuPosition_Click(object sender, RoutedEventArgs e)
    {
      var (_, _, handle, _) = ProcessUtils.GetOsuProcess();
      if (handle != IntPtr.Zero)
      {
        Win32Interop.SetWindowPos(
          handle,
          IntPtr.Zero,
          (int)Vm.OsuX,
          (int)Vm.OsuY,
          0,
          0,
          Win32Interop.SWP_NOSIZE | Win32Interop.SWP_NOZORDER
        );
      }
      else
      {
        MessageBox.Show(
          "osu! is not running.",
          "osu mate",
          MessageBoxButton.OK,
          MessageBoxImage.Information
        );
      }
    }

    private void SaveURBarSize_Click(object sender, RoutedEventArgs e) => Vm.RequestSaveURBarSize();

    private void ApplyURBarSize_Click(object sender, RoutedEventArgs e) =>
      Vm.RequestApplyURBarSize();

    private void SaveURBarPosition_Click(object sender, RoutedEventArgs e) =>
      Vm.RequestSaveURBarPosition();

    private void ApplyURBarPosition_Click(object sender, RoutedEventArgs e) =>
      Vm.RequestApplyURBarPosition();

    private void SaveOverlayPosition_Click(object sender, RoutedEventArgs e) =>
      Vm.RequestSaveOverlayPosition();

    private void ApplyOverlayPosition_Click(object sender, RoutedEventArgs e) =>
      Vm.RequestApplyOverlayPosition();

    private void BrowseAutoLaunchOsuPath_Click(object sender, RoutedEventArgs e)
    {
      var dialog = new Microsoft.Win32.OpenFileDialog
      {
        Title = "Select the osu!.exe or shortcut(.lnk) for auto-launch",
        Filter =
          "osu! (*.exe;*.lnk)|*.exe;*.lnk|Executable (*.exe)|*.exe|Shortcut (*.lnk)|*.lnk|All files (*.*)|*.*",
        CheckFileExists = true,
      };

      var currentDir = Path.GetDirectoryName(Vm.AutoLaunchOsuPath);
      if (!string.IsNullOrWhiteSpace(currentDir) && Directory.Exists(currentDir))
        dialog.InitialDirectory = currentDir;

      if (dialog.ShowDialog() == true)
        Vm.AutoLaunchOsuPath = dialog.FileName;
    }

    public void InvalidateBitmapCache()
    {
      _chipDrag.InvalidateBitmapCache();

      _logColumnDrag.InvalidateBitmapCache();
      Dispatcher.BeginInvoke(
        () =>
        {
          if (DataContext is not SettingsViewModel)
            return;
          _chipDrag.CacheAllBitmaps();
        },
        System.Windows.Threading.DispatcherPriority.ApplicationIdle
      );
    }

    private void Item_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
      _chipDrag.OnItemMouseLeftButtonDown(sender, e);

    private void LogColumn_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
      _logColumnDrag.OnItemMouseLeftButtonDown(sender, e);

    private void NewPreset_Click(object sender, RoutedEventArgs e)
    {
      Vm.CreatePreset("New Preset");
    }

    private void DuplicatePreset_Click(object sender, RoutedEventArgs e)
    {
      var current = Vm.SelectedPreset;
      if (current == null)
        return;
      Vm.DuplicatePreset(current.Id, current.Name + " - Copy");
    }

    private void DeletePreset_Click(object sender, RoutedEventArgs e)
    {
      var current = Vm.SelectedPreset;
      if (current == null)
        return;

      if (!Vm.DeletePreset(current.Id))
      {
        MessageBox.Show(
          "You cannot delete the last preset.",
          "osu mate",
          MessageBoxButton.OK,
          MessageBoxImage.Information
        );
      }
    }

    private void RenamePreset_Click(object sender, RoutedEventArgs e)
    {
      var current = Vm.SelectedPreset;
      if (current == null)
        return;
      Vm.RenamePreset(current.Id, PresetNameBox.Text);
    }

    private void AddTargetPlayerName_Click(object sender, RoutedEventArgs e)
    {
      Vm.AddTargetPlayerName(TargetPlayerNameInput.Text);
      TargetPlayerNameInput.Clear();
    }

    private void TargetPlayerNameInput_KeyDown(object sender, KeyEventArgs e)
    {
      if (e.Key != Key.Enter)
        return;
      Vm.AddTargetPlayerName(TargetPlayerNameInput.Text);
      TargetPlayerNameInput.Clear();
    }

    private void RemoveTargetPlayerName_Click(object sender, RoutedEventArgs e)
    {
      if (sender is FrameworkElement { Tag: string name })
        Vm.RemoveTargetPlayerName(name);
    }

    private void CommitOnEnter_KeyDown(object sender, KeyEventArgs e)
    {
      if (e.Key == Key.Enter && sender is TextBox tb)
      {
        tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        e.Handled = true;
      }
    }
  }
}
