using System.Diagnostics;
using System.IO;

namespace OsuMate.Utils;

internal static class LogUtils
{
    private const string ErrorFilePath = "Error.log";

    internal static void DebugLogger(string message, bool error = false, bool writeToFile = false)
    {
        string currentDateString = DebugDateGenerator();

        try
        {
            if (error) Console.ForegroundColor = ConsoleColor.Red;
            Console.ResetColor();
        }
        catch
        {
            // コンソール未アタッチ環境（通常のGUI起動時）では System.IO.IOException 等が発生しうるため無視する
        }

        Debug.WriteLine("[" + currentDateString + "] " + message);

        if (!writeToFile) return;
        try
        {
            using StreamWriter sw = File.Exists(ErrorFilePath) ? File.AppendText(ErrorFilePath) : File.CreateText(ErrorFilePath);
            sw.WriteLine("[" + currentDateString + "]");
            sw.WriteLine(message);
            sw.WriteLine();
        }
        catch { }
    }

    private static string DebugDateGenerator()
        => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
}
