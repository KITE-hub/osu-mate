using System;
using System.Runtime.InteropServices;

namespace OsuMate.Utils
{
  public static class Win32Interop
  {
    [StructLayout(LayoutKind.Sequential)]
    public struct Win32Rect
    {
      public int Left,
        Top,
        Right,
        Bottom;
    }

    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOZORDER = 0x0004;

    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_LAYERED = 0x00080000;
    public const int WS_EX_TRANSPARENT = 0x00000020;

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out Win32Rect rect);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(
      IntPtr hWnd,
      IntPtr hWndInsertAfter,
      int X,
      int Y,
      int cx,
      int cy,
      uint uFlags
    );

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
  }
}
