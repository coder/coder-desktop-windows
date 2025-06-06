using System.Diagnostics;
using System.Windows.Input;
using Windows.UI.ViewManagement;
using DependencyPropertyGenerator;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Coder.Desktop.App.Controls;

[DependencyProperty<ICommand>("OpenCommand")]
[DependencyProperty<ICommand>("ExitCommand")]
[DependencyProperty<ICommand>("CheckForUpdatesCommand")]
public sealed partial class TrayIcon : UserControl
{
    private readonly UISettings _uiSettings = new();

    public TrayIcon()
    {
        InitializeComponent();
        _uiSettings.ColorValuesChanged += OnColorValuesChanged;
        UpdateTrayIconBasedOnTheme();
    }

    private void OnColorValuesChanged(UISettings sender, object args)
    {
        DispatcherQueue.TryEnqueue(UpdateTrayIconBasedOnTheme);
    }

    private void UpdateTrayIconBasedOnTheme()
    {
        var currentTheme = Application.Current.RequestedTheme;
        Debug.WriteLine("Theme update requested, found theme: " + currentTheme);

        switch (currentTheme)
        {
            case ApplicationTheme.Dark:
                TaskbarIcon.IconSource = (BitmapImage)Resources["IconDarkTheme"];
                break;
            case ApplicationTheme.Light:
            default:
                TaskbarIcon.IconSource = (BitmapImage)Resources["IconLightTheme"];
                break;
        }
    }
}
