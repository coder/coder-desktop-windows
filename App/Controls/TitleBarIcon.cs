using System;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using WinRT.Interop;

namespace Coder.Desktop.App.Controls
{
    public static class TitleBarIcon
    {
        private static readonly Lazy<IconsManager> _iconsManager = new(() => new IconsManager());

        public static void SetTitlebarIcon(Window window)
        {
            var hwnd = WindowNative.GetWindowHandle(window);
            var theme = window.Content is FrameworkElement fe
             ? fe.ActualTheme
             : ElementTheme.Default;
            _iconsManager.Value.SetTitlebarIcon(hwnd, theme == ElementTheme.Dark);
        }

        public static void DisposeIconsManager()
        {
            _iconsManager.Value.Dispose();
        }
    }

#pragma warning disable CsWinRT1028 // Class does not need to be partial, it's an SDK bug: 
    public class IconsManager : IDisposable
#pragma warning restore CsWinRT1028 // Class is not marked partial
    {
        private nint hIconDark; 
        private nint hIconLight;
        private const string iconPathDark = "Assets/coder_icon_32_dark.ico";
        private const string iconPathLight = "Assets/coder_icon_32_light.ico";
        public IconsManager() {
            hIconDark = PInvoke.LoadImage(
                IntPtr.Zero,
                iconPathDark,
                PInvoke.IMAGE_ICON,
                0,
                0,
                PInvoke.LR_LOADFROMFILE
            );
            hIconLight = PInvoke.LoadImage(
                IntPtr.Zero,
                iconPathLight,
                PInvoke.IMAGE_ICON,
                0,
                0,
                PInvoke.LR_LOADFROMFILE
            );
        }

        public void SetTitlebarIcon(nint windowHandle, bool isDarkTheme)
        {
            PInvoke.SendMessage(windowHandle, PInvoke.WM_SETICON, (IntPtr)PInvoke.ICON_SMALL, isDarkTheme ? hIconDark : hIconLight);
            PInvoke.SendMessage(windowHandle, PInvoke.WM_SETICON, (IntPtr)PInvoke.ICON_BIG, isDarkTheme ? hIconLight : hIconDark);
        }

        public void Dispose()
        {
            if (hIconDark != IntPtr.Zero)
            {
                PInvoke.DestroyIcon(hIconDark);
            }
            if (hIconLight != IntPtr.Zero)
            {
                PInvoke.DestroyIcon(hIconLight);
            }
            GC.SuppressFinalize(this);
        }
    }
}
