using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using OsuMate.Models;
using OsuMate.Services;
using OsuMate.Services.PlayLog;
using OsuMate.Services.Trainer;
using OsuMate.ViewModels;
using OsuMate.Views;

namespace OsuMate
{
  public partial class App : Application
  {
    public static IServiceProvider ServiceProvider { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
      base.OnStartup(e);

      DispatcherUnhandledException += (s, args) =>
      {
        Utils.LogUtils.DebugLogger(
          "Unhandled UI exception: " + args.Exception,
          true,
          writeToFile: true
        );
        args.Handled = true;
      };

      AppDomain.CurrentDomain.UnhandledException += (s, args) =>
      {
        Utils.LogUtils.DebugLogger(
          "Unhandled non-UI exception: " + args.ExceptionObject,
          true,
          writeToFile: true
        );
      };

      TaskScheduler.UnobservedTaskException += (s, args) =>
      {
        Utils.LogUtils.DebugLogger(
          "Unobserved task exception: " + args.Exception,
          true,
          writeToFile: true
        );
        args.SetObserved();
      };

      OsuMate.Utils.ScrollBarClickToPositionBehavior.Register();

      OsuMate.Utils.ComboBoxWheelBehavior.Register();

      var services = new ServiceCollection();
      ConfigureServices(services);
      ServiceProvider = services.BuildServiceProvider();

      var settingsVm = ServiceProvider.GetRequiredService<SettingsViewModel>();
      var osuLauncher = ServiceProvider.GetRequiredService<OsuLauncherService>();
      Task.Run(() =>
      {
        try
        {
          if (settingsVm.AutoLaunchOsuEnabled)
            osuLauncher.TryAutoLaunch(settingsVm.AutoLaunchOsuPath);
        }
        catch (Exception ex)
        {
          Utils.LogUtils.DebugLogger("Auto launch osu failed: " + ex.Message, true);
        }
      });

      ShutdownMode = ShutdownMode.OnExplicitShutdown;

      ServiceProvider.GetRequiredService<WindowManagerService>();

      var uiThread = new Thread(() =>
      {
        var mainWindow = ServiceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
        Dispatcher.Run();
      });
      uiThread.SetApartmentState(ApartmentState.STA);
      uiThread.IsBackground = false;
      uiThread.Start();
    }

    private void ConfigureServices(IServiceCollection services)
    {
      services.AddSingleton<Dispatcher>(_ => Dispatcher.CurrentDispatcher);

      services.AddSingleton<OsuMemoryService>();
      services.AddSingleton<RawInputService>();
      services.AddSingleton<OsuProcessMonitorService>();
      services.AddSingleton<OsuLauncherService>();
      services.AddSingleton<PpCalculationService>();
      services.AddSingleton<SettingsViewModel>();

      services.AddSingleton<ThemeSettings>(_ => ThemeSettings.Dark());

      services.AddSingleton<PlayLogRepository>();
      services.AddSingleton<BeatmapPathResolver>();
      services.AddSingleton<PlayLogSrPpEnricher>();
      services.AddSingleton<HistoricalImporter>();
      services.AddSingleton<PlayLogService>();

      services.AddSingleton<PlayLogAggregationService>();
      services.AddSingleton<PlayStatsAggregationService>();
      services.AddSingleton<ActivityGridViewModel>();

      services.AddSingleton<ActivityChartViewModel>();

      services.AddSingleton<PlayStatsChartViewModel>();

      services.AddSingleton<PlayLogViewModel>();
      services.AddSingleton<MainViewModel>();
      services.AddSingleton<WindowManagerService>();

      services.AddSingleton<BeatmapTrainerService>();
      services.AddSingleton<TrainerViewModel>();

      services.AddTransient<MainWindow>();
    }
  }
}
