using System;
using System.Runtime.InteropServices;

namespace OsuMate.Utils
{
  public static class TimerResolution
  {
    private const uint TimerNoError = 0;

    private static int _appliedPeriodMs;

    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod", SetLastError = true)]
    private static extern uint TimeBeginPeriod(uint uPeriod);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod", SetLastError = true)]
    private static extern uint TimeEndPeriod(uint uPeriod);

    public static void Begin(int periodMs)
    {
      if (_appliedPeriodMs != 0)
        return;
      try
      {
        if (TimeBeginPeriod((uint)periodMs) == TimerNoError)
          _appliedPeriodMs = periodMs;
      }
      catch (Exception e)
      {
        LogUtils.DebugLogger($"TimerResolution.Begin failed: {e}", true);
      }
    }

    public static void End()
    {
      if (_appliedPeriodMs == 0)
        return;
      try
      {
        TimeEndPeriod((uint)_appliedPeriodMs);
      }
      catch (Exception e)
      {
        LogUtils.DebugLogger($"TimerResolution.End failed: {e}", true);
      }
      finally
      {
        _appliedPeriodMs = 0;
      }
    }
  }
}
