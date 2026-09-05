using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace OsuMate.Utils;

internal static class KeyboardPhysicalLayoutUtils
{
  private const uint JapaneseLanguageId = 0x0411;

  private static readonly Dictionary<Keys, double> UsPositions = BuildUsPositions();
  private static readonly Dictionary<Keys, double> JisPositions = BuildJisPositions();

  internal static double GetPhysicalPosition(Keys key)
  {
    var positions = IsJisLayoutActive() ? JisPositions : UsPositions;
    return positions.TryGetValue(key, out var position) ? position : double.MaxValue;
  }

  private static bool IsJisLayoutActive()
  {
    var foregroundWindow = GetForegroundWindow();
    var threadId = GetWindowThreadProcessId(foregroundWindow, IntPtr.Zero);
    var layout = GetKeyboardLayout(threadId);
    var languageId = (uint)(layout.ToInt64() & 0xFFFF);
    return languageId == JapaneseLanguageId;
  }

  private static Dictionary<Keys, double> BuildUsPositions()
  {
    var positions = new Dictionary<Keys, double>();

    AddRow(
      positions,
      0.0,
      Keys.Oemtilde, Keys.D1, Keys.D2, Keys.D3, Keys.D4, Keys.D5,
      Keys.D6, Keys.D7, Keys.D8, Keys.D9, Keys.D0, Keys.OemMinus, Keys.Oemplus
    );
    AddRow(
      positions,
      1.5,
      Keys.Q, Keys.W, Keys.E, Keys.R, Keys.T, Keys.Y, Keys.U, Keys.I, Keys.O, Keys.P,
      Keys.OemOpenBrackets, Keys.OemCloseBrackets, Keys.OemPipe
    );
    AddRow(
      positions,
      1.75,
      Keys.A, Keys.S, Keys.D, Keys.F, Keys.G, Keys.H, Keys.J, Keys.K, Keys.L,
      Keys.OemSemicolon, Keys.OemQuotes
    );
    AddRow(
      positions,
      2.25,
      Keys.Z, Keys.X, Keys.C, Keys.V, Keys.B, Keys.N, Keys.M,
      Keys.Oemcomma, Keys.OemPeriod, Keys.OemQuestion
    );

    positions[Keys.Tab] = 1.0;
    positions[Keys.CapsLock] = 1.25;
    positions[Keys.LShiftKey] = 0.5;
    positions[Keys.RShiftKey] = 12.5;
    positions[Keys.LControlKey] = 0.0;
    positions[Keys.RControlKey] = 13.0;
    positions[Keys.LMenu] = 2.75;
    positions[Keys.RMenu] = 8.25;
    positions[Keys.Space] = 4.5;

    return positions;
  }

  private static Dictionary<Keys, double> BuildJisPositions()
  {
    var positions = BuildUsPositions();

    positions[Keys.OemQuotes] = 12.0;
    positions[Keys.OemPipe] = 13.0;

    positions[Keys.Oemtilde] = 11.5;
    positions[Keys.OemOpenBrackets] = 12.5;

    positions[Keys.Oemplus] = 10.75;
    positions[Keys.OemSemicolon] = 11.75;
    positions[Keys.OemCloseBrackets] = 12.75;

    positions[Keys.OemBackslash] = 12.25;

    return positions;
  }

  private static void AddRow(Dictionary<Keys, double> positions, double start, params Keys[] rowKeys)
  {
    for (var i = 0; i < rowKeys.Length; i++)
      positions[rowKeys[i]] = start + i;
  }

  [DllImport("user32.dll")]
  private static extern IntPtr GetForegroundWindow();

  [DllImport("user32.dll")]
  private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr processId);

  [DllImport("user32.dll")]
  private static extern IntPtr GetKeyboardLayout(uint threadId);
}
