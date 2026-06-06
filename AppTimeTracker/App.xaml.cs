using AppTimeTracker.Services;

namespace AppTimeTracker;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceMutexName = @"Global\time-controller-single-instance";
    private Mutex? singleInstanceMutex;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            singleInstanceMutex.Dispose();
            singleInstanceMutex = null;
            Shutdown();
            return;
        }

        AppPaths.EnsureDirectories();
        RegisterExceptionHandlers();
        LogService.Info("App started " + AppPaths.Version);
        base.OnStartup(e);
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        LogService.Info("App exited with code " + e.ApplicationExitCode);
        singleInstanceMutex?.ReleaseMutex();
        singleInstanceMutex?.Dispose();
        singleInstanceMutex = null;
        base.OnExit(e);
    }

    private static void RegisterExceptionHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                LogService.Exception("Unhandled AppDomain exception", exception);
            }
            else
            {
                LogService.Error("Unhandled AppDomain exception: " + args.ExceptionObject);
            }
        };

        Current.DispatcherUnhandledException += (_, args) =>
        {
            LogService.Exception("Unhandled UI exception", args.Exception);
            System.Windows.MessageBox.Show(
                "time-controller 遇到界面异常，错误已经写入日志。如果程序状态异常，请重启应用。",
                "time-controller 错误",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            args.Handled = true;
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogService.Exception("Unobserved task exception", args.Exception);
            args.SetObserved();
        };
    }
}
