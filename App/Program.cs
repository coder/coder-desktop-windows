using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using WinRT;

namespace Coder.Desktop.App;

#if DISABLE_XAML_GENERATED_MAIN
public static class Program
{
    private static App? app;
#if DEBUG
    [DllImport("kernel32.dll")]
    private static extern bool AllocConsole();
#endif

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, int type);

    [STAThread]
    private static void Main(string[] args)
    {
        try
        {
            ComWrappersSupport.InitializeComWrappers();
            if (!CheckSingleInstance()) return;
            Application.Start(p =>
            {
                var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
                SynchronizationContext.SetSynchronizationContext(context);

                app = new App();
                app.UnhandledException += (_, e) =>
                {
                    e.Handled = true;
                    ShowExceptionAndCrash(e.Exception);
                };
            });
        }
        catch (Exception e)
        {
            ShowExceptionAndCrash(e);
        }
    }

    [STAThread]
    private static bool CheckSingleInstance()
    {
#if !DEBUG
        const string appInstanceName = "Coder.Desktop.App";
#else
        const string appInstanceName = "Coder.Desktop.App.Debug";
#endif

        var instance = AppInstance.FindOrRegisterForKey(appInstanceName);
        return instance.IsCurrent;
    }

    [STAThread]
    private static void ShowExceptionAndCrash(Exception e)
    {
        const string title = "Coder Desktop Fatal Error";
        var message =
            "Coder Desktop has encountered a fatal error and must exit.\n\n" +
            e + "\n\n" +
            Environment.StackTrace;
        MessageBoxW(IntPtr.Zero, message, title, 0);

        if (app != null)
            app.Exit();

        // This will log the exception to the Windows Event Log.
#if DEBUG
        // And, if in DEBUG mode, it will also log to the console window.
        AllocConsole();
#endif
        Environment.FailFast("Coder Desktop has encountered a fatal error and must exit.", e);
    }
}
#endif
