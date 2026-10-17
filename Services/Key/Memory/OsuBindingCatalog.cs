using System.Windows.Forms;

namespace OsuMate.Services.Key.Memory
{

  internal static class OsuBindingCatalog
  {
    internal const int MinKeyCode = 1;
    internal const int MaxKeyCode = 255;

    private static readonly OsuBinding[] AllBindings = Enum.GetValues<OsuBinding>();

    internal static bool IsValidKeyCode(int value) => value is >= MinKeyCode and <= MaxKeyCode;

    internal static void AddBindings(
      IReadOnlyDictionary<int, int> entries,
      Dictionary<OsuBinding, Keys> target
    )
    {
      foreach (var binding in AllBindings)
      {
        if (entries.TryGetValue((int)binding, out var keyCode) && IsValidKeyCode(keyCode))
          target[binding] = (Keys)keyCode;
      }
    }
  }
}
