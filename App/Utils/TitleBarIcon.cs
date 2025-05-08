using System;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using WinRT.Interop;

namespace Coder.Desktop.App.Utils
{
    public static class TitleBarIcon
    {
        public static void SetTitlebarIcon(Window window)
        {
            var hwnd = WindowNative.GetWindowHandle(window);
            var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            AppWindow.GetFromWindowId(windowId).SetIcon("coder.ico");
        }
    }
}
