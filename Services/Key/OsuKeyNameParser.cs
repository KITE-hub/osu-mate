using System.Windows.Forms;

namespace OsuMate.Services.Key
{

  internal static class OsuKeyNameParser
  {
    internal static bool TryParse(string name, out Keys key)
    {
      if (TryParseDefined(name, out key))
        return true;

      var normalized = name switch
      {
        "LeftControl" => "LControlKey",
        "RightControl" => "RControlKey",
        "LeftShift" => "LShiftKey",
        "RightShift" => "RShiftKey",
        "LeftAlt" => "LMenu",
        "RightAlt" => "RMenu",
        "LeftWindows" => "LWin",
        "RightWindows" => "RWin",
        "Backspace" => "Back",
        "Enter" => "Return",
        _ => name,
      };

      return TryParseDefined(normalized, out key);
    }

    internal static string ToName(Keys key) =>
      key.ToString() switch
      {
        "LControlKey" => "LeftControl",
        "RControlKey" => "RightControl",
        "LShiftKey" => "LeftShift",
        "RShiftKey" => "RightShift",
        "LMenu" => "LeftAlt",
        "RMenu" => "RightAlt",
        "LWin" => "LeftWindows",
        "RWin" => "RightWindows",
        var name => name,
      };

    private static bool TryParseDefined(string name, out Keys key) =>
      Enum.TryParse(name, true, out key) && key != Keys.None && Enum.IsDefined(typeof(Keys), key);
  }
}
