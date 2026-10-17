using System.Windows.Forms;

namespace OsuMate.Services.Key
{

  internal static class OsuKeyNames
  {
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
  }
}
