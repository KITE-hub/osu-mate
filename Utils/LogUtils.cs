using System.Diagnostics;
using System.IO;

namespace OsuMate.Utils;

internal static class LogUtils
{
  private const string ErrorFilePath = "Error.log";
  private static readonly object FileLock = new();

  internal static void DebugLogger(string message, bool error = false, bool writeToFile = false)
  {
    string currentDateString = DebugDateGenerator();

    try
    {
      if (error)
      {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine("[" + currentDateString + "] " + message);
        Console.ResetColor();
      }
    }
    catch (Exception ex)
    {
      Debug.WriteLine("LogUtils console output failed: " + ex.Message);
    }

    Debug.WriteLine("[" + currentDateString + "] " + message);

    if (!writeToFile)
      return;
    try
    {
      lock (FileLock)
      {
        File.AppendAllText(
          ErrorFilePath,
          "["
            + currentDateString
            + "]"
            + Environment.NewLine
            + message
            + Environment.NewLine
            + Environment.NewLine
        );
      }
    }
    catch (Exception ex)
    {
      Debug.WriteLine("LogUtils file output failed: " + ex.Message);
    }
  }

  private static string DebugDateGenerator() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
}
