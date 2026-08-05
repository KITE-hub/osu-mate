using System.Diagnostics;
using System.IO;

namespace OsuMate.Services.Osu;

internal static class ProcessUtils
{
    internal static (bool running, string path, IntPtr handle, int pid) GetOsuProcess()
    {
        Process[] processes = GetProcesses("osu!");
        if (processes.Length == 0) return (false, "", IntPtr.Zero, -1);

        Process osuProcess = processes[0];
        int pid = osuProcess.Id;

        try
        {
            ProcessModule? osuModule = osuProcess.MainModule;
            if (osuModule == null) return (true, "", osuProcess.MainWindowHandle, pid);

            string? osuDirectory = Path.GetDirectoryName(osuModule.FileName);
            if (osuDirectory == null || !Directory.Exists(osuDirectory)) return (true, "", osuProcess.MainWindowHandle, pid);

            return (true, osuDirectory, osuProcess.MainWindowHandle, pid);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // osu! の起動直後や権限の違いにより MainModule へのアクセスが拒否される場合があるため、パスを空にして返す
            return (true, "", osuProcess.MainWindowHandle, pid);
        }
        catch (Exception)
        {
            return (true, "", osuProcess.MainWindowHandle, pid);
        }
    }

    internal static Process[] GetProcesses(string executableName)
        => Process.GetProcessesByName(executableName);
}
