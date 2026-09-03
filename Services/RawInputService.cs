using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using OsuMate.Utils;

namespace OsuMate.Services;

public sealed class RawInputService : IDisposable
{
  internal readonly record struct KeyTransition(Keys Key, bool IsDown, long TimestampTicks);

  private const int WmInput = 0x00ff;
  private const uint RidInput = 0x10000003;
  private const uint RimTypeMouse = 0;
  private const uint RimTypeKeyboard = 1;
  private const uint RidevInputSink = 0x00000100;
  private const ushort RiKeyBreak = 0x0001;
  private const ushort RiMouseLeftButtonDown = 0x0001;
  private const ushort RiMouseLeftButtonUp = 0x0002;
  private const ushort RiMouseRightButtonDown = 0x0004;
  private const ushort RiMouseRightButtonUp = 0x0008;

  private readonly ConcurrentDictionary<Keys, byte> _pressedKeys = [];
  private readonly ConcurrentQueue<KeyTransition> _transitions = new();
  private HwndSource? _source;
  private Window? _window;

  public void Attach(Window window)
  {
    if (ReferenceEquals(_window, window))
      return;

    Detach();
    _window = window;
    window.SourceInitialized += Window_SourceInitialized;
    if (new WindowInteropHelper(window).Handle != IntPtr.Zero)
      AttachSource(window);
  }

  private void Window_SourceInitialized(object? sender, EventArgs e)
  {
    if (sender is Window window)
      AttachSource(window);
  }

  private void AttachSource(Window window)
  {
    var handle = new WindowInteropHelper(window).Handle;
    if (handle == IntPtr.Zero)
      return;

    var source = HwndSource.FromHwnd(handle);
    if (source == null || ReferenceEquals(source, _source))
      return;

    _source?.RemoveHook(WndProc);
    _source = source;
    _source.AddHook(WndProc);
    RegisterDevices(_source.Handle);
  }

  private static void RegisterDevices(IntPtr handle)
  {
    var devices = new[]
    {
      new RawInputDevice { UsagePage = 0x01, Usage = 0x02, Flags = RidevInputSink, Target = handle },
      new RawInputDevice { UsagePage = 0x01, Usage = 0x06, Flags = RidevInputSink, Target = handle },
    };

    if (!RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RawInputDevice>()))
      LogUtils.DebugLogger($"RawInputService.RegisterDevices failed: {Marshal.GetLastWin32Error()}", true);
  }

  private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
  {
    if (message == WmInput)
    {
      ProcessRawInput(lParam);
      handled = true;
    }
    return IntPtr.Zero;
  }

  private readonly IntPtr _rawInputBuffer = Marshal.AllocHGlobal(256);

  private void ProcessRawInput(IntPtr rawInputHandle)
  {
    uint size = 256;
    if (GetRawInputData(rawInputHandle, RidInput, _rawInputBuffer, ref size, (uint)Marshal.SizeOf<RawInputHeader>()) == uint.MaxValue || size == 0)
      return;

    var type = Marshal.ReadInt32(_rawInputBuffer, 0);
    var headerSize = IntPtr.Size == 8 ? 24 : 16;
    if (type == RimTypeKeyboard)
    {
      var vk = (Keys)(Marshal.ReadInt16(_rawInputBuffer, headerSize + 6) & 0xFF);
      if (!_activeKeys.Contains(vk))
        return;
      var flags = Marshal.ReadInt16(_rawInputBuffer, headerSize + 2);
      SetPressed(vk, (flags & RiKeyBreak) == 0);
    }
    else if (type == RimTypeMouse)
    {
      var active = _activeKeys;
      if (!active.Contains(Keys.LButton) && !active.Contains(Keys.RButton))
        return;

      var flags = Marshal.ReadInt16(_rawInputBuffer, headerSize + 4);
      if (flags != 0)
      {
        if (active.Contains(Keys.LButton))
        {
          if ((flags & RiMouseLeftButtonDown) != 0)
            SetPressed(Keys.LButton, true);
          if ((flags & RiMouseLeftButtonUp) != 0)
            SetPressed(Keys.LButton, false);
        }
        if (active.Contains(Keys.RButton))
        {
          if ((flags & RiMouseRightButtonDown) != 0)
            SetPressed(Keys.RButton, true);
          if ((flags & RiMouseRightButtonUp) != 0)
            SetPressed(Keys.RButton, false);
        }
      }
    }
  }

  private volatile HashSet<Keys> _activeKeys = [];

  internal void SetActiveKeys(IEnumerable<Keys> keys)
  {
    var set = new HashSet<Keys>();
    foreach (var k in keys)
    {
      if (k != Keys.None)
        set.Add(k);
    }
    _activeKeys = set;
  }

  private void SetPressed(Keys key, bool pressed)
  {
    var changed = pressed ? _pressedKeys.TryAdd(key, 0) : _pressedKeys.TryRemove(key, out _);
    if (!changed)
      return;
    _transitions.Enqueue(new KeyTransition(key, pressed, Stopwatch.GetTimestamp()));
  }

  internal void DrainTransitions(List<KeyTransition> destination)
  {
    while (_transitions.TryDequeue(out var transition))
      destination.Add(transition);
  }

  private void Detach()
  {
    if (_window != null)
      _window.SourceInitialized -= Window_SourceInitialized;
    _source?.RemoveHook(WndProc);
    _source = null;
    _window = null;
    _pressedKeys.Clear();
    while (_transitions.TryDequeue(out _)) { }
  }

  public void Dispose()
  {
    Detach();
    Marshal.FreeHGlobal(_rawInputBuffer);
  }

  [StructLayout(LayoutKind.Sequential)]
  private struct RawInputDevice
  {
    public ushort UsagePage;
    public ushort Usage;
    public uint Flags;
    public IntPtr Target;
  }

  [StructLayout(LayoutKind.Sequential)]
  private struct RawInputHeader
  {
    public uint Type;
    public uint Size;
    public IntPtr Device;
    public IntPtr WParam;
  }

  [StructLayout(LayoutKind.Sequential)]
  private struct RawKeyboard
  {
    public ushort MakeCode;
    public ushort Flags;
    public ushort Reserved;
    public ushort VirtualKey;
    public uint Message;
    public uint ExtraInformation;
  }

  [StructLayout(LayoutKind.Sequential)]
  private struct RawMouse
  {
    public ushort Flags;
    public ushort Reserved;
    public ushort ButtonFlags;
    public ushort ButtonData;
    public uint RawButtons;
    public int LastX;
    public int LastY;
    public uint ExtraInformation;
  }

  [DllImport("user32.dll", SetLastError = true)]
  private static extern bool RegisterRawInputDevices([In] RawInputDevice[] devices, uint deviceCount, uint size);

  [DllImport("user32.dll", SetLastError = true)]
  private static extern uint GetRawInputData(IntPtr rawInput, uint command, IntPtr data, ref uint size, uint headerSize);
}
