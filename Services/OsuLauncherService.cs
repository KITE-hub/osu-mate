using OsuMate.Services.Osu;
using OsuMate.Utils;
using System.Diagnostics;
using System.IO;

namespace OsuMate.Services
{
    /// <summary>
    /// osu mate起動時に、設定で指定された osu! の実行ファイル（.exe）またはショートカット（.lnk）を
    /// 自動起動するサービス
    /// </summary>
    public class OsuLauncherService
    {
        /// <summary>
        /// 指定されたパスで osu! の自動起動を試みる。
        /// 既に osu! プロセスが起動している場合（二重起動防止）、パスが未設定の場合、
        /// パスが指すファイルが存在しない場合は何もしない。
        /// </summary>
        /// <param name="path">起動対象の .exe または .lnk へのフルパス。</param>
        /// <returns>実際に起動を行った場合は true。</returns>
        public bool TryAutoLaunch(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;

            try
            {
                // 既にosu!が起動中なら何もしない（プライベートサーバー切り替え等で既に手動起動済みのケースを含む）
                var (running, _, _, _) = ProcessUtils.GetOsuProcess();
                if (running) return false;

                if (!File.Exists(path))
                {
                    LogUtils.DebugLogger($"OsuLauncherService.TryAutoLaunch: file not found: {path}", true);
                    return false;
                }

                // .lnk ショートカットの解決のため UseShellExecute=true が必須。
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
