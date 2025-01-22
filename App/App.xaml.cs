using Microsoft.UI.Xaml;

namespace Coder.Desktop.App;

public partial class App : Application
{
    private TrayWindow? TrayWindow;

    public App()
    {
        InitializeComponent();
    }

    private bool HandleClosedEvents { get; } = true;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        TrayWindow = new TrayWindow();
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
