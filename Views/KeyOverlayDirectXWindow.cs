using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using OsuMate.Models;
using OsuMate.Rendering;
using OsuMate.ViewModels;
using OsuMate.Views.Controls;

namespace OsuMate.Views;

public sealed class KeyOverlayDirectXWindow : IDisposable
{
  private const int WS_POPUP = unchecked((int)0x80000000);
  private const int WS_EX_TOPMOST = 0x00000008;
  private const int WS_EX_TOOLWINDOW = 0x00000080;
  private const int WS_EX_NOREDIRECTIONBITMAP = 0x00200000;
  private const int WS_EX_TRANSPARENT = 0x00000020;

  private const int SW_HIDE = 0;
  private const int SW_SHOWNOACTIVATE = 4;

  private const uint SWP_NOSIZE = 0x0001;
  private const uint SWP_NOMOVE = 0x0002;
  private const uint SWP_NOZORDER = 0x0004;
  private const uint SWP_NOACTIVATE = 0x0010;
  private const uint SWP_FRAMECHANGED = 0x0020;

  private const int GWL_EXSTYLE = -20;

  private const int WM_DESTROY = 0x0002;
  private const int WM_CLOSE = 0x0010;
  private const int WM_QUIT = 0x0012;
  private const int WM_SETCURSOR = 0x0020;
  private const int WM_NCHITTEST = 0x0084;
  private const int WM_MOUSEMOVE = 0x0200;
  private const int WM_LBUTTONDOWN = 0x0201;
  private const int WM_LBUTTONUP = 0x0202;
  private const int WM_CAPTURECHANGED = 0x0215;
  private const int WM_DPICHANGED = 0x02E0;
  private const int WM_APP_COMMAND = 0x8001;

  private const int HTTRANSPARENT = -1;
  private const int HTCLIENT = 1;

  private const uint PM_REMOVE = 0x0001;

  private const int IDC_ARROW = 32512;
  private const int IDC_SIZENS = 32645;
  private const int IDC_SIZEWE = 32644;

  private static readonly ConcurrentDictionary<IntPtr, KeyOverlayDirectXWindow> Windows = new();

  private readonly KeyOverlayViewModel _vm;
  private readonly ConcurrentQueue<Action> _commandQueue = new();
  private readonly List<KeyOverlayTransition> _transitionBuffer = [];
  private readonly ManualResetEventSlim _initReady = new(false);
  private readonly Thread _thread;
  private readonly WndProcDelegate _wndProc;

  private IntPtr _hwnd;
  private Direct2DContext _context = null!;
  private Direct2DKeyOverlayRenderer _renderer = null!;
  private Exception? _initException;

  private bool _running;
  private bool _isVisible;
  private bool _isDraggable;
  private bool _isDragging;
  private bool _isResizing;
  private ResizeEdge _activeResizeEdge = ResizeEdge.None;
  private bool _disposed;

  private int _rotation;
  private double _flowLength = 700;
  private double _durationMs = 1000;
  private double _speed = 600;
  private double _round = 4;
  private double _laneWidth = 64;
  private int _laneCount = -1;

  private double _widthDip = 120;
  private double _heightDip = 200;
  private uint _dpi = 96;
  private double _dpiScale = 1.0;

  private POINT _dragStartMouse;
  private int _dragStartLeft;
  private int _dragStartTop;
  private POINT _resizeStartMouse;
  private double _resizeStartLength;
  private int _resizeStartLeft;
  private int _resizeStartTop;
  private int _resizeStartRight;
  private int _resizeStartBottom;
  private int _currentPixelWidth;
  private int _currentPixelHeight;

  public event Action<double, double>? PositionChanged;
  public event Action<double>? FlowLengthChanged;

  public KeyOverlayDirectXWindow(KeyOverlayViewModel vm)
  {
    _vm = vm;
    _wndProc = WndProc;

    _thread = new Thread(RunThread)
    {
      IsBackground = true,
      Name = "OsuMate.KeyOverlayRenderThread",
      Priority = ThreadPriority.AboveNormal
    };
    _thread.Start();

    _initReady.Wait();
    if (_initException != null)
      throw new InvalidOperationException("Failed to initialize KeyOverlay DirectX window", _initException);
  }

  public void Show()
  {
    _commandQueue.Enqueue(() =>
    {
      _isVisible = true;
      ShowWindow(_hwnd, SW_SHOWNOACTIVATE);
    });
    PostMessage(_hwnd, WM_APP_COMMAND, IntPtr.Zero, IntPtr.Zero);
  }

  public void Hide()
  {
    _commandQueue.Enqueue(() =>
    {
      _isVisible = false;
      ShowWindow(_hwnd, SW_HIDE);
    });
    PostMessage(_hwnd, WM_APP_COMMAND, IntPtr.Zero, IntPtr.Zero);
  }

  public void SetDraggable(bool draggable)
  {
    _commandQueue.Enqueue(() => SetDraggableInternal(draggable));
    PostMessage(_hwnd, WM_APP_COMMAND, IntPtr.Zero, IntPtr.Zero);
  }

  public void UpdateSettings(
    int rotation,
    double flowLength,
    double durationMs,
    double round,
    double laneWidth,
    string? fontFamily = null,
    double inputBarOpacity = 0.5,
    double beatmapBarOpacity = 0.5,
    double beatmapTapLengthMs = 25
  )
  {
    _commandQueue.Enqueue(() =>
    {
      _rotation = (int)Math.Round((((rotation % 360) + 360) % 360) / 90.0) * 90 % 360;
      _flowLength = Math.Max(120.0, flowLength);
      _durationMs = Math.Clamp(durationMs, 100.0, 10000.0);
      _speed = Math.Max(1.0, (_flowLength - (Direct2DKeyOverlayRenderer.KeyLength + Direct2DKeyOverlayRenderer.Gap)) / (_durationMs / 1000.0));
      _round = round;
      _laneWidth = laneWidth;
      _renderer.UpdateSettings(_rotation, _speed, _round, _laneWidth, fontFamily, inputBarOpacity, beatmapBarOpacity, beatmapTapLengthMs);
      ApplySize();
    });
    PostMessage(_hwnd, WM_APP_COMMAND, IntPtr.Zero, IntPtr.Zero);
  }

  public void SetPosition(double left, double top)
  {
    _commandQueue.Enqueue(() =>
    {
      var x = (int)Math.Round(left * _dpiScale);
      var y = (int)Math.Round(top * _dpiScale);
      SetWindowPos(_hwnd, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
    });
    PostMessage(_hwnd, WM_APP_COMMAND, IntPtr.Zero, IntPtr.Zero);
  }

  public void ApplyPositionIfIdle(double left, double top)
  {
    _commandQueue.Enqueue(() =>
    {
      if (!_isVisible || _isDragging || _isResizing)
        return;
      var x = (int)Math.Round(left * _dpiScale);
      var y = (int)Math.Round(top * _dpiScale);
      SetWindowPos(_hwnd, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
    });
    PostMessage(_hwnd, WM_APP_COMMAND, IntPtr.Zero, IntPtr.Zero);
  }

  private void RunThread()
  {
    try
    {
      InitWindow();
      _running = true;
      _initReady.Set();
    }
    catch (Exception ex)
    {
      _initException = ex;
      _initReady.Set();
      return;
    }

    while (_running)
    {
      while (PeekMessage(out var msg, IntPtr.Zero, 0, 0, PM_REMOVE))
      {
        if (msg.Message == WM_QUIT)
        {
          _running = false;
          break;
        }
        TranslateMessage(ref msg);
        DispatchMessage(ref msg);
      }

      if (!_running)
        break;

      DrainCommands();

      if (!_isVisible)
      {
        WaitMessage();
        continue;
      }

      _vm.RequestUpdate?.Invoke();
      var layout = _vm.Layout;
      var beatmapState = _vm.BeatmapState;
      var resetCounts = _vm.DrainReset();
      var isPlayActive = _vm.IsPlayActive;
      _transitionBuffer.Clear();
      _vm.DrainTransitions(_transitionBuffer);

      if (_laneCount != layout.Keys.Length)
      {
        _laneCount = layout.Keys.Length;
        if (!_isResizing)
          ApplySize();
      }

      _renderer.Render(
        _context.DeviceContext,
        layout,
        _transitionBuffer,
        Stopwatch.GetTimestamp(),
        _widthDip,
        _heightDip,
        _isDraggable,
        isPlayActive,
        resetCounts,
        beatmapState
      );

      _context.Present();
    }

    Cleanup();
  }

  private void InitWindow()
  {
    var className = $"OsuMateKeyOverlay_{Environment.ProcessId}";
    var hInstance = Marshal.GetHINSTANCE(typeof(KeyOverlayDirectXWindow).Module);

    var wndClass = new WNDCLASSEX
    {
      cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
      lpfnWndProc = _wndProc,
      hInstance = hInstance,
      lpszClassName = className
    };

    RegisterClassEx(ref wndClass);

    var pxW = Math.Max(1, (int)Math.Round(_widthDip * _dpiScale));
    var pxH = Math.Max(1, (int)Math.Round(_heightDip * _dpiScale));

    _hwnd = CreateWindowEx(
      WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOREDIRECTIONBITMAP | WS_EX_TRANSPARENT,
      className,
      "osu mate - Key Overlay",
      WS_POPUP,
      0,
      0,
      pxW,
      pxH,
      IntPtr.Zero,
      IntPtr.Zero,
      hInstance,
      IntPtr.Zero
    );

    if (_hwnd == IntPtr.Zero)
      throw new InvalidOperationException($"CreateWindowEx failed with error {Marshal.GetLastWin32Error()}");

    Windows.TryAdd(_hwnd, this);

    var dpi = GetDpiForWindow(_hwnd);
    _dpi = dpi == 0 ? 96 : dpi;
    _dpiScale = _dpi / 96.0;

    pxW = Math.Max(1, (int)Math.Round(_widthDip * _dpiScale));
    pxH = Math.Max(1, (int)Math.Round(_heightDip * _dpiScale));
    _widthDip = pxW / _dpiScale;
    _heightDip = pxH / _dpiScale;
    _currentPixelWidth = pxW;
    _currentPixelHeight = pxH;

    _context = new Direct2DContext(_hwnd, pxW, pxH, _dpi);
    _renderer = new Direct2DKeyOverlayRenderer(_context);
    _renderer.UpdateSettings(_rotation, _speed, _round, _laneWidth);
  }

  private void DrainCommands()
  {
    while (_commandQueue.TryDequeue(out var action))
      action();
  }

  private void SetDraggableInternal(bool draggable)
  {
    _isDraggable = draggable;
    var exStyle = GetWindowLongPtr(_hwnd, GWL_EXSTYLE).ToInt64();
    if (draggable)
      exStyle &= ~WS_EX_TRANSPARENT;
    else
      exStyle |= WS_EX_TRANSPARENT;
    SetWindowLongPtr(_hwnd, GWL_EXSTYLE, (IntPtr)exStyle);
    SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
  }

  private void ResizeContextIfNeeded(int pxW, int pxH)
  {
    if (pxW == _currentPixelWidth && pxH == _currentPixelHeight)
      return;

    _currentPixelWidth = pxW;
    _currentPixelHeight = pxH;
    _context.Resize(pxW, pxH, _dpi);
  }

  private void ApplySize()
  {
    var effectiveLaneCount = _laneCount <= 0 ? 2 : _laneCount;
    var (w, h) = _renderer.GetRequiredSize(effectiveLaneCount, _flowLength);
    var pxW = Math.Max(1, (int)Math.Round(w * _dpiScale));
    var pxH = Math.Max(1, (int)Math.Round(h * _dpiScale));
    _widthDip = pxW / _dpiScale;
    _heightDip = pxH / _dpiScale;

    SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, pxW, pxH, SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE);
    ResizeContextIfNeeded(pxW, pxH);
  }

  private IntPtr HandleMessage(int msg, IntPtr wParam, IntPtr lParam)
  {
    switch (msg)
    {
      case WM_APP_COMMAND:
        DrainCommands();
        return IntPtr.Zero;

      case WM_NCHITTEST:
        return _isDraggable ? (IntPtr)HTCLIENT : (IntPtr)HTTRANSPARENT;

      case WM_LBUTTONDOWN:
        if (_isDraggable)
        {
          GetCursorPos(out var curPos);
          var clientPos = curPos;
          ScreenToClient(_hwnd, ref clientPos);
          var xDip = clientPos.X / (float)_dpiScale;
          var yDip = clientPos.Y / (float)_dpiScale;

          GetWindowRect(_hwnd, out var rect);

          var edge = _renderer.HitTestResizeHandle(xDip, yDip, (float)_widthDip, (float)_heightDip);
          if (edge != ResizeEdge.None)
          {
            _isResizing = true;
            _activeResizeEdge = edge;
            _resizeStartMouse = curPos;
            _resizeStartLength = _flowLength;
            _resizeStartLeft = rect.Left;
            _resizeStartTop = rect.Top;
            _resizeStartRight = rect.Right;
            _resizeStartBottom = rect.Bottom;
            SetCapture(_hwnd);
          }
          else
          {
            _isDragging = true;
            _dragStartMouse = curPos;
            _dragStartLeft = rect.Left;
            _dragStartTop = rect.Top;
            SetCapture(_hwnd);
          }
          return IntPtr.Zero;
        }
        break;

      case WM_MOUSEMOVE:
        if (_isResizing)
        {
          GetCursorPos(out var curPos);
          var delta = _activeResizeEdge switch
          {
            ResizeEdge.Bottom => curPos.Y - _resizeStartMouse.Y,
            ResizeEdge.Top => _resizeStartMouse.Y - curPos.Y,
            ResizeEdge.Right => curPos.X - _resizeStartMouse.X,
            ResizeEdge.Left => _resizeStartMouse.X - curPos.X,
            _ => 0
          };
          var deltaDip = delta / _dpiScale;
          _flowLength = Math.Max(120.0, _resizeStartLength + deltaDip);
          _speed = Math.Max(1.0, (_flowLength - (Direct2DKeyOverlayRenderer.KeyLength + Direct2DKeyOverlayRenderer.Gap)) / (_durationMs / 1000.0));
          _renderer.UpdateSettings(_rotation, _speed, _round, _laneWidth);

          var effectiveLaneCount = _laneCount <= 0 ? 2 : _laneCount;
          var (w, h) = _renderer.GetRequiredSize(effectiveLaneCount, _flowLength);
          var pxW = Math.Max(1, (int)Math.Round(w * _dpiScale));
          var pxH = Math.Max(1, (int)Math.Round(h * _dpiScale));
          _widthDip = pxW / _dpiScale;
          _heightDip = pxH / _dpiScale;

          var newLeft = _activeResizeEdge == ResizeEdge.Left ? _resizeStartRight - pxW : _resizeStartLeft;
          var newTop = _activeResizeEdge == ResizeEdge.Top ? _resizeStartBottom - pxH : _resizeStartTop;

          SetWindowPos(_hwnd, IntPtr.Zero, newLeft, newTop, pxW, pxH, SWP_NOZORDER | SWP_NOACTIVATE);
          ResizeContextIfNeeded(pxW, pxH);
          return IntPtr.Zero;
        }
        if (_isDragging)
        {
          GetCursorPos(out var curPos);
          var newLeft = _dragStartLeft + curPos.X - _dragStartMouse.X;
          var newTop = _dragStartTop + curPos.Y - _dragStartMouse.Y;
          SetWindowPos(_hwnd, IntPtr.Zero, newLeft, newTop, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
          return IntPtr.Zero;
        }
        break;

      case WM_LBUTTONUP:
        if (_isDragging)
        {
          _isDragging = false;
          ReleaseCapture();
          GetWindowRect(_hwnd, out var rect);
          PositionChanged?.Invoke(rect.Left / _dpiScale, rect.Top / _dpiScale);
          return IntPtr.Zero;
        }
        if (_isResizing)
        {
          _isResizing = false;
          ReleaseCapture();
          if (_activeResizeEdge is ResizeEdge.Top or ResizeEdge.Left)
          {
            GetWindowRect(_hwnd, out var rect);
            PositionChanged?.Invoke(rect.Left / _dpiScale, rect.Top / _dpiScale);
          }
          _activeResizeEdge = ResizeEdge.None;
          FlowLengthChanged?.Invoke(_flowLength);
          return IntPtr.Zero;
        }
        break;

      case WM_CAPTURECHANGED:
        if (_isDragging)
        {
          _isDragging = false;
          GetWindowRect(_hwnd, out var rect);
          PositionChanged?.Invoke(rect.Left / _dpiScale, rect.Top / _dpiScale);
        }
        if (_isResizing)
        {
          _isResizing = false;
          if (_activeResizeEdge is ResizeEdge.Top or ResizeEdge.Left)
          {
            GetWindowRect(_hwnd, out var rect);
            PositionChanged?.Invoke(rect.Left / _dpiScale, rect.Top / _dpiScale);
          }
          _activeResizeEdge = ResizeEdge.None;
          FlowLengthChanged?.Invoke(_flowLength);
        }
        return IntPtr.Zero;

      case WM_SETCURSOR:
        if (_isDraggable)
        {
          if (_isResizing)
          {
            var idc = _activeResizeEdge switch
            {
              ResizeEdge.Top or ResizeEdge.Bottom => IDC_SIZENS,
              ResizeEdge.Left or ResizeEdge.Right => IDC_SIZEWE,
              _ => IDC_ARROW
            };
            SetCursor(LoadCursor(IntPtr.Zero, (IntPtr)idc));
            return (IntPtr)1;
          }

          GetCursorPos(out var curPos);
          ScreenToClient(_hwnd, ref curPos);
          var xDip = curPos.X / (float)_dpiScale;
          var yDip = curPos.Y / (float)_dpiScale;
          var edge = _renderer.HitTestResizeHandle(xDip, yDip, (float)_widthDip, (float)_heightDip);
          if (edge != ResizeEdge.None)
          {
            var idc = edge switch
            {
              ResizeEdge.Top or ResizeEdge.Bottom => IDC_SIZENS,
              ResizeEdge.Left or ResizeEdge.Right => IDC_SIZEWE,
              _ => IDC_ARROW
            };
            SetCursor(LoadCursor(IntPtr.Zero, (IntPtr)idc));
            return (IntPtr)1;
          }
          SetCursor(LoadCursor(IntPtr.Zero, (IntPtr)IDC_ARROW));
          return (IntPtr)1;
        }
        break;

      case WM_DPICHANGED:
        _dpi = (uint)(wParam.ToInt64() & 0xFFFF);
        _dpiScale = _dpi / 96.0;
        var suggestedRect = Marshal.PtrToStructure<RECT>(lParam);
        var newW = suggestedRect.Right - suggestedRect.Left;
        var newH = suggestedRect.Bottom - suggestedRect.Top;
        _widthDip = newW / _dpiScale;
        _heightDip = newH / _dpiScale;
        SetWindowPos(_hwnd, IntPtr.Zero, suggestedRect.Left, suggestedRect.Top, newW, newH, SWP_NOZORDER | SWP_NOACTIVATE);
        _currentPixelWidth = newW;
        _currentPixelHeight = newH;
        _context.Resize(newW, newH, _dpi);
        return IntPtr.Zero;

      case WM_CLOSE:
        _running = false;
        DestroyWindow(_hwnd);
        return IntPtr.Zero;

      case WM_DESTROY:
        PostQuitMessage(0);
        return IntPtr.Zero;
    }

    return DefWindowProc(_hwnd, msg, wParam, lParam);
  }

  private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
  {
    if (Windows.TryGetValue(hwnd, out var window))
      return window.HandleMessage(msg, wParam, lParam);

    return DefWindowProc(hwnd, msg, wParam, lParam);
  }

  private void Cleanup()
  {
    Windows.TryRemove(_hwnd, out _);
    _renderer?.Dispose();
    _context?.Dispose();
  }

  public void Dispose()
  {
    if (_disposed)
      return;

    _disposed = true;
    _running = false;

    if (_hwnd != IntPtr.Zero)
      PostMessage(_hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);

    _thread.Join(TimeSpan.FromSeconds(2));
    _initReady.Dispose();
  }

  [StructLayout(LayoutKind.Sequential)]
  private struct POINT
  {
    public int X;
    public int Y;
  }

  [StructLayout(LayoutKind.Sequential)]
  private struct MSG
  {
    public IntPtr Hwnd;
    public uint Message;
    public IntPtr WParam;
    public IntPtr LParam;
    public uint Time;
    public POINT Point;
  }

  [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
  private struct WNDCLASSEX
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

  [StructLayout(LayoutKind.Sequential)]
  private struct RECT
  {
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
  }

  [UnmanagedFunctionPointer(CallingConvention.StdCall)]
  private delegate IntPtr WndProcDelegate(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

  [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
  private static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

  [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
  private static extern IntPtr CreateWindowEx(
    int dwExStyle,
    string lpClassName,
    string lpWindowName,
    int dwStyle,
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
  private static extern bool DestroyWindow(IntPtr hWnd);

  [DllImport("user32.dll")]
  private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

  [DllImport("user32.dll", SetLastError = true)]
  private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

  [DllImport("user32.dll")]
  private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

  [DllImport("user32.dll", SetLastError = true)]
  private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

  [DllImport("user32.dll", SetLastError = true)]
  private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

  [DllImport("user32.dll")]
  private static extern bool GetCursorPos(out POINT lpPoint);

  [DllImport("user32.dll")]
  private static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

  [DllImport("user32.dll")]
  private static extern IntPtr SetCapture(IntPtr hWnd);

  [DllImport("user32.dll")]
  private static extern bool ReleaseCapture();

  [DllImport("user32.dll")]
  private static extern IntPtr SetCursor(IntPtr hCursor);

  [DllImport("user32.dll")]
  private static extern IntPtr LoadCursor(IntPtr hInstance, IntPtr lpCursorName);

  [DllImport("user32.dll")]
  private static extern uint GetDpiForWindow(IntPtr hwnd);

  [DllImport("user32.dll")]
  private static extern bool PeekMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

  [DllImport("user32.dll")]
  private static extern bool TranslateMessage(ref MSG lpMsg);

  [DllImport("user32.dll")]
  private static extern IntPtr DispatchMessage(ref MSG lpMsg);

  [DllImport("user32.dll")]
  private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

  [DllImport("user32.dll")]
  private static extern void PostQuitMessage(int nExitCode);

  [DllImport("user32.dll")]
  private static extern bool WaitMessage();
}
