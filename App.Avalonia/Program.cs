using Avalonia;
using Coder.Desktop.App.Diagnostics;

namespace Coder.Desktop.App;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
        {
            AppBootstrapLogger.Error("Unhandled exception", eventArgs.ExceptionObject as Exception);
        };

        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        {
            AppBootstrapLogger.Error("Unobserved task exception", eventArgs.Exception);
            eventArgs.SetObserved();
        };

        AppBootstrapLogger.Info($"Starting Coder Desktop (Avalonia) with args: {string.Join(" ", args)}");
        AppBootstrapLogger.Info($"Logs: {AppBootstrapLogger.LogFilePath}");

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            AppBootstrapLogger.Info("Coder Desktop exited normally");
            return 0;
        }
        catch (Exception ex)
        {
            AppBootstrapLogger.Error("Coder Desktop exited with fatal error", ex);
            return 1;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
