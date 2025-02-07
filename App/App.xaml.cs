using Coder.Desktop.App.ViewModels;
using Microsoft.UI.Xaml;

namespace Coder.Desktop.App;

public partial class App : Application
{
    private TrayWindow? TrayWindow;
    public SignInViewModel SignInViewModel { get; }

    public App()
    {
        SignInViewModel = new SignInViewModel();
        InitializeComponent();
    }

    private bool HandleClosedEvents { get; } = true;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        TrayWindow = new TrayWindow(SignInViewModel);
        TrayWindow.Closed += (sender, args) =>
        {
            // TODO: wire up HandleClosedEvents properly
            if (HandleClosedEvents)
            {
                args.Handled = true;
                TrayWindow.AppWindow.Hide();
            }
        };
    }
}
