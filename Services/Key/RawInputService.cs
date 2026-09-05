using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using OsuMate.Utils;

namespace OsuMate.Services.Key;

public sealed class RawInputService : IDisposable
{
  internal readonly record struct KeyTransition(Keys Key, bool IsDown, long TimestampTicks);

  private const int WmInput = 0x00ff;
  private const uint WmQuit = 0x0012;
  private const uint RidInput = 0x10000003;
  private const uint RimTypeMouse = 0;
  private const uint RimTypeKeyboard = 1;
  private const uint RidevInputSink = 0x00000100;
  private const ushort RiKeyBreak = 0x0001;
  private const ushort RiMouseLeftButtonDown = 0x0001;
  private const ushort RiMouseLeftButtonUp = 0x0002;
  private const ushort RiMouseRightButtonDown = 0x0004;
  private const ushort RiMouseRightButtonUp = 0x0008;
  private static readonly IntPtr HwndMessage = new(-3);

  private readonly ConcurrentDictionary<Keys, byte> _pressedKeys = [];
  private readonly ConcurrentQueue<KeyTransition> _transitions = new();
  private readonly WndProcDelegate _wndProc;
  private readonly Thread _pumpThread;
  private readonly ManualResetEventSlim _ready = new(false);
  private readonly IntPtr _rawInputBuffer = Marshal.AllocHGlobal(256);
  private uint _pumpThreadId;
  private IntPtr _windowHandle;
  private bool _disposed;

  public RawInputService()
  {
    _wndProc = WndProc;
    _pumpThread = new Thread(RunPump)
    {
      IsBackground = true,
      Name = "OsuMate.RawInputPump",
      Priority = ThreadPriority.AboveNormal,
    };
    _pumpThread.Start();
    _ready.Wait();
  }

  private void RunPump()
  {
    _pumpThreadId = GetCurrentThreadId();
    _windowHandle = CreateMessageWindow();
    if (_windowHandle != IntPtr.Zero)
      RegisterDevices(_windowHandle);
    _ready.Set();

    while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
    {
      TranslateMessage(ref msg);
      DispatchMessage(ref msg);
    }
  }

  private IntPtr CreateMessageWindow()
  {
    var className = $"OsuMateRawInput_{Environment.ProcessId}";
    var hInstance = Marshal.GetHINSTANCE(typeof(RawInputService).Module);

    var wndClass = new WndClassEx
    {
      cbSize = (uint)Marshal.SizeOf<WndClassEx>(),
      lpfnWndProc = _wndProc,
      hInstance = hInstance,
      lpszClassName = className,
    };

    if (RegisterClassEx(ref wndClass) == 0)
    {
      LogUtils.DebugLogger($"RawInputService.RegisterClassEx failed: {Marshal.GetLastWin32Error()}", true);
      return IntPtr.Zero;
    }

    var handle = CreateWindowEx(
      0,
      className,
      string.Empty,
      0,
      0,
      0,
      0,
      0,
      HwndMessage,
      IntPtr.Zero,
      hInstance,
      IntPtr.Zero
    );
    if (handle == IntPtr.Zero)
      LogUtils.DebugLogger($"RawInputService.CreateWindowEx failed: {Marshal.GetLastWin32Error()}", true);
    return handle;
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

  private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam)
  {
    if (message == WmInput)
    {
      ProcessRawInput(lParam);
      return IntPtr.Zero;
    }
    return DefWindowProc(hwnd, message, wParam, lParam);
  }

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

  public void Dispose()
  {
    if (_disposed)
      return;
    _disposed = true;

    if (_pumpThreadId != 0)
      PostThreadMessage(_pumpThreadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
    _pumpThread.Join(TimeSpan.FromSeconds(2));

    if (_windowHandle != IntPtr.Zero)
      DestroyWindow(_windowHandle);

    _pressedKeys.Clear();
    while (_transitions.TryDequeue(out _)) { }
    Marshal.FreeHGlobal(_rawInputBuffer);
    _ready.Dispose();
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

  [StructLayout(LayoutKind.Sequential)]
  private struct WindowsPoint
  {
    public int X;
    public int Y;
  }

  [StructLayout(LayoutKind.Sequential)]
  private struct NativeMessage
  {
    public IntPtr Hwnd;
    public uint Message;
    public IntPtr WParam;
    public IntPtr LParam;
    public uint Time;
    public WindowsPoint Point;
  }

  [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
  private struct WndClassEx
  {
    public uint cbSize;
    public uint style;
    public WndProcDelegate lpfnWndProc;
    public int cbClsExtra;
    public int cbWndExtra;
    public IntPtr hInstance;
    public IntPtr hIcon;
    public IntPtr hCursor;
    public IntPtr hbrBackground;
    public string lpszMenuName;
    public string lpszClassName;
    public IntPtr hIconSm;
  }

  [UnmanagedFunctionPointer(CallingConvention.StdCall)]
  private delegate IntPtr WndProcDelegate(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

  [DllImport("user32.dll", SetLastError = true)]
  private static extern bool RegisterRawInputDevices([In] RawInputDevice[] devices, uint deviceCount, uint size);

  [DllImport("user32.dll", SetLastError = true)]
  private static extern uint GetRawInputData(IntPtr rawInput, uint command, IntPtr data, ref uint size, uint headerSize);

  [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
  private static extern ushort RegisterClassEx(ref WndClassEx lpwcx);

  [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
  private static extern IntPtr CreateWindowEx(
    uint dwExStyle,
    string lpClassName,
    string lpWindowName,
    uint dwStyle,
    int x,
    int y,
    int nWidth,
    int nHeight,
    IntPtr hWndParent,
    IntPtr hMenu,
    IntPtr hInstance,
    IntPtr lpParam
  );

  [DllImport("user32.dll")]
  private static extern IntPtr DefWindowProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

  [DllImport("user32.dll")]
  private static extern int GetMessage(out NativeMessage lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

  [DllImport("user32.dll")]
  private static extern bool TranslateMessage(ref NativeMessage lpMsg);

  [DllImport("user32.dll")]
  private static extern IntPtr DispatchMessage(ref NativeMessage lpMsg);

  [DllImport("user32.dll")]
  private static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

  [DllImport("user32.dll")]
  private static extern bool DestroyWindow(IntPtr hWnd);

  [DllImport("kernel32.dll")]
  private static extern uint GetCurrentThreadId();
}
