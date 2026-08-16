using System.Diagnostics;
using System.Linq;
using OsuMate.Utils;

namespace OsuMate.Services
{
  public class OsuProcessMonitorService : IDisposable
  {
    private Process? _osuProcess;
    private readonly object _processLock = new();

    public bool TryGetOsuWindowRect(out Win32Interop.Win32Rect rect)
    {
      lock (_processLock)
      {
        if (_osuProcess != null && !_osuProcess.HasExited)
        {
          IntPtr handle = _osuProcess.MainWindowHandle;
          if (handle != IntPtr.Zero && Win32Interop.GetWindowRect(handle, out rect))
            return true;
        }
      }
      rect = default;
      return false;
    }

    public bool EnsureProcess()
    {
      lock (_processLock)
      {
        if (_osuProcess == null || _osuProcess.HasExited)
        {
          _osuProcess?.Dispose();
          var processes = Process.GetProcessesByName("osu!");
          _osuProcess = processes.FirstOrDefault();
          foreach (var process in processes.Skip(1))
            process.Dispose();
        }
        return _osuProcess != null;
      }
    }

    public async Task StartTrackingAsync(
      Func<Win32Interop.Win32Rect, Task> onWindowRectAvailable,
      CancellationToken ct
    )
    {
      while (!ct.IsCancellationRequested)
      {
        try
        {
          await Task.Delay(16, ct).ConfigureAwait(false);
          if (ct.IsCancellationRequested)
            break;

          if (!EnsureProcess())
            continue;
          if (!TryGetOsuWindowRect(out var rect))
            continue;

          await onWindowRectAvailable(rect).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
          break;
        }
        catch (Exception ex)
        {
          LogUtils.DebugLogger("OsuProcessMonitorService tracking failed: " + ex.Message, true);
        }
      }
    }

    public void Dispose()
    {
      lock (_processLock)
      {
        _osuProcess?.Dispose();
        _osuProcess = null;
      }
    }
  }
}
