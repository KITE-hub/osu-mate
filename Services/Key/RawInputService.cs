using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using OsuMate.Utils;
using static OsuMate.Utils.Win32Interop;

namespace OsuMate.Services.Key
{

  public sealed class RawInputService : IDisposable
  {
    internal readonly record struct KeyTransition(Keys Key, bool IsDown, long TimestampTicks);

    private const int WmInput = 0x00ff;
    private const uint WmQuit = 0x0012;
    private const uint RidInput = 0x10000003;
    private const uint RimTypeMouse = 0;
    private const uint RimTypeKeyboard = 1;
    private const uint RidevInputSink = 0x00000100;
    private const uint RidevRemove = 0x00000001;
    private const int MouseButtonFlagsOffset = 4;
    private const int MaxQueuedTransitions = 4096;
    private const ushort RiKeyBreak = 0x0001;
    private const ushort RiKeyE0 = 0x0002;
    private const ushort RightShiftMakeCode = 0x36;
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
    private bool _mouseRegistered;
    private volatile bool _mouseNeeded;
    private readonly object _deviceLock = new();

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
      if (!_ready.Wait(TimeSpan.FromSeconds(10)))
        LogUtils.DebugLogger("RawInputService: pump thread did not signal ready in time", true);
    }

    private void RunPump()
    {
      _pumpThreadId = GetCurrentThreadId();
      _windowHandle = CreateMessageWindow();
      if (_windowHandle != IntPtr.Zero)
      {
        RegisterDevices(_windowHandle);
        SyncMouseRegistration();
      }
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
      return Win32Interop.DefWindowProc(hwnd, message, wParam, lParam);
    }

    private void ProcessRawInput(IntPtr rawInputHandle)
    {
      uint size = 256;
      if (GetRawInputData(rawInputHandle, RidInput, _rawInputBuffer, ref size, (uint)Marshal.SizeOf<RawInputHeader>()) == uint.MaxValue || size == 0)
        return;

      var type = Marshal.ReadInt32(_rawInputBuffer, 0);
      var headerSize = IntPtr.Size == 8 ? 24 : 16;
      var dataPtr = IntPtr.Add(_rawInputBuffer, headerSize);
      if (type == RimTypeKeyboard)
      {
        var keyboard = Marshal.PtrToStructure<RawKeyboard>(dataPtr);
        var rawKey = (Keys)(keyboard.VirtualKey & 0xFF);
        var vk = NormalizeVirtualKey(rawKey, keyboard);
        var activeKeys = _activeKeys;
        if (!activeKeys.Contains(vk))
        {
          if (!activeKeys.Contains(rawKey))
            return;
          vk = rawKey;
        }
        SetPressed(vk, (keyboard.Flags & RiKeyBreak) == 0);
      }
      else if (type == RimTypeMouse)
      {
        var active = _activeKeys;
        if (!active.Contains(Keys.LButton) && !active.Contains(Keys.RButton))
          return;

        var flags = (ushort)Marshal.ReadInt16(dataPtr, MouseButtonFlagsOffset);
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

    private static Keys NormalizeVirtualKey(Keys key, RawKeyboard keyboard)
    {
      var isExtended = (keyboard.Flags & RiKeyE0) != 0;
      return key switch
      {
        Keys.ShiftKey => keyboard.MakeCode == RightShiftMakeCode ? Keys.RShiftKey : Keys.LShiftKey,
        Keys.ControlKey => isExtended ? Keys.RControlKey : Keys.LControlKey,
        Keys.Menu => isExtended ? Keys.RMenu : Keys.LMenu,
        _ => key,
      };
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
      _mouseNeeded = set.Contains(Keys.LButton) || set.Contains(Keys.RButton);
      SyncMouseRegistration();
    }

    private void SyncMouseRegistration()
    {
      lock (_deviceLock)
      {
        var needed = _mouseNeeded;
        if (_disposed || _windowHandle == IntPtr.Zero || _mouseRegistered == needed)
          return;

        var device = new RawInputDevice
        {
          UsagePage = 0x01,
          Usage = 0x02,
          Flags = needed ? RidevInputSink : RidevRemove,
          Target = needed ? _windowHandle : IntPtr.Zero,
        };

        if (RegisterRawInputDevices([device], 1, (uint)Marshal.SizeOf<RawInputDevice>()))
          _mouseRegistered = needed;
        else
          LogUtils.DebugLogger($"RawInputService.SyncMouseRegistration failed: {Marshal.GetLastWin32Error()}", true);
      }
    }

    private void SetPressed(Keys key, bool pressed)
    {
      var changed = pressed ? _pressedKeys.TryAdd(key, 0) : _pressedKeys.TryRemove(key, out _);
      if (!changed)
        return;
      _transitions.Enqueue(new KeyTransition(key, pressed, Stopwatch.GetTimestamp()));
      while (_transitions.Count > MaxQueuedTransitions && _transitions.TryDequeue(out _)) { }
    }

    internal void DrainTransitions(List<KeyTransition> destination)
    {
      while (_transitions.TryDequeue(out var transition))
        destination.Add(transition);
    }

    public void Dispose()
    {
      lock (_deviceLock)
      {
        if (_disposed)
          return;
        _disposed = true;
      }

      if (_pumpThreadId != 0)
        PostThreadMessage(_pumpThreadId, WmQuit, IntPtr.Zero, IntPtr.Zero);

      if (!_pumpThread.Join(TimeSpan.FromSeconds(2)))
      {
        LogUtils.DebugLogger("RawInputService: pump thread did not exit in time; skipping unmanaged cleanup to avoid use-after-free.", true);
        return;
      }

      if (_windowHandle != IntPtr.Zero)
        Win32Interop.DestroyWindow(_windowHandle);

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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices([In] RawInputDevice[] devices, uint deviceCount, uint size);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputData(IntPtr rawInput, uint command, IntPtr data, ref uint size, uint headerSize);

  }
}
