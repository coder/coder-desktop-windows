using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;
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
            var mainInstance = GetMainInstance();
            var activationArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
            if (!mainInstance.IsCurrent)
            {
                mainInstance.RedirectActivationToAsync(activationArgs).AsTask().Wait();
                return;
            }

            // Register for URI handling (known as "protocol activation")
#if DEBUG
            const string scheme = "coder-debug";
#else
            const string scheme = "coder";
#endif
            var thisBin = Assembly.GetExecutingAssembly().Location;
            ActivationRegistrationManager.RegisterForProtocolActivation(scheme, thisBin + ",1", "Coder Desktop", "");

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

                // redirections via RedirectActivationToAsync above get routed to the App
                mainInstance.Activated += app.OnActivated;
                var notificationManager = AppNotificationManager.Default;
                notificationManager.NotificationInvoked += app.HandleNotification;
                notificationManager.Register();
                if (activationArgs.Kind != ExtendedActivationKind.Launch)
                    // this means we were activated without having already launched, so handle
                    // the activation as well.
                    app.OnActivated(null, activationArgs);
            });
        }
        catch (Exception e)
        {
            ShowExceptionAndCrash(e);
        }
    }

    private static AppInstance GetMainInstance()
    {
#if !DEBUG
        const string appInstanceName = "Coder.Desktop.App";
#else
        const string appInstanceName = "Coder.Desktop.App.Debug";
#endif

        return AppInstance.FindOrRegisterForKey(appInstanceName);
    }

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
