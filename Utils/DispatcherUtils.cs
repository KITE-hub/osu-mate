using System.Windows;

namespace OsuMate.Utils
{

  internal static class DispatcherUtils
  {
    internal static void RunOnPrimary(Action action)
    {
      var dispatcher = Application.Current?.Dispatcher;
      if (dispatcher == null)
        return;

      if (dispatcher.CheckAccess())
        action();
      else
        _ = dispatcher.InvokeAsync(action);
    }

    internal static bool IsRecoverable(Exception exception) =>
      exception is not (OutOfMemoryException or AccessViolationException or StackOverflowException);
  }
}
