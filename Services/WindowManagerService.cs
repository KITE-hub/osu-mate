using System.Windows;
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
    private readonly Views.KeyOverlayWindow _keyOverlayWindow;
    private Window? _mainWindow;

    private CancellationTokenSource? _overlayTrackingCts;
    private int _suppressCount = 0;
    private bool _isSettingsOpen = false;

    private readonly RelativeWindowPosition _overlayPosition;
    private readonly RelativeWindowPosition _urBarPosition;
    private readonly RelativeWindowSize _urBarSize;
    private readonly RelativeWindowPosition _keyOverlayPosition;

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

      _overlayPosition = new RelativeWindowPosition(_settingsVm.OverlayX, _settingsVm.OverlayY);
      _urBarPosition = new RelativeWindowPosition(_settingsVm.URBarX, _settingsVm.URBarY);
      _urBarSize = new RelativeWindowSize(_settingsVm.URBarWidth, _settingsVm.URBarHeight);
      _keyOverlayPosition = new RelativeWindowPosition(
        _settingsVm.KeyOverlayX,
        _settingsVm.KeyOverlayY
      );

      _overlayWindow = new Views.InGameOverlayWindow(_mainViewModel.InGameOverlay);
      _overlayWindow.PositionChanged += HandleOverlayWindowPositionChanged;

      _urBarWindow = new Views.URBarWindow();
      _urBarWindow.SetViewModel(_mainViewModel.URBar);
      _urBarWindow.SetRotation(_settingsVm.URBarRotation);
      _urBarWindow.UpdateAnimationSettings(
        _settingsVm.URBarAvgLineFollowStrength,
        _settingsVm.URBarAvgLineAnimMs
      );
      _urBarWindow.UpdateOpacitySettings(
        _settingsVm.URBarLabelOpacity,
        _settingsVm.URBarSegmentOpacity,
        _settingsVm.URBarMarkerOpacity,
        _settingsVm.URBarHitErrorOpacity
      );
      _urBarWindow.PositionChanged += HandleURBarWindowPositionChanged;
      _urBarWindow.OnSizeChanged += HandleURBarWindowSizeChanged;

      _keyOverlayWindow = new Views.KeyOverlayWindow(_mainViewModel.KeyOverlay);
      _rawInput.Attach(_keyOverlayWindow);
      ApplyKeyOverlaySettings();
      _keyOverlayWindow.PositionChanged += HandleKeyOverlayWindowPositionChanged;
      _keyOverlayWindow.FlowLengthChanged += length => _settingsVm.KeyOverlayHeight = length;

      _settingsVm.OnSaveOverlayPositionRequested += HandleSaveOverlayPositionRequested;
      _settingsVm.OnApplyOverlayPositionRequested += HandleApplyOverlayPositionRequested;
      _settingsVm.OnSaveURBarPositionRequested += HandleSaveURBarPositionRequested;
      _settingsVm.OnApplyURBarPositionRequested += HandleApplyURBarPositionRequested;
      _settingsVm.OnSaveURBarSizeRequested += HandleSaveURBarSizeRequested;
      _settingsVm.OnApplyURBarSizeRequested += HandleApplyURBarSizeRequested;
      _settingsVm.OnSaveKeyOverlayPositionRequested += HandleSaveKeyOverlayPositionRequested;
      _settingsVm.OnApplyKeyOverlayPositionRequested += HandleApplyKeyOverlayPositionRequested;

      _settingsVm.PropertyChanged += SettingsVm_PropertyChanged;
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
      _keyOverlayPosition.CaptureFromScreen(left, top, TryGetOsuWindowRectOrNull());
    }

    private void HandleSaveOverlayPositionRequested()
    {
      _settingsVm.SetOverlayPosition(_overlayPosition.X, _overlayPosition.Y);
    }

    private void HandleApplyOverlayPositionRequested()
    {
      _overlayPosition.SetValue(_settingsVm.OverlayX, _settingsVm.OverlayY);
      PositionOverlaysToOsu();
    }

    private void HandleSaveURBarPositionRequested()
    {
      _settingsVm.SetURBarPosition(_urBarPosition.X, _urBarPosition.Y);
    }

    private void HandleApplyURBarPositionRequested()
    {
      _urBarPosition.SetValue(_settingsVm.URBarX, _settingsVm.URBarY);
      PositionOverlaysToOsu();
    }

    private void HandleSaveURBarSizeRequested()
    {
      _settingsVm.URBarWidth = _urBarSize.Width;
      _settingsVm.URBarHeight = _urBarSize.Height;
      _settingsVm.Save();
    }

    private void HandleApplyURBarSizeRequested()
    {
      _urBarSize.SetValue(_settingsVm.URBarWidth, _settingsVm.URBarHeight);
      Application.Current.Dispatcher.Invoke(() =>
        _urBarWindow.SetSettingsMode(_isSettingsOpen, _urBarSize.Width, _urBarSize.Height)
      );
    }

    private void HandleSaveKeyOverlayPositionRequested()
    {
      _settingsVm.SetKeyOverlayPosition(_keyOverlayPosition.X, _keyOverlayPosition.Y);
    }

    private void HandleApplyKeyOverlayPositionRequested()
    {
      _keyOverlayPosition.SetValue(_settingsVm.KeyOverlayX, _settingsVm.KeyOverlayY);
      PositionOverlaysToOsu();
    }

    private void SettingsVm_PropertyChanged(
      object? sender,
      System.ComponentModel.PropertyChangedEventArgs e
    )
    {
      Application.Current.Dispatcher.Invoke(() =>
      {
        if (e.PropertyName == nameof(SettingsViewModel.OverlayFontSize))
        {
          Interlocked.Increment(ref _suppressCount);
          _ = Task.Delay(1000).ContinueWith(_ => Interlocked.Decrement(ref _suppressCount));
        }
        else if (e.PropertyName == nameof(SettingsViewModel.URBarRotation))
        {
          double cx = _urBarWindow.Left + _urBarWindow.Width / 2;
          double cy = _urBarWindow.Top + _urBarWindow.Height / 2;

          _urBarWindow.SetRotation(_settingsVm.URBarRotation);

          _urBarWindow.Left = cx - _urBarWindow.Width / 2;
          _urBarWindow.Top = cy - _urBarWindow.Height / 2;

          _urBarPosition.CaptureFromScreen(
            _urBarWindow.Left,
            _urBarWindow.Top,
            TryGetOsuWindowRectOrNull()
          );
        }
        else if (e.PropertyName == nameof(SettingsViewModel.URBarEnabled))
        {
          OnURBarEnabledChanged(_settingsVm.URBarEnabled);
        }
        else if (e.PropertyName == nameof(SettingsViewModel.KeyOverlayEnabled))
        {
          OnKeyOverlayEnabledChanged(_settingsVm.KeyOverlayEnabled);
        }
        else if (
          e.PropertyName == nameof(SettingsViewModel.KeyOverlayHeight)
          || e.PropertyName == nameof(SettingsViewModel.KeyOverlayRotation)
          || e.PropertyName == nameof(SettingsViewModel.KeyOverlayBarSpeed)
          || e.PropertyName == nameof(SettingsViewModel.KeyOverlayBarRound)
          || e.PropertyName == nameof(SettingsViewModel.KeyOverlayLaneWidth)
        )
        {
          ApplyKeyOverlaySettings();
        }
        else if (
          e.PropertyName == nameof(SettingsViewModel.URBarAvgLineFollowStrength)
          || e.PropertyName == nameof(SettingsViewModel.URBarAvgLineAnimMs)
        )
        {
          _urBarWindow.UpdateAnimationSettings(
            _settingsVm.URBarAvgLineFollowStrength,
            _settingsVm.URBarAvgLineAnimMs
          );
        }
        else if (
          e.PropertyName == nameof(SettingsViewModel.URBarLabelOpacity)
          || e.PropertyName == nameof(SettingsViewModel.URBarMarkerOpacity)
          || e.PropertyName == nameof(SettingsViewModel.URBarSegmentOpacity)
          || e.PropertyName == nameof(SettingsViewModel.URBarHitErrorOpacity)
        )
        {
          _urBarWindow.UpdateOpacitySettings(
            _settingsVm.URBarLabelOpacity,
            _settingsVm.URBarSegmentOpacity,
            _settingsVm.URBarMarkerOpacity,
            _settingsVm.URBarHitErrorOpacity
          );
        }
        else if (e.PropertyName == nameof(SettingsViewModel.OverlayEnabled))
        {
          OnOverlayEnabledChanged(_settingsVm.OverlayEnabled);
        }
      });
    }

    public void EnterSettingsMode()
    {
      Application.Current.Dispatcher.Invoke(() =>
      {
        _isSettingsOpen = true;
        if (!_mainViewModel.IsPlaying)
        {
          PositionOverlaysToOsu();
          _overlayWindow.Show();
          _overlayWindow.SetDraggable(true);
          _urBarWindow.SetSettingsMode(true, _settingsVm.URBarWidth, _settingsVm.URBarHeight);
          _urBarWindow.Show();
          _keyOverlayWindow.SetDraggable(true);
          ApplyKeyOverlaySettings();
          _keyOverlayWindow.Show();
        }
      });
    }

    public void LeaveSettingsMode()
    {
      Application.Current.Dispatcher.Invoke(() =>
      {
        _isSettingsOpen = false;
        if (!_mainViewModel.IsPlaying)
        {
          _overlayWindow.SetDraggable(false);
          _urBarWindow.SetSettingsMode(false, _settingsVm.URBarWidth, _settingsVm.URBarHeight);

          _overlayWindow.Hide();
          _urBarWindow.Hide();
          _keyOverlayWindow.SetDraggable(false);
          _keyOverlayWindow.Hide();
        }
      });
    }

    public void HideOverlays()
    {
      Application.Current.Dispatcher.Invoke(() =>
      {
        _overlayWindow?.Hide();
        _urBarWindow?.Hide();
        _keyOverlayWindow?.Hide();
      });
    }

    private void OnIsPlayingChanged(bool isPlaying)
    {
      Application.Current.Dispatcher.Invoke(() =>
      {
        if (isPlaying)
        {
          PositionOverlaysToOsu();

          if (_settingsVm.OverlayEnabled)
          {
            _overlayWindow.SetDraggable(false);
            _overlayWindow.Show();
          }
          else
          {
            _overlayWindow.Hide();
          }

          if (_settingsVm.URBarEnabled)
          {
            _urBarWindow.SetSettingsMode(false, _settingsVm.URBarWidth, _settingsVm.URBarHeight);
            _urBarWindow.Show();
          }
          else
          {
            _urBarWindow.Hide();
          }

          if (_settingsVm.KeyOverlayEnabled)
          {
            _keyOverlayWindow.SetDraggable(false);
            ApplyKeyOverlaySettings();
            _keyOverlayWindow.Show();
          }
          else
          {
            _keyOverlayWindow.Hide();
          }

          if (_settingsVm.OverlayEnabled || _settingsVm.KeyOverlayEnabled)
            HideMainWindow();
        }
        else
        {
          _overlayWindow.Hide();
          _urBarWindow.Hide();
          _keyOverlayWindow.Hide();

          if (_isSettingsOpen)
          {
            PositionOverlaysToOsu();
            _overlayWindow.Show();
            _overlayWindow.SetDraggable(true);
            _urBarWindow.SetSettingsMode(true, _settingsVm.URBarWidth, _settingsVm.URBarHeight);
            _urBarWindow.Show();
            _keyOverlayWindow.SetDraggable(true);
            ApplyKeyOverlaySettings();
            _keyOverlayWindow.Show();
          }
          ShowMainWindow();
        }
      });
    }

    private void OnOsuWindowFound(IntPtr handle)
    {
      if (_settingsVm.OsuPositionEnabled)
      {
        Application.Current.Dispatcher.Invoke(() =>
        {
          Win32Interop.SetWindowPos(
            handle,
            IntPtr.Zero,
            (int)_settingsVm.OsuX,
            (int)_settingsVm.OsuY,
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
      Application.Current.Dispatcher.Invoke(() =>
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
      _keyOverlayWindow.Left = left;
      _keyOverlayWindow.Top = top;
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
      return Application
        .Current.Dispatcher.InvokeAsync(() =>
        {
          if (_suppressCount > 0)
            return;
          if (_overlayWindow.IsVisible && !_overlayWindow.IsDragging)
            ApplyOverlayPosition(rect);
          if (_urBarWindow.IsVisible && !_urBarWindow.IsDragging && !_urBarWindow.IsResizing)
            ApplyURBarPosition(rect);
          if (_keyOverlayWindow.IsVisible && !_keyOverlayWindow.IsDragging && !_keyOverlayWindow.IsResizing)
            ApplyKeyOverlayPosition(rect);
        })
        .Task;
    }

    public void Dispose()
    {
      _overlayTrackingCts?.Cancel();
      _overlayTrackingCts?.Dispose();
      _overlayTrackingCts = null;
      _processMonitor.Dispose();
      _rawInput.Dispose();
    }

    private void OnOverlayEnabledChanged(bool enabled)
    {
      if (_isSettingsOpen && !_mainViewModel.IsPlaying)
        _overlayWindow.Show();
      else if (!enabled)
        _overlayWindow.Hide();
    }

    private void OnURBarEnabledChanged(bool enabled)
    {
      if (_isSettingsOpen && !_mainViewModel.IsPlaying)
      {
        _urBarWindow.SetSettingsMode(true, _settingsVm.URBarWidth, _settingsVm.URBarHeight);
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
        _keyOverlayWindow.SetDraggable(true);
        ApplyKeyOverlaySettings();
        _keyOverlayWindow.Show();
      }
      else if (!enabled)
      {
        _keyOverlayWindow.Hide();
      }
    }

    private void ApplyKeyOverlaySettings()
    {
      _keyOverlayWindow.UpdateSettings(
        _settingsVm.KeyOverlayRotation,
        _settingsVm.KeyOverlayHeight,
        _settingsVm.KeyOverlayBarSpeed,
        _settingsVm.KeyOverlayBarRound,
        _settingsVm.KeyOverlayLaneWidth
      );
    }
  }
}
