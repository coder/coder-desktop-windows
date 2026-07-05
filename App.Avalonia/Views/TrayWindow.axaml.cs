using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Coder.Desktop.App.Diagnostics;
using Coder.Desktop.App.Models;
using Coder.Desktop.App.Services;
using Coder.Desktop.App.ViewModels;
using Coder.Desktop.App.Views.Pages;

namespace Coder.Desktop.App.Views;

public partial class TrayWindow : Window
{
    private readonly TransitioningContentControl _pageHost;
    private readonly TrayWindowShellViewModel _vm;

    private IRpcController? _rpcController;
    private ICredentialManager? _credentialManager;

    private readonly TrayWindowLoadingPage _loadingPage;
    private TrayWindowDisconnectedPage _disconnectedPage;
    private TrayWindowLoginRequiredPage _loginRequiredPage;
    private TrayWindowMainPage _mainPage;

    private bool _hideOnFirstOpen;
    private DateTime _suppressAutoHideUntilUtc;
    private bool _positionUpdateQueued;
    private int _sizeChangedCount;
    private DateTime _lastSizeChangedLogUtc = DateTime.MinValue;
    private DispatcherTimer? _uiHeartbeatTimer;

    // Parameterless constructor keeps the XAML runtime loader/designer path valid.
    public TrayWindow()
    {
        InitializeComponent();

        _pageHost = this.FindControl<TransitioningContentControl>("PageHost")
                    ?? throw new InvalidOperationException("PageHost control was not found");

        _vm = new TrayWindowShellViewModel();
        DataContext = _vm;

        _loadingPage = new TrayWindowLoadingPage();
        _disconnectedPage = new TrayWindowDisconnectedPage();
        _loginRequiredPage = new TrayWindowLoginRequiredPage();
        _mainPage = new TrayWindowMainPage();

        _vm.PropertyChanged += VmOnPropertyChanged;

        Deactivated += (_, _) =>
        {
            // Auto-hide when we lose focus (similar to WinUI version).
            // Some tray implementations briefly steal focus immediately after
            // opening, so suppress auto-hide for a short grace period.
            if (DateTime.UtcNow < _suppressAutoHideUntilUtc)
                return;

            Hide();
        };

        Opened += (_, _) =>
        {
            AppBootstrapLogger.Info("Tray window opened");
            if (_hideOnFirstOpen)
            {
                _hideOnFirstOpen = false;
                Hide();
                return;
            }

            PositionInBottomRight();
        };

        SizeChanged += (_, _) =>
        {
            if (!IsVisible)
                return;

            _sizeChangedCount++;
            var now = DateTime.UtcNow;
            if (now - _lastSizeChangedLogUtc >= TimeSpan.FromSeconds(1))
            {
                AppBootstrapLogger.Info($"Tray SizeChanged x{_sizeChangedCount} frame={FrameSize?.Width}x{FrameSize?.Height}");
                _sizeChangedCount = 0;
                _lastSizeChangedLogUtc = now;
            }

            QueuePositionUpdate();
        };

        Closed += (_, _) =>
        {
            _uiHeartbeatTimer?.Stop();
            DetachRuntimeStateListeners();
        };

        _uiHeartbeatTimer = new DispatcherTimer(TimeSpan.FromSeconds(5), DispatcherPriority.Background, (_, _) =>
        {
            AppBootstrapLogger.Info($"UI heartbeat page={_vm.Page} visible={IsVisible} frame={FrameSize?.Width}x{FrameSize?.Height}");
        });
        _uiHeartbeatTimer.Start();

        // Ensure initial content is rendered even when the page value starts at Loading.
        UpdatePageContent(_vm.Page);
    }

    public TrayWindow(
        IRpcController rpcController,
        ICredentialManager credentialManager,
        TrayWindowDisconnectedViewModel disconnectedViewModel,
        TrayWindowLoginRequiredViewModel loginRequiredViewModel,
        TrayWindowViewModel mainViewModel)
        : this()
    {
        _rpcController = rpcController;
        _credentialManager = credentialManager;

        _disconnectedPage = new TrayWindowDisconnectedPage(disconnectedViewModel);
        _loginRequiredPage = new TrayWindowLoginRequiredPage(loginRequiredViewModel);
        _mainPage = new TrayWindowMainPage(mainViewModel);

        _rpcController.StateChanged += RpcControllerOnStateChanged;
        _credentialManager.CredentialsChanged += CredentialManagerOnCredentialsChanged;

        SetPageByState(_rpcController.GetState(), _credentialManager.GetCachedCredentials());
    }

    public void RequestHideOnFirstOpen()
    {
        _hideOnFirstOpen = true;
    }

    public void ShowNearSystemTray()
    {
        // Show() must be called before Activate() will do anything.
        _suppressAutoHideUntilUtc = DateTime.UtcNow.AddMilliseconds(300);
        Show();
        PositionInBottomRight();
        Activate();
    }

    private void DetachRuntimeStateListeners()
    {
        if (_rpcController is not null)
            _rpcController.StateChanged -= RpcControllerOnStateChanged;

        if (_credentialManager is not null)
            _credentialManager.CredentialsChanged -= CredentialManagerOnCredentialsChanged;
    }

    private void RpcControllerOnStateChanged(object? sender, RpcModel rpcModel)
    {
        RefreshPageFromCurrentState();
    }

    private void CredentialManagerOnCredentialsChanged(object? sender, CredentialModel credentialModel)
    {
        RefreshPageFromCurrentState();
    }

    private void RefreshPageFromCurrentState()
    {
        if (_rpcController is null || _credentialManager is null)
            return;

        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(RefreshPageFromCurrentState);
            return;
        }

        SetPageByState(_rpcController.GetState(), _credentialManager.GetCachedCredentials());
    }

    private void SetPageByState(RpcModel rpcModel, CredentialModel credentialModel)
    {
        if (credentialModel.State == CredentialState.Unknown)
        {
            SetPage(TrayWindowShellPage.Loading, rpcModel, credentialModel);
            return;
        }

        switch (rpcModel.RpcLifecycle)
        {
            case RpcLifecycle.Connected:
                if (credentialModel.State == CredentialState.Valid)
                    SetPage(TrayWindowShellPage.Main, rpcModel, credentialModel);
                else
                    SetPage(TrayWindowShellPage.LoginRequired, rpcModel, credentialModel);
                break;
            case RpcLifecycle.Disconnected:
            case RpcLifecycle.Connecting:
            default:
                SetPage(TrayWindowShellPage.Disconnected, rpcModel, credentialModel);
                break;
        }
    }

    private void SetPage(TrayWindowShellPage page, RpcModel rpcModel, CredentialModel credentialModel)
    {
        if (_vm.Page == page)
            return;

        AppBootstrapLogger.Info(
            $"Tray page -> {page} (rpc={rpcModel.RpcLifecycle}, vpn={rpcModel.VpnLifecycle}, credentials={credentialModel.State})");
        _vm.Page = page;
    }

    private void VmOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TrayWindowShellViewModel.Page))
            UpdatePageContent(_vm.Page);
    }

    private void UpdatePageContent(TrayWindowShellPage page)
    {
        _pageHost.Content = page switch
        {
            TrayWindowShellPage.Loading => _loadingPage,
            TrayWindowShellPage.Disconnected => _disconnectedPage,
            TrayWindowShellPage.LoginRequired => _loginRequiredPage,
            TrayWindowShellPage.Main => _mainPage,
            _ => _loadingPage,
        };
    }

    private void QueuePositionUpdate()
    {
        if (_positionUpdateQueued)
            return;

        _positionUpdateQueued = true;
        AppBootstrapLogger.Info("QueuePositionUpdate queued");
        Dispatcher.UIThread.Post(() =>
        {
            _positionUpdateQueued = false;
            AppBootstrapLogger.Info("QueuePositionUpdate running");
            if (IsVisible)
                PositionInBottomRight();
        }, DispatcherPriority.Background);
    }

    private void PositionInBottomRight()
    {
        var screen = Screens.Primary;
        if (screen is null)
            return;

        var workArea = screen.WorkingArea;

        // Position relative to the working area (bottom-right).
        const int margin = 12;

        var scaling = screen.Scaling;

        // FrameSize is in DIPs; convert to pixels for Window.Position.
        var frameSize = FrameSize;

        var widthPx = (int)Math.Ceiling((frameSize?.Width ?? Width) * scaling);
        var heightPx = (int)Math.Ceiling((frameSize?.Height ?? Height) * scaling);

        // If we haven't been measured yet, fall back to a reasonable estimate.
        if (widthPx <= 0)
            widthPx = (int)Math.Ceiling(Width * scaling);
        if (heightPx <= 0)
            heightPx = (int)Math.Ceiling((Math.Max(Height, 320) + 48) * scaling);

        var x = workArea.X + workArea.Width - widthPx - margin;
        var y = workArea.Y + workArea.Height - heightPx - margin;

        var newPosition = new PixelPoint(Math.Max(workArea.X, x), Math.Max(workArea.Y, y));
        if (Position != newPosition)
        {
            Position = newPosition;
            AppBootstrapLogger.Info($"Tray position set to {newPosition.X},{newPosition.Y} frame={frameSize?.Width}x{frameSize?.Height}");
        }
    }
}
