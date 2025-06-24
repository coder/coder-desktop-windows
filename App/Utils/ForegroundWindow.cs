using System;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace Coder.Desktop.App.Utils;

public static class ForegroundWindow
{

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    public static void MakeForeground(Window window)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        _ = SetForegroundWindow(hwnd);
        // Not a big deal if it fails.
    }
}
