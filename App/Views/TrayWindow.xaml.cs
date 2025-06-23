using Coder.Desktop.App.Controls;
using Coder.Desktop.App.Models;
using Coder.Desktop.App.Services;
using Coder.Desktop.App.Utils;
using Coder.Desktop.App.Views.Pages;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics;
using Windows.System;
using Windows.UI.Core;
using WinRT.Interop;
using WindowActivatedEventArgs = Microsoft.UI.Xaml.WindowActivatedEventArgs;

namespace Coder.Desktop.App.Views;

public sealed partial class TrayWindow : Window, INotificationHandler
{
    private const int WIDTH = 300;

    private readonly AppWindow _aw;

    public double ProxyHeight { get; private set; }

    // This is used to know the "start point of the animation"
    private int _lastWindowHeight;
    private Storyboard? _currentSb;

    private VpnLifecycle prevVpnLifecycle = VpnLifecycle.Stopped;
    private RpcLifecycle prevRpcLifecycle = RpcLifecycle.Disconnected;

    private NativeApi.POINT? _lastActivatePosition;

    private readonly IRpcController _rpcController;
    private readonly ICredentialManager _credentialManager;
    private readonly ISyncSessionController _syncSessionController;
    private readonly IUpdateController _updateController;
    private readonly IUserNotifier _userNotifier;
    private readonly TrayWindowLoadingPage _loadingPage;
    private readonly TrayWindowDisconnectedPage _disconnectedPage;
    private readonly TrayWindowLoginRequiredPage _loginRequiredPage;
    private readonly TrayWindowMainPage _mainPage;

    public TrayWindow(
        IRpcController rpcController, ICredentialManager credentialManager,
        ISyncSessionController syncSessionController, IUpdateController updateController,
        IUserNotifier userNotifier,
        TrayWindowLoadingPage loadingPage,
        TrayWindowDisconnectedPage disconnectedPage, TrayWindowLoginRequiredPage loginRequiredPage,
        TrayWindowMainPage mainPage)
    {
        _rpcController = rpcController;
        _credentialManager = credentialManager;
        _syncSessionController = syncSessionController;
        _updateController = updateController;
        _userNotifier = userNotifier;
        _loadingPage = loadingPage;
        _disconnectedPage = disconnectedPage;
        _loginRequiredPage = loginRequiredPage;
        _mainPage = mainPage;

        InitializeComponent();
        _userNotifier.RegisterHandler("TrayWindow", this);
        AppWindow.Hide();
        SystemBackdrop = new DesktopAcrylicBackdrop();
        Activated += Window_Activated;
        RootFrame.SizeChanged += RootFrame_SizeChanged;

        _rpcController.StateChanged += RpcController_StateChanged;
        _credentialManager.CredentialsChanged += CredentialManager_CredentialsChanged;
        _syncSessionController.StateChanged += SyncSessionController_StateChanged;
        SetPageByState(_rpcController.GetState(), _credentialManager.GetCachedCredentials(),
            _syncSessionController.GetState());

        // Setting these directly in the .xaml doesn't seem to work for whatever reason.
        TrayIcon.OpenCommand = Tray_OpenCommand;
        TrayIcon.CheckForUpdatesCommand = Tray_CheckForUpdatesCommand;
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

        _aw = AppWindow.GetFromWindowId(
                 Win32Interop.GetWindowIdFromWindow(
                 WindowNative.GetWindowHandle(this)));
        SizeProxy.SizeChanged += (_, e) =>
        {
            if (_currentSb is null) return;            // nothing running

            int newHeight = (int)Math.Round(
                                e.NewSize.Height * DisplayScale.WindowScale(this));

            int delta = newHeight - _lastWindowHeight;
            if (delta == 0) return;

            var pos = _aw.Position;
            var size = _aw.Size;

            pos.Y -= delta;                    // grow upward
            size.Height = newHeight;

            _aw.MoveAndResize(
                new RectInt32(pos.X, pos.Y, size.Width, size.Height));

            _lastWindowHeight = newHeight;
        };
    }

    private void SetPageByState(RpcModel rpcModel, CredentialModel credentialModel,
        SyncSessionControllerStateModel syncSessionModel)
    {
        if (credentialModel.State == CredentialState.Unknown)
        {
            SetRootFrame(_loadingPage);
            return;
        }

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

    private void NotifyUser(RpcModel rpcModel)
    {
        // This method is called when the state changes, but we don't want to notify
        // the user if the state hasn't changed.
        var isRpcLifecycleChanged = rpcModel.RpcLifecycle != RpcLifecycle.Connecting && prevRpcLifecycle != rpcModel.RpcLifecycle;
        var isVpnLifecycleChanged = (rpcModel.VpnLifecycle == VpnLifecycle.Started || rpcModel.VpnLifecycle == VpnLifecycle.Stopped) && prevVpnLifecycle != rpcModel.VpnLifecycle;

        if (!isRpcLifecycleChanged && !isVpnLifecycleChanged)
        {
            return;
        }
        var message = string.Empty;
        // Compose the message based on the lifecycle changes
        if (isRpcLifecycleChanged)
            message += rpcModel.RpcLifecycle switch
            {
                RpcLifecycle.Connected => "Connected to Coder vpn service.",
                RpcLifecycle.Disconnected => "Disconnected from Coder vpn service.",
                _ => "" // This will never be hit.
            };

        if(message.Length > 0 && isVpnLifecycleChanged)
            message += " ";

        if (isVpnLifecycleChanged)
            message += rpcModel.VpnLifecycle switch
            {
                VpnLifecycle.Started => "Coder Connect started.",
                VpnLifecycle.Stopped => "Coder Connect stopped.",
                _ => "" // This will never be hit.
            };

        // Save state for the next notification check
        prevRpcLifecycle = rpcModel.RpcLifecycle;
        prevVpnLifecycle = rpcModel.VpnLifecycle;

        if (_aw.IsVisible)
        {
            return; // No need to notify if the window is not visible.
        }

        // Trigger notification
        _userNotifier.ShowActionNotification(message, string.Empty, nameof(TrayWindow), null, CancellationToken.None);
    }

    private void RpcController_StateChanged(object? _, RpcModel model)
    {
        SetPageByState(model, _credentialManager.GetCachedCredentials(), _syncSessionController.GetState());
        NotifyUser(model);
    }

    private void CredentialManager_CredentialsChanged(object? _, CredentialModel model)
    {
        SetPageByState(_rpcController.GetState(), model, _syncSessionController.GetState());
    }

    private void SyncSessionController_StateChanged(object? _, SyncSessionControllerStateModel model)
    {
        SetPageByState(_rpcController.GetState(), _credentialManager.GetCachedCredentials(), model);
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

        RootFrame.SetPage(page);
    }

    private void RootFrame_SizeChanged(object sender, SizedFrameEventArgs e)
    {
        AnimateWindowHeight(e.NewSize.Height);
    }

    // We need to animate the height change in code-behind, because XAML
    // storyboard animation timeline is immutable - it cannot be changed
    // mid-run to accomodate a new height.
    private void AnimateWindowHeight(double targetHeight)
    {
        // If another animation is already running we need to fast forward it.
        if (_currentSb is { } oldSb)
        {
            oldSb.Completed -= OnStoryboardCompleted;
            // We need to use SkipToFill, because Stop actually sets Height to 0, which
            // makes the window go haywire.
            oldSb.SkipToFill();
        }

        _lastWindowHeight = AppWindow.Size.Height;

        var anim = new DoubleAnimation
        {
            To = targetHeight,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            EnableDependentAnimation = true
        };

        Storyboard.SetTarget(anim, SizeProxy);
        Storyboard.SetTargetProperty(anim, "Height");

        var sb = new Storyboard { Children = { anim } };
        sb.Completed += OnStoryboardCompleted;
        sb.Begin();

        _currentSb = sb;
    }

    private void OnStoryboardCompleted(object? sender, object e)
    {
        // We need to remove the event handler after the storyboard completes,
        // to avoid memory leaks and multiple calls.
        if (sender is Storyboard sb)
            sb.Completed -= OnStoryboardCompleted;

        // SizeChanged handler will stop forwarding resize ticks
        // until we start the next storyboard.
        _currentSb = null;
    }

    private void MoveResizeAndActivate()
    {
        SaveCursorPos();
        var size = CalculateWindowSize(RootFrame.GetContentSize().Height);
        var pos = CalculateWindowPosition(size);
        var rect = new RectInt32(pos.X, pos.Y, size.Width, size.Height);
        AppWindow.MoveAndResize(rect);
        AppWindow.Show();
        ForegroundWindow.MakeForeground(this);
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

    private SizeInt32 CalculateWindowSize(double height)
    {
        if (height <= 0) height = 100; // will be resolved next frame typically

        var scale = DisplayScale.WindowScale(this);
        var newWidth = (int)(WIDTH * scale);
        var newHeight = (int)(height * scale);

        return new SizeInt32(newWidth, newHeight);
    }

    private PointInt32 CalculateWindowPosition(SizeInt32 size)
    {
        var width = size.Width;
        var height = size.Height;

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
    private async Task Tray_CheckForUpdates()
    {
        // Handles errors itself for the most part.
        await _updateController.CheckForUpdatesNow();
    }

    [RelayCommand]
    private void Tray_Exit()
    {
        // It's fine that this happens in the background.
        _ = ((App)Application.Current).ExitApplication();
    }

    public void HandleNotificationActivation(IDictionary<string, string> args)
    {
        Tray_Open();
    }

    public static class NativeApi
    {
        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT lpPoint);

        public struct POINT
        {
            public int X;
            public int Y;
        }
    }
}
