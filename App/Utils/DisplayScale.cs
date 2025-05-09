using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace Coder.Desktop.App.Utils;

/// <summary>
///     A static utility class to house methods related to the visual scale of the display monitor.
/// </summary>
public static class DisplayScale
{
    public static double WindowScale(Window win)
    {
        var hwnd = WindowNative.GetWindowHandle(win);
        var dpi = NativeApi.GetDpiForWindow(hwnd);
        if (dpi == 0) return 1; // assume scale of 1
        return dpi / 96.0; // 96 DPI == 1
    }

    public class NativeApi
    {
        [DllImport("user32.dll")]
        public static extern int GetDpiForWindow(IntPtr hwnd);
    }
}
