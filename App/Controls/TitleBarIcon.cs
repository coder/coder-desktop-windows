using System;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace Coder.Desktop.App.Controls
{
    public static class TitleBarIcon
    {
        public static void InjectIcon(Window window)
        {
            var hwnd = WindowNative.GetWindowHandle(window);
            var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            AppWindow.GetFromWindowId(windowId).SetIcon("coder.ico");
        }

        public static void SetTitlebarIcon(Window window)
        {
            var hwnd = WindowNative.GetWindowHandle(window);

            string iconPathDark = "Assets/coder_icon_32_dark.ico";
            string iconPathLight = "Assets/coder_icon_32_light.ico";

            var hIconDark = PInvoke.LoadImage(
                IntPtr.Zero,
                iconPathDark,
                PInvoke.IMAGE_ICON,
                0,
                0,
                PInvoke.LR_LOADFROMFILE
            );

            var hIconLight = PInvoke.LoadImage(
                IntPtr.Zero,
                iconPathLight,
                PInvoke.IMAGE_ICON,
                0,
                0,
                PInvoke.LR_LOADFROMFILE
            );

            PInvoke.SendMessage(hwnd, PInvoke.WM_SETICON, (IntPtr)PInvoke.ICON_SMALL, hIconDark);
            PInvoke.SendMessage(hwnd, PInvoke.WM_SETICON, (IntPtr)PInvoke.ICON_BIG, hIconLight);
        }
    }
}
