using OsuMate.Utils;
using OsuMate.ViewModels;

namespace OsuMate.Services
{
  public sealed class AppLifecycle(
    WindowManagerService windowManager,
    MainViewModel mainViewModel,
    PlayLogViewModel playLogViewModel,
    TrainerViewModel trainerViewModel
  )
  {
    public void Start()
    {
      windowManager.Start();
      mainViewModel.Start();

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
      windowManager.HideOverlays();
      windowManager.Dispose();
      await mainViewModel.StopAsync();
      trainerViewModel.Dispose();
      playLogViewModel.Dispose();
    }
  }
}
