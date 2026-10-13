using OsuMate.Utils;
using OsuMate.ViewModels;

namespace OsuMate.Services
{
  public sealed class AppLifecycle(
    WindowManagerService windowManager,
    ModePresetSwitcher modePresetSwitcher,
    MainViewModel mainViewModel,
    PlayLogViewModel playLogViewModel,
    TrainerViewModel trainerViewModel
  )
  {
    public void Start()
    {
      windowManager.Start();
      mainViewModel.Start();
      modePresetSwitcher.Start();

      _ = Task.Run(async () =>
      {
        try
        {
          await playLogViewModel.LoadAsync();
        }
        catch (Exception ex)
        {
          LogUtils.DebugLogger("AppLifecycle.Start: PlayLog load failed: " + ex, true, writeToFile: true);
        }
      });
    }

    public async Task StopAsync()
    {
      modePresetSwitcher.Dispose();
      windowManager.HideOverlays();
      windowManager.Dispose();
      await mainViewModel.StopAsync();
      trainerViewModel.Dispose();
      playLogViewModel.Dispose();
    }
  }
}
