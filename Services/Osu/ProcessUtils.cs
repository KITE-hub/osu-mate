using System.Diagnostics;
using System.IO;
using System.Linq;

namespace OsuMate.Services.Osu
{

  internal static class ProcessUtils
  {
    internal const string OsuProcessName = "osu!";

    private const long CacheLifetimeMs = 500;
    private static readonly object CacheLock = new();
    private static (bool running, string path, IntPtr handle, int pid) _cachedProcess;
    private static long _cachedAtMs = long.MinValue;

    internal static (bool running, string path, IntPtr handle, int pid) GetOsuProcess()
    {
      lock (CacheLock)
      {
        var now = Environment.TickCount64;
        if (_cachedAtMs != long.MinValue && now - _cachedAtMs < CacheLifetimeMs)
          return _cachedProcess;

        _cachedProcess = QueryOsuProcess();
        _cachedAtMs = now;
        return _cachedProcess;
      }
    }

    private static (bool running, string path, IntPtr handle, int pid) QueryOsuProcess()
    {
      try
      {
        Process[] processes = GetProcesses(OsuProcessName);
        if (processes.Length == 0)
          return (false, "", IntPtr.Zero, -1);

        try
        {
          using Process osuProcess = processes[0];
          int pid = osuProcess.Id;
          IntPtr handle = osuProcess.MainWindowHandle;
          return (true, TryGetModuleDirectory(osuProcess) ?? "", handle, pid);
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
        OsuMate.Utils.LogUtils.DebugLogger("ProcessUtils.GetOsuProcess failed: " + ex, true);
        return (false, "", IntPtr.Zero, -1);
      }
    }

    private static string? TryGetModuleDirectory(Process process)
    {
      try
      {
        string? directory = Path.GetDirectoryName(process.MainModule?.FileName);
        return directory != null && Directory.Exists(directory) ? directory : null;
      }
      catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
      {
        return null;
      }
    }

    internal static Process[] GetProcesses(string executableName) =>
      Process.GetProcessesByName(executableName);
  }
}
