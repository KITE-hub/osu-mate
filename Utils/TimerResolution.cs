using System;
using System.Runtime.InteropServices;

namespace OsuMate.Utils
{
  public static class TimerResolution
  {
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
        TimeBeginPeriod((uint)periodMs);
        _appliedPeriodMs = periodMs;
      }
      catch (Exception e)
      {
        LogUtils.DebugLogger($"TimerResolution.Begin failed: {e.Message}", true);
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
        LogUtils.DebugLogger($"TimerResolution.End failed: {e.Message}", true);
      }
      finally
      {
        _appliedPeriodMs = 0;
      }
    }
  }
}
