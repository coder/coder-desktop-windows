using System;
using System.Runtime.InteropServices;
using System.Threading;
using Windows.Foundation;
using Windows.Graphics;
using Windows.System;
using Windows.UI.Core;
using Coder.Desktop.App.Models;
using Coder.Desktop.App.Services;
using Coder.Desktop.App.Views.Pages;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WinRT.Interop;
using WindowActivatedEventArgs = Microsoft.UI.Xaml.WindowActivatedEventArgs;

namespace Coder.Desktop.App.Views;

public sealed partial class TrayWindow : Window
{
    private const int WIDTH = 300;

    private NativeApi.POINT? _lastActivatePosition;

    private readonly IRpcController _rpcController;
    private readonly ICredentialManager _credentialManager;
    private readonly TrayWindowDisconnectedPage _disconnectedPage;
    private readonly TrayWindowLoginRequiredPage _loginRequiredPage;
    private readonly TrayWindowMainPage _mainPage;

    public TrayWindow(IRpcController rpcController, ICredentialManager credentialManager,
        TrayWindowDisconnectedPage disconnectedPage, TrayWindowLoginRequiredPage loginRequiredPage,
        TrayWindowMainPage mainPage)
    {
        _rpcController = rpcController;
        _credentialManager = credentialManager;
        _disconnectedPage = disconnectedPage;
        _loginRequiredPage = loginRequiredPage;
        _mainPage = mainPage;

        InitializeComponent();
        AppWindow.Hide();
        SystemBackdrop = new DesktopAcrylicBackdrop();
        Activated += Window_Activated;

        rpcController.StateChanged += RpcController_StateChanged;
        credentialManager.CredentialsChanged += CredentialManager_CredentialsChanged;
        SetPageByState(rpcController.GetState(), credentialManager.GetCredentials());

        _rpcController.Reconnect(CancellationToken.None);

        // Setting OpenCommand and ExitCommand directly in the .xaml doesn't seem to work for whatever reason.
        TrayIcon.OpenCommand = Tray_OpenCommand;
        TrayIcon.ExitCommand = Tray_ExitCommand;

        // Hide the title bar and buttons. WinUi 3 provides a method to do this with
        // `ExtendsContentIntoTitleBar = true;`, but it automatically adds emulated title bar buttons that cannot be
        // removed.
        if (AppWindow.Presenter is not OverlappedPresenter presenter)
            throw new Exception("Failed to get OverlappedPresenter for window");
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsResizable = false;
        presenter.IsAlwaysOnTop = true;
        presenter.SetBorderAndTitleBar(true, false);
        AppWindow.IsShownInSwitchers = false;

        // Ensure the corner is rounded.
        var windowHandle = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        var value = 2;
        // Best effort. This does not work on Windows 10.
        _ = NativeApi.DwmSetWindowAttribute(windowHandle, 33, ref value, Marshal.SizeOf<int>());
    }

    private void SetPageByState(RpcModel rpcModel, CredentialModel credentialModel)
    {
        switch (rpcModel.RpcLifecycle)
        {
            case RpcLifecycle.Connected:
                if (credentialModel.State == CredentialState.Valid)
                    SetRootFrame(_mainPage);
                else
                    SetRootFrame(_loginRequiredPage);
                break;
            case RpcLifecycle.Disconnected:
            case RpcLifecycle.Connecting:
            default:
                SetRootFrame(_disconnectedPage);
                break;
        }
    }

    private void RpcController_StateChanged(object? _, RpcModel model)
    {
        SetPageByState(model, _credentialManager.GetCredentials());
    }

    private void CredentialManager_CredentialsChanged(object? _, CredentialModel model)
    {
        SetPageByState(_rpcController.GetState(), model);
    }

    // Sadly this is necessary because Window.Content.SizeChanged doesn't
    // trigger when the Page's content changes.
    public void SetRootFrame(Page page)
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => SetRootFrame(page));
            return;
        }

        if (ReferenceEquals(page, RootFrame.Content)) return;

        if (page.Content is not FrameworkElement newElement)
            throw new Exception("Failed to get Page.Content as FrameworkElement on RootFrame navigation");
        newElement.SizeChanged += Content_SizeChanged;

        // Unset the previous event listener.
        if (RootFrame.Content is Page { Content: FrameworkElement oldElement })
            oldElement.SizeChanged -= Content_SizeChanged;

        // Swap them out and reconfigure the window.
        // We don't use RootFrame.Navigate here because it doesn't let you
        // instantiate the page yourself. We also don't need forwards/backwards
        // capabilities.
        RootFrame.Content = page;
        ResizeWindow();
        MoveWindow();
    }

    private void Content_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ResizeWindow();
        MoveWindow();
    }

    private void ResizeWindow()
    {
        if (RootFrame.Content is not Page { Content: FrameworkElement frameworkElement })
            throw new Exception("Failed to get Content as FrameworkElement for window");

        // Measure the desired size of the content
        frameworkElement.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        // Adjust the AppWindow size
        var scale = GetDisplayScale();
        var height = (int)(frameworkElement.ActualHeight * scale);
        var width = (int)(WIDTH * scale);
        AppWindow.Resize(new SizeInt32(width, height));
    }

    private double GetDisplayScale()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var dpi = NativeApi.GetDpiForWindow(hwnd);
        if (dpi == 0) return 1; // assume scale of 1
        return dpi / 96.0; // 96 DPI == 1
    }

    public void MoveResizeAndActivate()
    {
        SaveCursorPos();
        ResizeWindow();
        MoveWindow();
        AppWindow.Show();
        NativeApi.SetForegroundWindow(WindowNative.GetWindowHandle(this));
    }

    private void SaveCursorPos()
    {
        var res = NativeApi.GetCursorPos(out var cursorPosition);
        if (res)
            _lastActivatePosition = cursorPosition;
        else
            // When the cursor position is null, we will spawn the window in
            // the bottom right corner of the primary display.
            // TODO: log(?) an error when this happens
            _lastActivatePosition = null;
    }

    private void MoveWindow()
    {
        AppWindow.Move(GetWindowPosition());
    }

    private PointInt32 GetWindowPosition()
    {
        var height = AppWindow.Size.Height;
        var width = AppWindow.Size.Width;
        var cursorPosition = _lastActivatePosition;
        if (cursorPosition is null)
        {
            var primaryWorkArea = DisplayArea.Primary.WorkArea;
            return new PointInt32(
                primaryWorkArea.Width - width,
                primaryWorkArea.Height - height
            );
        }

        // Spawn the window to the top right of the cursor.
        var x = cursorPosition.Value.X + 10;
        var y = cursorPosition.Value.Y - 10 - height;

        var workArea = DisplayArea.GetFromPoint(
            new PointInt32(cursorPosition.Value.X, cursorPosition.Value.Y),
            DisplayAreaFallback.Primary
        ).WorkArea;

        // Adjust if the window goes off the right edge of the display.
        if (x + width > workArea.X + workArea.Width) x = workArea.X + workArea.Width - width;

        // Adjust if the window goes off the bottom edge of the display.
        if (y + height > workArea.Y + workArea.Height) y = workArea.Y + workArea.Height - height;

        // Adjust if the window goes off the left edge of the display (somehow).
        if (x < workArea.X) x = workArea.X;

        // Adjust if the window goes off the top edge of the display (somehow).
        if (y < workArea.Y) y = workArea.Y;

        return new PointInt32(x, y);
    }

    private void Window_Activated(object sender, WindowActivatedEventArgs e)
    {
        // Close the window as soon as it loses focus.
        if (e.WindowActivationState == WindowActivationState.Deactivated
#if DEBUG
            // In DEBUG, holding SHIFT is required to have the window close when it loses focus.
            && InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down)
#endif
           )
            AppWindow.Hide();
    }

    [RelayCommand]
    private void Tray_Open()
    {
        MoveResizeAndActivate();
    }

    [RelayCommand]
    private void Tray_Exit()
    {
        Application.Current.Exit();
    }

    public class NativeApi
    {
        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern int GetDpiForWindow(IntPtr hwnd);

        public struct POINT
        {
            public int X;
            public int Y;
        }
    }
}
