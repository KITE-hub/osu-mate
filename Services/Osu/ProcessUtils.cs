using System.Diagnostics;
using System.IO;
using System.Linq;

namespace OsuMate.Services.Osu;

internal static class ProcessUtils
{
  internal static (bool running, string path, IntPtr handle, int pid) GetOsuProcess()
  {
    try
    {
      Process[] processes = GetProcesses("osu!");
      if (processes.Length == 0)
        return (false, "", IntPtr.Zero, -1);

      try
      {
        using Process osuProcess = processes[0];
        int pid = osuProcess.Id;
        IntPtr handle = osuProcess.MainWindowHandle;
        ProcessModule? osuModule = osuProcess.MainModule;
        if (osuModule == null)
          return (true, "", handle, pid);

        string? osuDirectory = Path.GetDirectoryName(osuModule.FileName);
        return osuDirectory == null || !Directory.Exists(osuDirectory)
          ? (true, "", handle, pid)
          : (true, osuDirectory, handle, pid);
      }
      finally
      {
        foreach (var process in processes.Skip(1))
          process.Dispose();
      }
    }
    catch (System.ComponentModel.Win32Exception)
    {
      return (false, "", IntPtr.Zero, -1);
    }
    catch (Exception ex)
    {
      OsuMate.Utils.LogUtils.DebugLogger("ProcessUtils.GetOsuProcess failed: " + ex.Message, true);
      return (false, "", IntPtr.Zero, -1);
    }
  }

  internal static Process[] GetProcesses(string executableName) =>
    Process.GetProcessesByName(executableName);
}
