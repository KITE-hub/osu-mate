using OsuMate.Utils;

namespace OsuMate.Services
{
  internal sealed class RelativeWindowPosition
  {
    public double X { get; private set; }
    public double Y { get; private set; }

    public RelativeWindowPosition(double x, double y)
    {
      X = x;
      Y = y;
    }

    public void SetValue(double x, double y)
    {
      X = x;
      Y = y;
    }

    public void CaptureFromScreen(
      double screenLeft,
      double screenTop,
      Win32Interop.Win32Rect? osuRect
    )
    {
      if (osuRect is { } rect)
      {
        X = screenLeft - rect.Left;
        Y = screenTop - rect.Top;
      }
      else
      {
        X = screenLeft;
        Y = screenTop;
      }
    }

    public (double Left, double Top) ToScreen(Win32Interop.Win32Rect rect) =>
      (rect.Left + X, rect.Top + Y);
  }
}
