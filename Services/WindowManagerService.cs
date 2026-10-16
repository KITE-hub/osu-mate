using System.Windows;
using OsuMate.Services.Key;
using OsuMate.Utils;
using OsuMate.ViewModels;

namespace OsuMate.Services
{
  public class WindowManagerService : IDisposable
  {
    private readonly MainViewModel _mainViewModel;
    private readonly SettingsViewModel _settingsVm;
    private readonly OsuProcessMonitorService _processMonitor;
    private readonly RawInputService _rawInput;
    private readonly Views.InGameOverlayWindow _overlayWindow;
    private readonly Views.URBarWindow _urBarWindow;
    private readonly KeyOverlayWindowController _keyOverlayThread;
    private Window? _mainWindow;

    private CancellationTokenSource? _overlayTrackingCts;
    private int _suppressCount = 0;
    private bool _isSettingsOpen = false;

    private readonly RelativeWindowPosition _overlayPosition;
    private readonly RelativeWindowPosition _urBarPosition;
    private readonly RelativeWindowSize _urBarSize;
    private readonly RelativeWindowPosition _keyOverlayPosition;
    private double _keyOverlayFlowLength;

    public WindowManagerService(
      MainViewModel mainViewModel,
      SettingsViewModel settingsVm,
      OsuProcessMonitorService processMonitor,
      RawInputService rawInput
    )
    {
      _mainViewModel = mainViewModel;
      _settingsVm = settingsVm;
      _processMonitor = processMonitor;
      _rawInput = rawInput;

      _overlayPosition = new RelativeWindowPosition(_settingsVm.Overlay.OverlayX, _settingsVm.Overlay.OverlayY);
      _urBarPosition = new RelativeWindowPosition(_settingsVm.URBar.URBarX, _settingsVm.URBar.URBarY);
      _urBarSize = new RelativeWindowSize(_settingsVm.URBar.URBarWidth, _settingsVm.URBar.URBarHeight);
      _keyOverlayPosition = new RelativeWindowPosition(
        _settingsVm.KeyOverlay.KeyOverlayX,
        _settingsVm.KeyOverlay.KeyOverlayY
      );
      _keyOverlayFlowLength = _settingsVm.KeyOverlay.KeyOverlayHeight;

      _overlayWindow = new Views.InGameOverlayWindow(_mainViewModel.InGameOverlay);
      _overlayWindow.PositionChanged += HandleOverlayWindowPositionChanged;

      _urBarWindow = new Views.URBarWindow();
      _urBarWindow.SetViewModel(_mainViewModel.URBar);
      _urBarWindow.SetRotation(_settingsVm.URBar.URBarRotation);
      _urBarWindow.UpdateAnimationSettings(
        _settingsVm.URBar.URBarAvgLineFollowStrength,
        _settingsVm.URBar.URBarAvgLineAnimMs
      );
      _urBarWindow.UpdateOpacitySettings(
        _settingsVm.URBar.URBarLabelOpacity,
        _settingsVm.URBar.URBarSegmentOpacity,
        _settingsVm.URBar.URBarMarkerOpacity,
        _settingsVm.URBar.URBarHitErrorOpacity
      );
      _urBarWindow.PositionChanged += HandleURBarWindowPositionChanged;
      _urBarWindow.OnSizeChanged += HandleURBarWindowSizeChanged;

      _keyOverlayThread = new KeyOverlayWindowController(_mainViewModel.KeyOverlay);
      ApplyKeyOverlaySettings();
      _keyOverlayThread.PositionChanged += HandleKeyOverlayWindowPositionChanged;
      _keyOverlayThread.FlowLengthChanged += HandleKeyOverlayFlowLengthChanged;
    }

    public void Start()
    {
      _settingsVm.Overlay.OnSaveOverlayPositionRequested += HandleSaveOverlayPositionRequested;
      _settingsVm.Overlay.OnApplyOverlayPositionRequested += HandleApplyOverlayPositionRequested;
      _settingsVm.URBar.OnSaveURBarPositionRequested += HandleSaveURBarPositionRequested;
      _settingsVm.URBar.OnApplyURBarPositionRequested += HandleApplyURBarPositionRequested;
      _settingsVm.URBar.OnSaveURBarSizeRequested += HandleSaveURBarSizeRequested;
      _settingsVm.URBar.OnApplyURBarSizeRequested += HandleApplyURBarSizeRequested;
      _settingsVm.KeyOverlay.OnSaveKeyOverlayPositionRequested += HandleSaveKeyOverlayPositionRequested;
      _settingsVm.KeyOverlay.OnApplyKeyOverlayPositionRequested += HandleApplyKeyOverlayPositionRequested;
      _settingsVm.KeyOverlay.OnSaveKeyOverlayFlowLengthRequested += HandleSaveKeyOverlayFlowLengthRequested;
      _settingsVm.KeyOverlay.OnApplyKeyOverlayFlowLengthRequested += HandleApplyKeyOverlayFlowLengthRequested;

      _settingsVm.PropertyChanged += SettingsVm_PropertyChanged;
      _settingsVm.Overlay.PropertyChanged += SettingsVm_PropertyChanged;
      _settingsVm.URBar.PropertyChanged += SettingsVm_PropertyChanged;
      _settingsVm.KeyOverlay.PropertyChanged += KeyOverlayVm_PropertyChanged;
      _mainViewModel.IsPlayingChanged += OnIsPlayingChanged;
      _mainViewModel.OnOsuWindowFound += OnOsuWindowFound;

      _overlayTrackingCts = new CancellationTokenSource();
      _ = _processMonitor.StartTrackingAsync(
        OnOsuWindowRectAvailableAsync,
        _overlayTrackingCts.Token
      );
    }

    public void AttachMainWindow(Window mainWindow)
    {
      _mainWindow = mainWindow;
    }

    private void HandleOverlayWindowPositionChanged(double left, double top)
    {
      _overlayPosition.CaptureFromScreen(left, top, TryGetOsuWindowRectOrNull());
    }

    private void HandleURBarWindowPositionChanged(double left, double top)
    {
      _urBarPosition.CaptureFromScreen(left, top, TryGetOsuWindowRectOrNull());
    }

    private void HandleURBarWindowSizeChanged(double width, double height)
    {
      _urBarSize.SetValue(width, height);
    }

    private void HandleKeyOverlayWindowPositionChanged(double left, double top)
    {
      Application.Current.Dispatcher.BeginInvoke(() =>
        _keyOverlayPosition.CaptureFromScreen(left, top, TryGetOsuWindowRectOrNull())
      );
    }

    private void HandleKeyOverlayFlowLengthChanged(double length)
    {
      Application.Current.Dispatcher.BeginInvoke(() =>
      {
        _keyOverlayFlowLength = length;
        _settingsVm.KeyOverlay.KeyOverlayHeight = length;
        if (_settingsVm.KeyOverlay.KeyOverlayRotation is 180 or 270)
          _settingsVm.KeyOverlay.SetKeyOverlayPosition(_keyOverlayPosition.X, _keyOverlayPosition.Y);
      });
    }

    private void HandleSaveOverlayPositionRequested()
    {
      _settingsVm.Overlay.SetOverlayPosition(_overlayPosition.X, _overlayPosition.Y);
    }

    private void HandleApplyOverlayPositionRequested()
    {
      _overlayPosition.SetValue(_settingsVm.Overlay.OverlayX, _settingsVm.Overlay.OverlayY);
      PositionOverlaysToOsu();
    }

    private void HandleSaveURBarPositionRequested()
    {
      _settingsVm.URBar.SetURBarPosition(_urBarPosition.X, _urBarPosition.Y);
    }

    private void HandleApplyURBarPositionRequested()
    {
      _urBarPosition.SetValue(_settingsVm.URBar.URBarX, _settingsVm.URBar.URBarY);
      PositionOverlaysToOsu();
    }

    private void HandleSaveURBarSizeRequested()
    {
      _settingsVm.URBar.URBarWidth = _urBarSize.Width;
      _settingsVm.URBar.URBarHeight = _urBarSize.Height;
      _settingsVm.Save();
    }

    private void HandleApplyURBarSizeRequested()
    {
      _urBarSize.SetValue(_settingsVm.URBar.URBarWidth, _settingsVm.URBar.URBarHeight);
      DispatcherUtils.RunOnPrimary(() =>
        _urBarWindow.SetSettingsMode(
          _isSettingsOpen && !_mainViewModel.IsPlaying,
          _urBarSize.Width,
          _urBarSize.Height
        )
      );
    }

    private void HandleSaveKeyOverlayPositionRequested()
    {
      _settingsVm.KeyOverlay.SetKeyOverlayPosition(_keyOverlayPosition.X, _keyOverlayPosition.Y);
    }

    private void HandleApplyKeyOverlayPositionRequested()
    {
      _keyOverlayPosition.SetValue(_settingsVm.KeyOverlay.KeyOverlayX, _settingsVm.KeyOverlay.KeyOverlayY);
      PositionOverlaysToOsu();
    }

    private void HandleSaveKeyOverlayFlowLengthRequested()
    {
      _settingsVm.KeyOverlay.KeyOverlayHeight = _keyOverlayFlowLength;
      _settingsVm.KeyOverlay.SetKeyOverlayPosition(_keyOverlayPosition.X, _keyOverlayPosition.Y);
      _settingsVm.Save();
    }

    private void HandleApplyKeyOverlayFlowLengthRequested()
    {
      _keyOverlayFlowLength = _settingsVm.KeyOverlay.KeyOverlayHeight;
      ApplyKeyOverlaySettings();
      PositionOverlaysToOsu();
    }

    private void SettingsVm_PropertyChanged(
      object? sender,
      System.ComponentModel.PropertyChangedEventArgs e
    )
    {
      DispatcherUtils.RunOnPrimary(() =>
      {
        if (e.PropertyName == nameof(OverlaySettingsViewModel.OverlayFontSize))
        {
          Interlocked.Increment(ref _suppressCount);
          _ = Task.Delay(1000).ContinueWith(_ => Interlocked.Decrement(ref _suppressCount));
        }
        else if (e.PropertyName == nameof(URBarSettingsViewModel.URBarRotation))
        {
          double cx = _urBarWindow.Left + _urBarWindow.Width / 2;
          double cy = _urBarWindow.Top + _urBarWindow.Height / 2;

          _urBarWindow.SetRotation(_settingsVm.URBar.URBarRotation);

          _urBarWindow.Left = cx - _urBarWindow.Width / 2;
          _urBarWindow.Top = cy - _urBarWindow.Height / 2;

          _urBarPosition.CaptureFromScreen(
            _urBarWindow.Left,
            _urBarWindow.Top,
            TryGetOsuWindowRectOrNull()
          );
        }
        else if (e.PropertyName == nameof(URBarSettingsViewModel.URBarEnabled))
        {
          OnURBarEnabledChanged(_settingsVm.URBar.URBarEnabled);
        }
        else if (e.PropertyName == nameof(SettingsViewModel.FontFamily))
        {
          ApplyKeyOverlaySettings();
        }
        else if (
          e.PropertyName == nameof(URBarSettingsViewModel.URBarAvgLineFollowStrength)
          || e.PropertyName == nameof(URBarSettingsViewModel.URBarAvgLineAnimMs)
        )
        {
          _urBarWindow.UpdateAnimationSettings(
            _settingsVm.URBar.URBarAvgLineFollowStrength,
            _settingsVm.URBar.URBarAvgLineAnimMs
          );
        }
        else if (
          e.PropertyName == nameof(URBarSettingsViewModel.URBarLabelOpacity)
          || e.PropertyName == nameof(URBarSettingsViewModel.URBarMarkerOpacity)
          || e.PropertyName == nameof(URBarSettingsViewModel.URBarSegmentOpacity)
          || e.PropertyName == nameof(URBarSettingsViewModel.URBarHitErrorOpacity)
        )
        {
          _urBarWindow.UpdateOpacitySettings(
            _settingsVm.URBar.URBarLabelOpacity,
            _settingsVm.URBar.URBarSegmentOpacity,
            _settingsVm.URBar.URBarMarkerOpacity,
            _settingsVm.URBar.URBarHitErrorOpacity
          );
        }
        else if (e.PropertyName == nameof(OverlaySettingsViewModel.OverlayEnabled))
        {
          OnOverlayEnabledChanged(_settingsVm.Overlay.OverlayEnabled);
        }
      });
    }

    private void KeyOverlayVm_PropertyChanged(
      object? sender,
      System.ComponentModel.PropertyChangedEventArgs e
    )
    {
      DispatcherUtils.RunOnPrimary(() =>
      {
        if (e.PropertyName == nameof(KeyOverlaySettingsViewModel.KeyOverlayEnabled))
        {
          OnKeyOverlayEnabledChanged(_settingsVm.KeyOverlay.KeyOverlayEnabled);
        }
        else if (
          e.PropertyName == nameof(KeyOverlaySettingsViewModel.KeyOverlayHeight)
          || e.PropertyName == nameof(KeyOverlaySettingsViewModel.KeyOverlayRotation)
          || e.PropertyName == nameof(KeyOverlaySettingsViewModel.KeyOverlayDurationMs)
          || e.PropertyName == nameof(KeyOverlaySettingsViewModel.KeyOverlayBarRound)
          || e.PropertyName == nameof(KeyOverlaySettingsViewModel.KeyOverlayLaneWidth)
          || e.PropertyName == nameof(KeyOverlaySettingsViewModel.KeyOverlayInputBarOpacity)
        )
        {
          ApplyKeyOverlaySettings();
        }
      });
    }

    public void EnterSettingsMode()
    {
      DispatcherUtils.RunOnPrimary(() =>
      {
        _isSettingsOpen = true;
        if (!_mainViewModel.IsPlaying)
        {
          PositionOverlaysToOsu();
          _overlayWindow.Show();
          _overlayWindow.SetDraggable(true);
          _urBarWindow.SetSettingsMode(true, _settingsVm.URBar.URBarWidth, _settingsVm.URBar.URBarHeight);
          _urBarWindow.Show();
          _keyOverlayThread.SetDraggable(true);
          ApplyKeyOverlaySettings();
          _keyOverlayThread.Show();
        }
      });
    }

    public void LeaveSettingsMode()
    {
      DispatcherUtils.RunOnPrimary(() =>
      {
        _isSettingsOpen = false;
        if (!_mainViewModel.IsPlaying)
        {
          _overlayWindow.SetDraggable(false);
          _urBarWindow.SetSettingsMode(false, _settingsVm.URBar.URBarWidth, _settingsVm.URBar.URBarHeight);

          _overlayWindow.Hide();
          _urBarWindow.Hide();
          _keyOverlayThread.SetDraggable(false);
          _keyOverlayThread.Hide();
        }
      });
    }

    public void HideOverlays()
    {
      DispatcherUtils.RunOnPrimary(() =>
      {
        _overlayWindow?.Hide();
        _urBarWindow?.Hide();
      });
      _keyOverlayThread?.Hide();
    }

    private void OnIsPlayingChanged(bool isPlaying)
    {
      DispatcherUtils.RunOnPrimary(() =>
      {
        if (isPlaying)
        {
          PositionOverlaysToOsu();

          if (_settingsVm.Overlay.OverlayEnabled)
          {
            _overlayWindow.SetDraggable(false);
            _overlayWindow.Show();
            HideMainWindow();
          }
          else
          {
            _overlayWindow.Hide();
            ShowMainWindow();
          }

          if (_settingsVm.URBar.URBarEnabled)
          {
            _urBarWindow.SetSettingsMode(false, _settingsVm.URBar.URBarWidth, _settingsVm.URBar.URBarHeight);
            _urBarWindow.Show();
          }
          else
          {
            _urBarWindow.Hide();
          }

          if (_settingsVm.KeyOverlay.KeyOverlayEnabled)
          {
            _keyOverlayThread.SetDraggable(false);
            ApplyKeyOverlaySettings();
            _keyOverlayThread.Show();
          }
          else
          {
            _keyOverlayThread.Hide();
          }
        }
        else
        {
          _overlayWindow.Hide();
          _urBarWindow.Hide();
          _keyOverlayThread.Hide();

          if (_isSettingsOpen)
          {
            PositionOverlaysToOsu();
            _overlayWindow.Show();
            _overlayWindow.SetDraggable(true);
            _urBarWindow.SetSettingsMode(true, _settingsVm.URBar.URBarWidth, _settingsVm.URBar.URBarHeight);
            _urBarWindow.Show();
            _keyOverlayThread.SetDraggable(true);
            ApplyKeyOverlaySettings();
            _keyOverlayThread.Show();
          }
          ShowMainWindow();
        }
      });
    }

    private void OnOsuWindowFound(IntPtr handle)
    {
      if (_settingsVm.Position.OsuPositionEnabled)
      {
        DispatcherUtils.RunOnPrimary(() =>
        {
          Win32Interop.SetWindowPos(
            handle,
            IntPtr.Zero,
            (int)_settingsVm.Position.OsuX,
            (int)_settingsVm.Position.OsuY,
            0,
            0,
            Win32Interop.SWP_NOSIZE | Win32Interop.SWP_NOZORDER
          );
        });
      }
    }

    public bool TryGetOsuWindowRect(out Win32Interop.Win32Rect rect) =>
      _processMonitor.TryGetOsuWindowRect(out rect);

    private Win32Interop.Win32Rect? TryGetOsuWindowRectOrNull() =>
      TryGetOsuWindowRect(out var rect) ? rect : null;

    private void PositionOverlaysToOsu()
    {
      DispatcherUtils.RunOnPrimary(() =>
      {
        if (!_processMonitor.EnsureProcess())
          return;
        if (!TryGetOsuWindowRect(out var rect))
          return;

        ApplyOverlayPosition(rect);
        ApplyURBarPosition(rect);
        ApplyKeyOverlayPosition(rect);
      });
    }

    private void ApplyOverlayPosition(Win32Interop.Win32Rect rect)
    {
      var (left, top) = _overlayPosition.ToScreen(rect);
      _overlayWindow.Left = left;
      _overlayWindow.Top = top;
    }

    private void ApplyURBarPosition(Win32Interop.Win32Rect rect)
    {
      var (left, top) = _urBarPosition.ToScreen(rect);
      _urBarWindow.Left = left;
      _urBarWindow.Top = top;
    }

    private void ApplyKeyOverlayPosition(Win32Interop.Win32Rect rect)
    {
      var (left, top) = _keyOverlayPosition.ToScreen(rect);
      _keyOverlayThread.SetPosition(left, top);
    }

    private void SyncKeyOverlayPositionIfIdle(Win32Interop.Win32Rect rect)
    {
      var (left, top) = _keyOverlayPosition.ToScreen(rect);
      _keyOverlayThread.ApplyPositionIfIdle(left, top);
    }

    private void HideMainWindow()
    {
      var window = _mainWindow;
      window?.Dispatcher.BeginInvoke(() => window.Hide());
    }

    private void ShowMainWindow()
    {
      var window = _mainWindow;
      window?.Dispatcher.BeginInvoke(() => window.Show());
    }

    private Task OnOsuWindowRectAvailableAsync(Win32Interop.Win32Rect rect)
    {
      var primaryTask = Application
        .Current.Dispatcher.InvokeAsync(() =>
        {
          if (_suppressCount > 0)
            return;
          if (_overlayWindow.IsVisible && !_overlayWindow.IsDragging)
            ApplyOverlayPosition(rect);
          if (_urBarWindow.IsVisible && !_urBarWindow.IsDragging && !_urBarWindow.IsResizing)
            ApplyURBarPosition(rect);
        })
        .Task;

      if (Volatile.Read(ref _suppressCount) == 0)
        SyncKeyOverlayPositionIfIdle(rect);

      return primaryTask;
    }

    public void Dispose()
    {
      _overlayTrackingCts?.Cancel();
      _overlayTrackingCts?.Dispose();
      _overlayTrackingCts = null;
      _keyOverlayThread.Dispose();
    }

    private void OnOverlayEnabledChanged(bool enabled)
    {
      if (_mainViewModel.IsPlaying)
      {
        if (enabled)
        {
          _overlayWindow.SetDraggable(false);
          _overlayWindow.Show();
          HideMainWindow();
        }
        else
        {
          _overlayWindow.Hide();
          ShowMainWindow();
        }
      }
      else if (_isSettingsOpen)
      {
        if (enabled)
          _overlayWindow.Show();
        else
          _overlayWindow.Hide();
      }
      else if (!enabled)
      {
        _overlayWindow.Hide();
      }
    }

    private void OnURBarEnabledChanged(bool enabled)
    {
      if (_isSettingsOpen && !_mainViewModel.IsPlaying)
      {
        _urBarWindow.SetSettingsMode(true, _settingsVm.URBar.URBarWidth, _settingsVm.URBar.URBarHeight);
        _urBarWindow.Show();
      }
      else if (!enabled)
      {
        _urBarWindow.Hide();
      }
    }

    private void OnKeyOverlayEnabledChanged(bool enabled)
    {
      if (_isSettingsOpen && !_mainViewModel.IsPlaying)
      {
        _keyOverlayThread.SetDraggable(true);
        ApplyKeyOverlaySettings();
        _keyOverlayThread.Show();
      }
      else if (!enabled)
      {
        _keyOverlayThread.Hide();
      }
    }

    private void ApplyKeyOverlaySettings()
    {
      _keyOverlayThread.UpdateSettings(
        _settingsVm.KeyOverlay.KeyOverlayRotation,
        _settingsVm.KeyOverlay.KeyOverlayHeight,
        _settingsVm.KeyOverlay.KeyOverlayDurationMs,
        _settingsVm.KeyOverlay.KeyOverlayBarRound,
        _settingsVm.KeyOverlay.KeyOverlayLaneWidth,
        _settingsVm.FontFamily,
        _settingsVm.KeyOverlay.KeyOverlayInputBarOpacity
      );
    }
  }
}
