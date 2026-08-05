using OsuMate.Services;
using OsuMate.Services.PlayLog;
using OsuMate.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace OsuMate.Views
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _mainViewModel;
        private readonly SettingsViewModel _settingsVm;
        private readonly PlayLogViewModel _playLogVm;
        private readonly TrainerViewModel _trainerVm;
        private readonly WindowManagerService _windowManager;
        private enum TabKind { Main, Trainer, Settings, Log }
        private TabKind _currentTab = TabKind.Main;
        private int _suppressCount = 0;
        // Logタブ切り替え時にウィンドウサイズを固定するために記録する
        private double _windowWidth  = double.NaN;
        private double _windowHeight = double.NaN;

        public MainWindow(MainViewModel mainViewModel, SettingsViewModel settingsVm, PlayLogViewModel playLogVm, TrainerViewModel trainerVm, WindowManagerService windowManager)
        {
            InitializeComponent();

            _mainViewModel = mainViewModel;
            _settingsVm = settingsVm;
            _playLogVm = playLogVm;
            _trainerVm = trainerVm;
            _windowManager = windowManager;

            if (!_settingsVm.IsDarkMode)
                _mainViewModel.Theme.Toggle();
            _mainViewModel.Theme.SetFont(_settingsVm.FontFamily);
            _mainViewModel.OnThemeChanged();
            // PlayLogViewModel.PlayStatsChartVM / ContributionChartVM は MainViewModel のグラフ群
            // （Themeables）とは別のDIオブジェクトグラフに属するため、OnThemeChanged() の対象には
            // 含まれない。ここで同じタイミングで明示的に ApplyTheme を呼び、テーマ反映の起点を1箇所
            // （MainWindow）に保つ。
            _playLogVm.PlayStatsChartVM.ApplyTheme(_mainViewModel.Theme.Current);
            _playLogVm.ContributionChartVM.ApplyTheme(_mainViewModel.Theme.Current);
            _mainViewModel.SetOverlayFontSize(_settingsVm.OverlayFontSize);
            _mainViewModel.SetOverlayShowValueFirst(_settingsVm.IsShowValueFirst);

            _settingsVm.PropertyChanged += (_, e) =>
            {
                switch (e.PropertyName)
                {
                    case nameof(SettingsViewModel.IsDarkMode):
                        if (_mainViewModel.Theme.IsDark != _settingsVm.IsDarkMode)
                        {
                            _mainViewModel.Theme.Toggle();
                            _mainViewModel.OnThemeChanged();
                            _playLogVm.PlayStatsChartVM.ApplyTheme(_mainViewModel.Theme.Current);
                            _playLogVm.ContributionChartVM.ApplyTheme(_mainViewModel.Theme.Current);
                            // アニメーション完了後にキャッシュ無効化（ラグ防止）
                            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,
                                () => SettingsPanel.InvalidateBitmapCache());
                        }
                        break;
                    case nameof(SettingsViewModel.FontFamily):
                        _mainViewModel.Theme.SetFont(_settingsVm.FontFamily);
                        _mainViewModel.OnThemeChanged();
                        _playLogVm.PlayStatsChartVM.ApplyTheme(_mainViewModel.Theme.Current);
                        _playLogVm.ContributionChartVM.ApplyTheme(_mainViewModel.Theme.Current);
                        SettingsPanel.InvalidateBitmapCache();
                        break;
                    case nameof(SettingsViewModel.OverlayFontSize):
                        _mainViewModel.SetOverlayFontSize(_settingsVm.OverlayFontSize);
                        Interlocked.Increment(ref _suppressCount);
                        _ = Task.Delay(1000).ContinueWith(_ => Interlocked.Decrement(ref _suppressCount));
                        break;
                    case nameof(SettingsViewModel.IsShowValueFirst):
                        _mainViewModel.SetOverlayShowValueFirst(_settingsVm.IsShowValueFirst);
                        break;
                }
            };

            DataContext = _mainViewModel;
            MainPanel.DataContext = _mainViewModel;
            SettingsPanel.DataContext = _settingsVm;
            LogPanel.DataContext = _playLogVm;
            TrainerPanel.DataContext = _trainerVm;

            Loaded += (_, _) =>
            {
                if (_settingsVm.AppPositionEnabled)
                {
                    this.Left = _settingsVm.AppX;
                    this.Top = _settingsVm.AppY;
                }
                _windowWidth  = this.ActualWidth;
                _windowHeight = this.ActualHeight;
            };

            _settingsVm.Items.CollectionChanged += (_, _) => SyncOverlayIds();
            foreach (var item in _settingsVm.Items)
                item.PropertyChanged += (_, _) => SyncOverlayIds();
            SyncOverlayIds();

            _mainViewModel.Start(Dispatcher);

            // 起動時にバックグラウンドでログを読み込む
            _ = Task.Run(async () =>
            {
                await _playLogVm.LoadAsync();
            });

            TabMain.Tag = "Active";
            TabTrainer.Tag = null;
            TabSettings.Tag = null;
            TabLog.Tag = null;

            // 起動時はMainタブが表示されているため、Trainerのポーリングは停止しておく
            TrainerPanel.SuspendBinding();
        }

        // -----------------------------------------------------------------

        private void SyncOverlayIds()
        {
            var ids = _settingsVm.Items
                .Where(i => i.IsEnabled)
                .Select(i => i.Id)
                .ToList();
            _mainViewModel.SetEnabledOverlayIds(ids);

            Interlocked.Increment(ref _suppressCount);
            _ = Task.Delay(600).ContinueWith(_ => Interlocked.Decrement(ref _suppressCount));
        }

        private void TabMain_Click(object sender, RoutedEventArgs e)
        {
            if (_currentTab != TabKind.Main) ShowMain();
        }

        private void TabTrainer_Click(object sender, RoutedEventArgs e)
        {
            if (_currentTab != TabKind.Trainer) ShowTrainer();
        }

        private void TabSettings_Click(object sender, RoutedEventArgs e)
        {
            if (_currentTab != TabKind.Settings) ShowSettings();
        }

        private void TabLog_Click(object sender, RoutedEventArgs e)
        {
            if (_currentTab != TabKind.Log) ShowLog();
        }

        private void ShowMain() => SwitchTab(TabKind.Main);
        private void ShowSettings() => SwitchTab(TabKind.Settings);
        private void ShowTrainer() => SwitchTab(TabKind.Trainer);
        private void ShowLog() => SwitchTab(TabKind.Log);

        /// <summary>
        /// タブ切替の共通処理
        /// </summary>
        private void SwitchTab(TabKind tab)
        {
            _currentTab = tab;
            TabMain.Tag     = tab == TabKind.Main     ? "Active" : null;
            TabTrainer.Tag  = tab == TabKind.Trainer  ? "Active" : null;
            TabSettings.Tag = tab == TabKind.Settings ? "Active" : null;
            TabLog.Tag      = tab == TabKind.Log      ? "Active" : null;

            // Settings/Trainer/Log は SizeToContent.Manual で直前のウィンドウサイズを再利用するため、
            // 切り替え前に現在のサイズを記憶しておく（Mainは自動サイズなのでここでは記憶しない）。
            if (tab != TabKind.Main)
            {
                if (this.ActualWidth  > 0) _windowWidth  = this.ActualWidth;
                if (this.ActualHeight > 0) _windowHeight = this.ActualHeight;
            }

            // 表示しないパネルを畳み、サイズ指定をリセットする。
            // Trainer/Log は非表示中にバックグラウンドポーリングし続けないよう SuspendBinding する
            // （Main/Settings にはポーリング停止の仕組みが無いため対象外）。
            if (tab != TabKind.Main)
            {
                MainPanel.Visibility = Visibility.Collapsed;
            }
            if (tab != TabKind.Settings)
            {
                SettingsPanel.Visibility = Visibility.Collapsed;
                SettingsPanel.Width  = double.NaN;
                SettingsPanel.Height = double.NaN;
            }
            if (tab != TabKind.Trainer)
            {
                TrainerPanel.Visibility = Visibility.Collapsed;
                TrainerPanel.Width  = double.NaN;
                TrainerPanel.Height = double.NaN;
                TrainerPanel.SuspendBinding();
            }
            if (tab != TabKind.Log)
            {
                LogPanel.Visibility = Visibility.Collapsed;
                LogPanel.Width  = double.NaN;
                LogPanel.Height = double.NaN;
                LogPanel.SuspendBinding();
            }

            if (tab == TabKind.Main)
            {
                // 元の ShowMain() と同じ順序を維持する: 他パネルを畳んだ直後、
                // MainPanel を先に表示してからウィンドウサイズをコンテンツ追従に戻す。
                // 逆順（リサイズ→表示）にすると、他パネルもMainPanelもまだ非表示の
                // 一瞬だけウィンドウが極小サイズへ縮み、MainPanelが表示された直後に
                // 再び広がるという「一瞬だけ閉じる」ちらつきが発生してしまう。
                MainPanel.Visibility = Visibility.Visible;

                // SizeToContent をコンテンツ追従に戻す（Manual のままだとウィンドウが縮まない）
                Width  = double.NaN;
                Height = double.NaN;
                SizeToContent = SizeToContent.WidthAndHeight;

                _windowManager.LeaveSettingsMode();
                _settingsVm.Save();
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
                {
                    if (this.ActualWidth  > 0) _windowWidth  = this.ActualWidth;
                    if (this.ActualHeight > 0) _windowHeight = this.ActualHeight;
                });
                return;
            }

            // ウィンドウサイズを固定してGrid Row="*" で対象パネルが残り高さを埋める
            SizeToContent = SizeToContent.Manual;
            Width  = _windowWidth;
            Height = _windowHeight;

            switch (tab)
            {
                case TabKind.Settings:
                    SettingsPanel.Width  = double.NaN;
                    SettingsPanel.Height = double.NaN;
                    SettingsPanel.Visibility = Visibility.Visible;
                    _windowManager.EnterSettingsMode();
                    break;

                case TabKind.Trainer:
                    TrainerPanel.Width  = double.NaN;
                    TrainerPanel.Height = double.NaN;
                    TrainerPanel.Visibility = Visibility.Visible;
                    TrainerPanel.ResumeBinding();
                    _windowManager.LeaveSettingsMode();
                    break;

                case TabKind.Log:
                    LogPanel.Width  = double.NaN;
                    LogPanel.Height = double.NaN;
                    LogPanel.Visibility = Visibility.Visible;
                    LogPanel.ResumeBinding();
                    _windowManager.LeaveSettingsMode();
                    break;
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is Button ||
                e.OriginalSource is CheckBox ||
                e.OriginalSource is Slider ||
                e.OriginalSource is ListBox ||
                e.OriginalSource is Border) return;
            DragMove();
        }

        protected override void OnClosed(EventArgs e)
        {
            _windowManager.HideOverlays();
            _mainViewModel.Stop();
            _trainerVm.Dispose();
            Application.Current.Shutdown();
            base.OnClosed(e);
        }
    }
}
