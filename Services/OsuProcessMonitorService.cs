using OsuMate.Utils;
using System.Diagnostics;

namespace OsuMate.Services
{
    /// <summary>
    /// osu!本体プロセスの検出・追跡と、そのメインウィンドウの座標(Win32 Rect)取得
    /// </summary>
    public class OsuProcessMonitorService
    {
        private Process? _osuProcess;

        /// <summary>
        /// 現在保持しているosu!プロセスのメインウィンドウRectを取得する。
        /// プロセスが見つからない/終了している/ウィンドウハンドルが無い場合は false。
        /// </summary>
        public bool TryGetOsuWindowRect(out Win32Interop.Win32Rect rect)
        {
            if (_osuProcess != null && !_osuProcess.HasExited)
            {
                IntPtr handle = _osuProcess.MainWindowHandle;
                if (handle != IntPtr.Zero && Win32Interop.GetWindowRect(handle, out rect))
                    return true;
            }
            rect = default;
            return false;
        }

        /// <summary>
        /// osu!プロセスをまだ保持していない、または終了していれば再検出する。
        /// </summary>
        /// <returns>有効なプロセスを保持できていれば true。</returns>
        public bool EnsureProcess()
        {
            if (_osuProcess == null || _osuProcess.HasExited)
            {
                _osuProcess?.Dispose();
                _osuProcess = Process.GetProcessesByName("osu!").FirstOrDefault();
            }
            return _osuProcess != null;
        }

        /// <summary>
        /// 16ms間隔でosu!プロセス・ウィンドウ位置を監視し、有効なRectが取得できるたびに
        /// onWindowRectAvailable を呼び出す（呼び出しの完了を待ってから次のTickへ進む）。
        /// UIスレッドへのディスパッチも含めた反映は呼び出し元（WindowManagerService）の責務とする。
        /// </summary>
        public async Task StartTrackingAsync(Func<Win32Interop.Win32Rect, Task> onWindowRectAvailable, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(16).ConfigureAwait(false);
                    if (ct.IsCancellationRequested) break;

                    if (!EnsureProcess()) continue;
                    if (!TryGetOsuWindowRect(out var rect)) continue;

                    await onWindowRectAvailable(rect).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { break; }
                catch { }
            }
        }
    }
}
