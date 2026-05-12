using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Coder.Desktop.App.ViewModels;
using Coder.Desktop.App.Views.Pages;

namespace Coder.Desktop.App.Views;

public partial class TrayWindow : Window
{
    private readonly TransitioningContentControl _pageHost;
    private readonly TrayWindowShellViewModel _vm;

    private bool _hideOnFirstOpen;

    public TrayWindow()
    {
        InitializeComponent();

        _pageHost = this.FindControl<TransitioningContentControl>("PageHost")
                    ?? throw new InvalidOperationException("PageHost control was not found");

        _vm = new TrayWindowShellViewModel();
        DataContext = _vm;

        _vm.PropertyChanged += VmOnPropertyChanged;

        Deactivated += (_, _) =>
        {
            // Auto-hide when we lose focus (similar to WinUI version).
            // In the future, some pages may want to keep it open; that can be
            // handled by viewmodel state.
            Hide();
        };

        Opened += (_, _) =>
        {
            if (_hideOnFirstOpen)
            {
                _hideOnFirstOpen = false;
                Hide();
                return;
            }

            PositionInBottomRight();
        };

        UpdatePageContent(_vm.Page);
    }

    public void RequestHideOnFirstOpen()
    {
        _hideOnFirstOpen = true;
    }

    public void ShowNearSystemTray()
    {
        // Show() must be called before Activate() will do anything.
        Show();
        PositionInBottomRight();
        Activate();
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
            TrayWindowShellPage.Loading => new TrayWindowLoadingPage(),
            TrayWindowShellPage.Disconnected => new TrayWindowDisconnectedPage(),
            TrayWindowShellPage.LoginRequired => new TrayWindowLoginRequiredPage(),
            TrayWindowShellPage.Main => new TrayWindowMainPage(),
            _ => new TrayWindowLoadingPage(),
        };
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
            heightPx = (int)Math.Ceiling((_vm.ContentHeight + 48) * scaling);

        var x = workArea.X + workArea.Width - widthPx - margin;
        var y = workArea.Y + workArea.Height - heightPx - margin;

        Position = new PixelPoint(Math.Max(workArea.X, x), Math.Max(workArea.Y, y));
    }
}
