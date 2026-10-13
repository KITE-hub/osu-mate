using System.Diagnostics;
using System.Linq;
using OsuMate.Services.Osu;
using OsuMate.Utils;

namespace OsuMate.Services
{
  public class OsuProcessMonitorService : IDisposable
  {
    private const int ActiveDelayMs = 16;
    private const int IdleDelayMs = 1000;
    private const int HeartbeatMs = 250;

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
          _osuProcess = null;

          var (running, _, _, pid) = ProcessUtils.GetOsuProcess();
          if (running)
          {
            try
            {
              _osuProcess = Process.GetProcessById(pid);
            }
            catch (ArgumentException)
            {
              _osuProcess = null;
            }
          }
        }
        return _osuProcess != null;
      }
    }

    public async Task StartTrackingAsync(
      Func<Win32Interop.Win32Rect, Task> onWindowRectAvailable,
      CancellationToken ct
    )
    {
      int delayMs = ActiveDelayMs;
      bool hasLast = false;
      long lastDispatchTick = 0;
      Win32Interop.Win32Rect lastRect = default;

      while (!ct.IsCancellationRequested)
      {
        try
        {
          await Task.Delay(delayMs, ct).ConfigureAwait(false);
          if (ct.IsCancellationRequested)
            break;

          if (!EnsureProcess() || !TryGetOsuWindowRect(out var rect))
          {
            delayMs = IdleDelayMs;
            hasLast = false;
            continue;
          }

          delayMs = ActiveDelayMs;
          long now = Environment.TickCount64;
          if (hasLast && AreEqual(rect, lastRect) && now - lastDispatchTick < HeartbeatMs)
            continue;

          lastRect = rect;
          lastDispatchTick = now;
          hasLast = true;
          await onWindowRectAvailable(rect).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
          break;
        }
        catch (Exception ex)
        {
          LogUtils.DebugLogger("OsuProcessMonitorService tracking failed: " + ex, true);
        }
      }
    }

    private static bool AreEqual(Win32Interop.Win32Rect a, Win32Interop.Win32Rect b) =>
      a.Left == b.Left && a.Top == b.Top && a.Right == b.Right && a.Bottom == b.Bottom;

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
