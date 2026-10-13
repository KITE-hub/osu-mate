using System.Diagnostics;
using System.IO;

namespace OsuMate.Utils
{

  internal static class LogUtils
  {
    private const long MaxLogFileBytes = 2L * 1024 * 1024;
    private static readonly string ErrorFilePath = Path.Combine(AppContext.BaseDirectory, "Error.log");
    private static readonly object FileLock = new();
    private static volatile bool _consoleAvailable = true;

    internal static void DebugLogger(string message, bool error = false, bool writeToFile = false)
    {
      string currentDateString = DebugDateGenerator();

      if (error && _consoleAvailable)
      {
        try
        {
          Console.ForegroundColor = ConsoleColor.Red;
          Console.Error.WriteLine("[" + currentDateString + "] " + message);
          Console.ResetColor();
        }
        catch (Exception ex)
        {
          _consoleAvailable = false;
          Debug.WriteLine("LogUtils console output disabled: " + ex.Message);
        }
      }

      Debug.WriteLine("[" + currentDateString + "] " + message);

      if (!writeToFile)
        return;
      try
      {
        lock (FileLock)
        {
          var fileInfo = new FileInfo(ErrorFilePath);
          if (fileInfo.Exists && fileInfo.Length > MaxLogFileBytes)
            File.Move(ErrorFilePath, ErrorFilePath + ".old", overwrite: true);

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
}
