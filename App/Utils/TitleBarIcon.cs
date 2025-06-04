using Microsoft.UI.Xaml;

namespace Coder.Desktop.App.Utils;

public static class TitleBarIcon
{
    public static void SetTitlebarIcon(Window window)
    {
        window.AppWindow.SetIcon("coder.ico");
    }
}
