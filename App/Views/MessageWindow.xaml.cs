using System.Diagnostics;
using Windows.Foundation;
using Windows.Graphics;
using Coder.Desktop.App.Utils;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using WinUIEx;

namespace Coder.Desktop.App.Views;

public sealed partial class MessageWindow : WindowEx
{
    public string MessageTitle;
    public string MessageContent;

    public MessageWindow(string title, string content, string windowTitle = "Coder Desktop")
    {
        Title = windowTitle;
        MessageTitle = title;
        MessageContent = content;

        InitializeComponent();
        TitleBarIcon.SetTitlebarIcon(this);
        SystemBackdrop = new DesktopAcrylicBackdrop();
        this.CenterOnScreen();
        AppWindow.Show();

        // TODO: the window should resize to fit content and not be resizable
        //       by the user, probably possible with SizedFrame and a Page, but
        //       I didn't want to add a Page for this
    }

    public void CloseClicked(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
