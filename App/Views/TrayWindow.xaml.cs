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
using Microsoft.UI.Xaml.Documents;
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

public sealed partial class TrayWindow : Window
{
    private const int WIDTH = 300;

    private readonly AppWindow _aw;

    public double ProxyHeight { get; private set; }

    // This is used to know the "start point of the animation"
    private int _lastWindowHeight;
    private Storyboard? _currentSb;

    private VpnLifecycle curVpnLifecycle = VpnLifecycle.Stopped;
    private RpcLifecycle curRpcLifecycle = RpcLifecycle.Disconnected;

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
        AppWindow.Hide();
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
            if (_currentSb is null) return; // nothing running

            var newHeight = (int)Math.Round(
                                e.NewSize.Height * DisplayScale.WindowScale(this));

            var delta = newHeight - _lastWindowHeight;
            if (delta == 0) return;

            var pos = _aw.Position;
            var size = _aw.Size;

            pos.Y -= delta; // grow upward
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

    /// <summary>
    /// This method is called when the state changes, but we don't want to notify
    /// the user if the state hasn't changed.
    /// </summary>
    /// <param name="rpcModel"></param>
    private void MaybeNotifyUser(RpcModel rpcModel)
    {
        var isRpcLifecycleChanged = rpcModel.RpcLifecycle == RpcLifecycle.Disconnected && curRpcLifecycle != rpcModel.RpcLifecycle;
        var isVpnLifecycleChanged = (rpcModel.VpnLifecycle == VpnLifecycle.Started || rpcModel.VpnLifecycle == VpnLifecycle.Stopped) && curVpnLifecycle != rpcModel.VpnLifecycle;

        if (!isRpcLifecycleChanged && !isVpnLifecycleChanged)
        {
            return;
        }

        var oldRpcLifeycle = curRpcLifecycle;
        var oldVpnLifecycle = curVpnLifecycle;
        curRpcLifecycle = rpcModel.RpcLifecycle;
        curVpnLifecycle = rpcModel.VpnLifecycle;

        var messages = new List<string>();

        if (oldRpcLifeycle != RpcLifecycle.Disconnected && curRpcLifecycle == RpcLifecycle.Disconnected)
        {
            messages.Add("Disconnected from Coder background service.");
        }

        if (oldVpnLifecycle != curVpnLifecycle)
        {
            switch (curVpnLifecycle)
            {
                case VpnLifecycle.Started:
                    messages.Add("Coder Connect started.");
                    break;
                case VpnLifecycle.Stopped:
                    messages.Add("Coder Connect stopped.");
                    break;
            }
        }

        if (messages.Count == 0) return;
        if (_aw.IsVisible) return;

        var message = string.Join(" ", messages);
        _userNotifier.ShowActionNotification(message, string.Empty, null, null, CancellationToken.None);
    }

    private void RpcController_StateChanged(object? _, RpcModel model)
    {
        SetPageByState(model, _credentialManager.GetCachedCredentials(), _syncSessionController.GetState());
        MaybeNotifyUser(model);
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
        var size = CalculateWindowSize(RootFrame.GetContentSize().Height);
        var pos = CalculateWindowPosition(size);
        var rect = new RectInt32(pos.X, pos.Y, size.Width, size.Height);
        AppWindow.MoveAndResize(rect);
        AppWindow.Show();
        ForegroundWindow.MakeForeground(this);
    }

    private SizeInt32 CalculateWindowSize(double height)
    {
        if (height <= 0) height = 100; // will be resolved next frame typically

        var scale = DisplayScale.WindowScale(this);
        var newWidth = (int)(WIDTH * scale);
        var newHeight = (int)(height * scale);

        return new SizeInt32(newWidth, newHeight);
    }

    private PointInt32 CalculateWindowPosition(SizeInt32 panelSize)
    {
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        // whole monitor
        var bounds = area.OuterBounds;
        // monitor minus taskbar
        var workArea = area.WorkArea;

        // get taskbar details - position, gap (size), auto-hide
        var tb = GetTaskbarInfo(area);

        // safe edges where tray window can touch the screen 
        var safeRight = workArea.X + workArea.Width;
        var safeBottom = workArea.Y + workArea.Height;

        // if the taskbar is auto-hidden at the bottom, stay clear of its reveal band
        if (tb.Position == TaskbarPosition.Bottom && tb.AutoHide)
            safeBottom -= tb.Gap;                       // shift everything up by its thickness

        // pick corner & position the panel
        int x, y;
        switch (tb.Position)
        {
            case TaskbarPosition.Left: // for Left we will stick to the left-bottom corner
                x = bounds.X + tb.Gap; // just right of the bar
                y = safeBottom - panelSize.Height;
                break;

            case TaskbarPosition.Top: // for Top we will stick to the top-right corner
                x = safeRight - panelSize.Width;
                y = bounds.Y + tb.Gap; // just below the bar
                break;

            default: // Bottom or Right bar we will stick to the bottom-right corner
                x = safeRight - panelSize.Width;
                y = safeBottom - panelSize.Height;
                break;
        }

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
    public void Tray_Open()
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

    internal enum TaskbarPosition { Left, Top, Right, Bottom }

    internal readonly record struct TaskbarInfo(TaskbarPosition Position, int Gap, bool AutoHide);

    // -----------------------------------------------------------------------------
    //  Taskbar helpers – ABM_GETTASKBARPOS / ABM_GETSTATE via SHAppBarMessage
    // -----------------------------------------------------------------------------
    private static TaskbarInfo GetTaskbarInfo(DisplayArea area)
    {
        var data = new APPBARDATA
        {
            cbSize = (uint)Marshal.SizeOf<APPBARDATA>()
        };

        // Locate the taskbar.
        if (SHAppBarMessage(ABM_GETTASKBARPOS, ref data) == 0)
            return new TaskbarInfo(TaskbarPosition.Bottom, 0, false); // failsafe

        var autoHide = (SHAppBarMessage(ABM_GETSTATE, ref data) & ABS_AUTOHIDE) != 0;

        // Use uEdge instead of guessing from the RECT.
        var pos = data.uEdge switch
        {
            ABE_LEFT => TaskbarPosition.Left,
            ABE_TOP => TaskbarPosition.Top,
            ABE_RIGHT => TaskbarPosition.Right,
            _ => TaskbarPosition.Bottom,   // ABE_BOTTOM or anything unexpected
        };

        // Thickness (gap) = shorter side of the rect.
        var gap = (pos == TaskbarPosition.Left || pos == TaskbarPosition.Right)
                  ? data.rc.right - data.rc.left    // width
                  : data.rc.bottom - data.rc.top;   // height

        return new TaskbarInfo(pos, gap, autoHide);
    }

    // -------------  P/Invoke plumbing -------------
    private const uint ABM_GETTASKBARPOS = 0x0005;
    private const uint ABM_GETSTATE = 0x0004;
    private const int ABS_AUTOHIDE = 0x0001;

    private const int ABE_LEFT = 0;   // values returned in APPBARDATA.uEdge
    private const int ABE_TOP = 1;
    private const int ABE_RIGHT = 2;
    private const int ABE_BOTTOM = 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;   // contains ABE_* value
        public RECT rc;
        public int lParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left, top, right, bottom;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern uint SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);
}
