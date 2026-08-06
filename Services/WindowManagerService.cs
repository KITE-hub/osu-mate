using OsuMate.Utils;
using OsuMate.ViewModels;
using System.Windows;

namespace OsuMate.Services
{
    public class WindowManagerService
    {
        private readonly MainViewModel _mainViewModel;
        private readonly SettingsViewModel _settingsVm;
        private readonly OsuProcessMonitorService _processMonitor;
        private readonly Views.InGameOverlayWindow _overlayWindow;
        private readonly Views.URBarWindow _urBarWindow;

        private CancellationTokenSource? _overlayTrackingCts;
        private int _suppressCount = 0;
        private bool _isSettingsOpen = false;

        private double _currentOverlayX;
        private double _currentOverlayY;
        private double _currentURBarX;
        private double _currentURBarY;
        private double _currentURBarWidth;
        private double _currentURBarHeight;

        public WindowManagerService(MainViewModel mainViewModel, SettingsViewModel settingsVm, OsuProcessMonitorService processMonitor)
        {
            _mainViewModel = mainViewModel;
            _settingsVm = settingsVm;
            _processMonitor = processMonitor;

            _overlayWindow = new Views.InGameOverlayWindow(_mainViewModel.InGameOverlay);
            _overlayWindow.PositionChanged += (left, top) =>
            {
                if (TryGetOsuWindowRect(out var rect))
                {
                    _currentOverlayX = left - rect.Left;
                    _currentOverlayY = top - rect.Top;
                }
                else
                {
                    _currentOverlayX = left;
                    _currentOverlayY = top;
                }
            };

            _urBarWindow = new Views.URBarWindow();
            _urBarWindow.SetViewModel(_mainViewModel.URBar);
            _urBarWindow.SetRotation(_settingsVm.URBarRotation);
            _urBarWindow.UpdateAnimationSettings(_settingsVm.URBarAvgLineFollowStrength, _settingsVm.URBarAvgLineAnimMs);
            _urBarWindow.UpdateOpacitySettings(_settingsVm.URBarLabelOpacity, _settingsVm.URBarSegmentOpacity, _settingsVm.URBarMarkerOpacity, _settingsVm.URBarHitErrorOpacity);
            _urBarWindow.PositionChanged += (left, top) =>
            {
                if (TryGetOsuWindowRect(out var rect))
                {
                    _currentURBarX = left - rect.Left;
                    _currentURBarY = top - rect.Top;
                }
                else
                {
                    _currentURBarX = left;
                    _currentURBarY = top;
                }
            };
            _urBarWindow.OnSizeChanged += (w, h) =>
            {
                _currentURBarWidth = w;
                _currentURBarHeight = h;
            };

            _currentOverlayX = _settingsVm.OverlayX;
            _currentOverlayY = _settingsVm.OverlayY;
            _currentURBarX = _settingsVm.URBarX;
            _currentURBarY = _settingsVm.URBarY;
            _currentURBarWidth = _settingsVm.URBarWidth;
            _currentURBarHeight = _settingsVm.URBarHeight;

            _settingsVm.OnSaveOverlayPositionRequested += () => { _settingsVm.OverlayX = _currentOverlayX; _settingsVm.OverlayY = _currentOverlayY; _settingsVm.Save(); };
            _settingsVm.OnApplyOverlayPositionRequested += () => { _currentOverlayX = _settingsVm.OverlayX; _currentOverlayY = _settingsVm.OverlayY; PositionOverlaysToOsu(); };

            _settingsVm.OnSaveURBarPositionRequested += () => { _settingsVm.URBarX = _currentURBarX; _settingsVm.URBarY = _currentURBarY; _settingsVm.Save(); };
            _settingsVm.OnApplyURBarPositionRequested += () => { _currentURBarX = _settingsVm.URBarX; _currentURBarY = _settingsVm.URBarY; PositionOverlaysToOsu(); };

            _settingsVm.OnSaveURBarSizeRequested += () => { _settingsVm.URBarWidth = _currentURBarWidth; _settingsVm.URBarHeight = _currentURBarHeight; _settingsVm.Save(); };
            _settingsVm.OnApplyURBarSizeRequested += () => {
                _currentURBarWidth = _settingsVm.URBarWidth;
                _currentURBarHeight = _settingsVm.URBarHeight;
                _urBarWindow.SetSettingsMode(_isSettingsOpen, _currentURBarWidth, _currentURBarHeight);
            };

            _settingsVm.PropertyChanged += SettingsVm_PropertyChanged;
            _mainViewModel.IsPlayingChanged += OnIsPlayingChanged;
            _mainViewModel.OnOsuWindowFound += OnOsuWindowFound;

            _overlayTrackingCts = new CancellationTokenSource();
            _ = _processMonitor.StartTrackingAsync(OnOsuWindowRectAvailableAsync, _overlayTrackingCts.Token);
        }

        private void SettingsVm_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
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

                if (TryGetOsuWindowRect(out var rect))
                {
                    _currentURBarX = _urBarWindow.Left - rect.Left;
                    _currentURBarY = _urBarWindow.Top - rect.Top;
                }
                else
                {
                    _currentURBarX = _urBarWindow.Left;
                    _currentURBarY = _urBarWindow.Top;
                }
            }
            else if (e.PropertyName == nameof(SettingsViewModel.URBarEnabled))
            {
                OnURBarEnabledChanged(_settingsVm.URBarEnabled);
            }
            else if (e.PropertyName == nameof(SettingsViewModel.URBarAvgLineFollowStrength)
                  || e.PropertyName == nameof(SettingsViewModel.URBarAvgLineAnimMs))
            {
                _urBarWindow.UpdateAnimationSettings(_settingsVm.URBarAvgLineFollowStrength, _settingsVm.URBarAvgLineAnimMs);
            }
            else if (e.PropertyName == nameof(SettingsViewModel.URBarLabelOpacity)
                  || e.PropertyName == nameof(SettingsViewModel.URBarMarkerOpacity)
                  || e.PropertyName == nameof(SettingsViewModel.URBarSegmentOpacity)
                  || e.PropertyName == nameof(SettingsViewModel.URBarHitErrorOpacity))
            {
                _urBarWindow.UpdateOpacitySettings(_settingsVm.URBarLabelOpacity, _settingsVm.URBarSegmentOpacity, _settingsVm.URBarMarkerOpacity, _settingsVm.URBarHitErrorOpacity);
            }
            else if (e.PropertyName == nameof(SettingsViewModel.OverlayEnabled))
            {
                OnOverlayEnabledChanged(_settingsVm.OverlayEnabled);
            }
        }

        public void EnterSettingsMode()
        {
            _isSettingsOpen = true;
            if (!_mainViewModel.IsPlaying)
            {
                PositionOverlaysToOsu();
                _overlayWindow.Show();
                _overlayWindow.SetDraggable(true);
                _urBarWindow.SetSettingsMode(true, _settingsVm.URBarWidth, _settingsVm.URBarHeight);
                _urBarWindow.Show();
            }
        }

        public void LeaveSettingsMode()
        {
            _isSettingsOpen = false;
            if (!_mainViewModel.IsPlaying)
            {
                // Hide() する前に見た目を通常状態へ戻す
                _overlayWindow.SetDraggable(false);
                _urBarWindow.SetSettingsMode(false, _settingsVm.URBarWidth, _settingsVm.URBarHeight);

                _overlayWindow.Hide();
                _urBarWindow.Hide();
            }
        }

        public void HideOverlays()
        {
            _overlayWindow?.Hide();
            _urBarWindow?.Hide();
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

                    if (_settingsVm.OverlayEnabled && Application.Current.MainWindow != null)
                        Application.Current.MainWindow.Hide();
                }
                else
                {
                    _overlayWindow.Hide();
                    _urBarWindow.Hide();

                    if (_isSettingsOpen)
                    {
                        PositionOverlaysToOsu();
                        _overlayWindow.Show();
                        _overlayWindow.SetDraggable(true);
                        _urBarWindow.SetSettingsMode(true, _settingsVm.URBarWidth, _settingsVm.URBarHeight);
                        _urBarWindow.Show();
                    }
                    if (Application.Current.MainWindow != null)
                        Application.Current.MainWindow.Show();
                }
            });
        }

        private void OnOsuWindowFound(IntPtr handle)
        {
            if (_settingsVm.OsuPositionEnabled)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    Win32Interop.SetWindowPos(handle, IntPtr.Zero, (int)_settingsVm.OsuX, (int)_settingsVm.OsuY, 0, 0, Win32Interop.SWP_NOSIZE | Win32Interop.SWP_NOZORDER);
                });
            }
        }

        /// <summary>
        /// osu!ウィンドウの現在位置(Rect)取得は OsuProcessMonitorService の責務。
        /// WindowManagerService はオーバーレイ配置計算のためにこれを利用するだけ。
        /// </summary>
        public bool TryGetOsuWindowRect(out Win32Interop.Win32Rect rect)
            => _processMonitor.TryGetOsuWindowRect(out rect);

        private void PositionOverlaysToOsu()
        {
            if (!_processMonitor.EnsureProcess()) return;
            if (!TryGetOsuWindowRect(out var rect)) return;

            _overlayWindow.Left = rect.Left + _currentOverlayX;
            _overlayWindow.Top = rect.Top + _currentOverlayY;
            _urBarWindow.Left = rect.Left + _currentURBarX;
            _urBarWindow.Top = rect.Top + _currentURBarY;
        }

        /// <summary>
        /// OsuProcessMonitorService から16ms間隔で通知されるosu!ウィンドウRectを受けて、
        /// 表示中のオーバーレイ位置をUIスレッド上で更新する。
        /// </summary>
        private Task OnOsuWindowRectAvailableAsync(Win32Interop.Win32Rect rect)
        {
            return Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (_suppressCount > 0) return;
                if (_overlayWindow.IsVisible && !_overlayWindow.IsDragging)
                {
                    _overlayWindow.Left = rect.Left + _currentOverlayX;
                    _overlayWindow.Top = rect.Top + _currentOverlayY;
                }
                if (_urBarWindow.IsVisible && !_urBarWindow.IsDragging && !_urBarWindow.IsResizing)
                {
                    _urBarWindow.Left = rect.Left + _currentURBarX;
                    _urBarWindow.Top = rect.Top + _currentURBarY;
                }
            }).Task;
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
    }
}
