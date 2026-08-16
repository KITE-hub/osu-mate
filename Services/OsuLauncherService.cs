using System.Diagnostics;
using System.IO;
using OsuMate.Services.Osu;
using OsuMate.Utils;

namespace OsuMate.Services
{
  public class OsuLauncherService
  {
    public bool TryAutoLaunch(string? path)
    {
      if (string.IsNullOrWhiteSpace(path))
        return false;

      try
      {
        var (running, _, _, _) = ProcessUtils.GetOsuProcess();
        if (running)
          return false;

        if (!File.Exists(path))
        {
          LogUtils.DebugLogger($"OsuLauncherService.TryAutoLaunch: file not found: {path}", true);
          return false;
        }

        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        return true;
      }
      catch (Exception e)
      {
        LogUtils.DebugLogger("OsuLauncherService.TryAutoLaunch failed: " + e.Message, true);
        return false;
      }
    }
  }
}
