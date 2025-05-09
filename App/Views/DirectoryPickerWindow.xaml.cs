using System;
using System.Runtime.InteropServices;
using Windows.Graphics;
using Coder.Desktop.App.Utils;
using Coder.Desktop.App.ViewModels;
using Coder.Desktop.App.Views.Pages;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using WinRT.Interop;
using WinUIEx;

namespace Coder.Desktop.App.Views;

public sealed partial class DirectoryPickerWindow : WindowEx
{
    public DirectoryPickerWindow(DirectoryPickerViewModel viewModel)
    {
        InitializeComponent();
        TitleBarIcon.SetTitlebarIcon(this);

        SystemBackdrop = new DesktopAcrylicBackdrop();

        viewModel.Initialize(this, DispatcherQueue);
        RootFrame.Content = new DirectoryPickerMainPage(viewModel);

        // This will be moved to the center of the parent window in SetParent.
        this.CenterOnScreen();
    }

    public void SetParent(Window parentWindow)
    {
        // Move the window to the center of the parent window.
        var scale = DisplayScale.WindowScale(parentWindow);
        var windowPos = new PointInt32(
            parentWindow.AppWindow.Position.X + parentWindow.AppWindow.Size.Width / 2 - AppWindow.Size.Width / 2,
            parentWindow.AppWindow.Position.Y + parentWindow.AppWindow.Size.Height / 2 - AppWindow.Size.Height / 2
        );

        // Ensure we stay within the display.
        var workArea = DisplayArea.GetFromPoint(parentWindow.AppWindow.Position, DisplayAreaFallback.Primary).WorkArea;
        if (windowPos.X + AppWindow.Size.Width > workArea.X + workArea.Width) // right edge
            windowPos.X = workArea.X + workArea.Width - AppWindow.Size.Width;
        if (windowPos.Y + AppWindow.Size.Height > workArea.Y + workArea.Height) // bottom edge
            windowPos.Y = workArea.Y + workArea.Height - AppWindow.Size.Height;
        if (windowPos.X < workArea.X) // left edge
            windowPos.X = workArea.X;
        if (windowPos.Y < workArea.Y) // top edge
            windowPos.Y = workArea.Y;

        AppWindow.Move(windowPos);

        var parentHandle = WindowNative.GetWindowHandle(parentWindow);
        var thisHandle = WindowNative.GetWindowHandle(this);

        // Set the parent window in win API.
        NativeApi.SetWindowParent(thisHandle, parentHandle);

        // Override the presenter, which allows us to enable modal-like
        // behavior for this window:
        // - Disables the parent window
        // - Any activations of the parent window will play a bell sound and
        //   focus the modal window
        //
        // This behavior is very similar to the native file/directory picker on
        // Windows.
        var presenter = OverlappedPresenter.CreateForDialog();
        presenter.IsModal = true;
        AppWindow.SetPresenter(presenter);
        AppWindow.Show();

        // Cascade close events.
        parentWindow.Closed += OnParentWindowClosed;
        Closed += (_, _) =>
        {
            parentWindow.Closed -= OnParentWindowClosed;
            parentWindow.Activate();
        };
    }

    private void OnParentWindowClosed(object? sender, WindowEventArgs e)
    {
        Close();
    }

    private static class NativeApi
    {
        [DllImport("user32.dll")]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        public static void SetWindowParent(IntPtr window, IntPtr parent)
        {
            SetWindowLongPtr(window, -8, parent);
        }
    }
}
