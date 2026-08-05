using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;
using System.Windows.Threading;
using OsuMate.Models;
using OsuMate.Services;
using OsuMate.Services.PlayLog;
using OsuMate.Services.Trainer;
using OsuMate.ViewModels;
using OsuMate.Views;
using System.Windows;

namespace OsuMate
{
    public partial class App : Application
    {
        public static IServiceProvider ServiceProvider { get; private set; } = null!;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // グローバル例外ハンドラ：個別のtry/catchで捕捉漏れがあった場合の最終防衛ライン。
            // UIスレッド上（Dispatcher経由）の例外。ここに来た時点でプロセスは基本的に延命できる。
            DispatcherUnhandledException += (s, args) =>
            {
                Utils.LogUtils.DebugLogger("Unhandled UI exception: " + args.Exception, true, writeToFile: true);
                args.Handled = true; // ログを残して継続。プレイ中にダイアログで割り込みたくないため通知はしない。
            };

            // UIスレッド以外（ThreadPool/Timerコールバック等）の例外。
            // 発火した時点でほぼ確実にプロセス終了は避けられないが、原因をログに残す。
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                Utils.LogUtils.DebugLogger("Unhandled non-UI exception: " + args.ExceptionObject, true, writeToFile: true);
            };

            // 観測されなかったTask例外（fire-and-forgetの Task.Run 等）。診断用。
            TaskScheduler.UnobservedTaskException += (s, args) =>
            {
                Utils.LogUtils.DebugLogger("Unobserved task exception: " + args.Exception, true, writeToFile: true);
                args.SetObserved();
            };

            // ScrollBar のトラック部分クリックで Thumb 中心を移動させる
            OsuMate.Utils.ScrollBarClickToPositionBehavior.Register();

            // ComboBoxが閉じている時にホイールで選択候補が切り替わる既定動作を無効化
            OsuMate.Utils.ComboBoxWheelBehavior.Register();

            var services = new ServiceCollection();
            ConfigureServices(services);
            ServiceProvider = services.BuildServiceProvider();

            // 設定で指定されたosu!（.exe/.lnk）の自動起動。ShellExecute（.lnk解決）を含むため
            // 数十〜数百ms程度かかることがあり、UIスレッドの初期表示をブロックしないよう
            // バックグラウンドで実行する。失敗時の例外はOsuLauncherService内で捕捉・ログ済みだが、
            // 万一の想定外例外でアプリ起動自体が失敗しないよう、ここでも念のため捕捉する。
            var settingsVm = ServiceProvider.GetRequiredService<SettingsViewModel>();
            var osuLauncher = ServiceProvider.GetRequiredService<OsuLauncherService>();
            Task.Run(() =>
            {
                try
                {
                    // 「自動起動を行うかどうか」の判定
                    if (settingsVm.AutoLaunchOsuEnabled)
                        osuLauncher.TryAutoLaunch(settingsVm.AutoLaunchOsuPath);
                }
                catch (Exception ex) { Utils.LogUtils.DebugLogger("Auto launch osu failed: " + ex.Message, true); }
            });

            var mainWindow = ServiceProvider.GetRequiredService<MainWindow>();
            Application.Current.MainWindow = mainWindow;
            mainWindow.Show();
        }

        private void ConfigureServices(IServiceCollection services)
        {
            // WPF Dispatcher をコンストラクタ注入できるよう登録
            services.AddSingleton<Dispatcher>(_ => Dispatcher.CurrentDispatcher);

            services.AddSingleton<OsuMemoryService>();
            services.AddSingleton<OsuProcessMonitorService>();
            services.AddSingleton<OsuLauncherService>();
            services.AddSingleton<PpCalculationService>();
            services.AddSingleton<SettingsViewModel>();

            // Light/Dark テーマの共有初期値。
            // ThemeViewModel（MainViewModel が保持する実体）はトグルのたびに新しい ThemeSettings を
            // 生成して差し替えるため、ここでDIに登録するのはあくまで「起動直後の既定テーマ」
            // （StrainGraph/URTimeGraph/URDistGraph を MainViewModel が new する際に渡す
            //   Theme.Current の初期値＝ThemeSettings.Dark() と同じ考え方）。
            // 実際のテーマ切替反映は、MainWindow が IThemeable.ApplyTheme(...) を明示的に
            // 呼び出すことで行う（PlayStatsChartViewModel も含め、DIコンテナから毎回新しい
            // ThemeSettings を取得し直す設計にはしていない）。
            services.AddSingleton<ThemeSettings>(_ => ThemeSettings.Dark());

            // PlayLog 系クラス（依存順に登録）
            services.AddSingleton<PlayLogRepository>();
            services.AddSingleton<BeatmapPathResolver>();
            services.AddSingleton<PlayLogSrPpEnricher>();
            services.AddSingleton<HistoricalImporter>();
            services.AddSingleton<PlayLogService>();

            // コントリビューショングラフ（PlayLogViewModel が依存するため、それより先に登録）。
            // ContributionGraphViewModel はヒートマップの色分け用に PlayLogAggregationService を、
            // セルツールチップのSR/pp/Acc表示用に PlayStatsAggregationService をそれぞれ使う。
            services.AddSingleton<PlayLogAggregationService>();
            services.AddSingleton<PlayStatsAggregationService>();
            services.AddSingleton<ContributionGraphViewModel>();

            // ContributionGraphの直下に表示する日次ヒット数推移グラフ。
            // ContributionGraphViewModel.DailyHits をそのまま受け取るだけで独自の集計サービスへは
            // 依存しないため、PlayLogAggregationServiceへの登録追加は不要（ThemeSettingsのみでOK）。
            services.AddSingleton<ContributionChartViewModel>();

            // 月次SR/pp/Accグラフ。ContributionGraphViewModel.DailyStats をそのまま受け取るだけで
            // 独自の集計サービスへは依存しないため、PlayStatsAggregationServiceへの登録追加は
            // 不要（ThemeSettingsのみでOK）。
            services.AddSingleton<PlayStatsChartViewModel>();

            services.AddSingleton<PlayLogViewModel>();
            services.AddSingleton<MainViewModel>();
            services.AddSingleton<WindowManagerService>();

            // Trainer 系クラス
            services.AddSingleton<BeatmapTrainerService>();
            services.AddSingleton<TrainerViewModel>();

            services.AddTransient<MainWindow>();
        }
    }

}
