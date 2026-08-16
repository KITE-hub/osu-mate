using System;
using System.Windows;
using System.Windows.Interop;

namespace OsuMate.Utils
{
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
