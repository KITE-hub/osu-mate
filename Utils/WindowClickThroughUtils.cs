using System;
using System.Windows;
using System.Windows.Interop;

namespace OsuMate.Utils
{
    /// <summary>
    /// WPFウィンドウに対しWS_EX_TRANSPARENTを付け外しし、マウスクリックを背後のウィンドウへ
    /// 透過させるかどうかを切り替える拡張メソッド。
    /// AllowsTransparency="True" により WS_EX_LAYERED は WPF 側で既に設定されているため、
    /// ここでは WS_EX_TRANSPARENT ビットのみを操作する。
    /// </summary>
    public static class WindowClickThroughUtils
    {
        public static void SetClickThrough(this Window window, bool enabled)
        {
            IntPtr hwnd = new WindowInteropHelper(window).EnsureHandle();
            int exStyle = Win32Interop.GetWindowLongPtr(hwnd, Win32Interop.GWL_EXSTYLE).ToInt32();

            exStyle = enabled
                ? exStyle | Win32Interop.WS_EX_TRANSPARENT
                : exStyle & ~Win32Interop.WS_EX_TRANSPARENT;

            Win32Interop.SetWindowLongPtr(hwnd, Win32Interop.GWL_EXSTYLE, new IntPtr(exStyle));
        }
    }
}
