using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Graphics;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using WinRT.Interop;
using WindowActivatedEventArgs = Microsoft.UI.Xaml.WindowActivatedEventArgs;
using Coder.Desktop.App.ViewModels;

namespace Coder.Desktop.App;

public enum AgentStatus
{
    Green,
    Red,
    Gray,
}

public partial class Agent
{
    public required string Hostname { get; set; } // without suffix
    public required string Suffix { get; set; }
    public AgentStatus Status { get; set; }

    public Brush StatusColor => Status switch
    {
        AgentStatus.Green => new SolidColorBrush(Color.FromArgb(255, 52, 199, 89)),
        AgentStatus.Red => new SolidColorBrush(Color.FromArgb(255, 255, 59, 48)),
        _ => new SolidColorBrush(Color.FromArgb(255, 142, 142, 147)),
    };

    [RelayCommand]
    private void AgentHostnameButton_Click()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                // TODO: this should probably be more robust instead of just joining strings
                FileName = "http://" + Hostname + Suffix,
                UseShellExecute = true,
            });
        }
        catch
        {
            // TODO: log (notify?)
        }
    }

    [RelayCommand]
    private void AgentHostnameCopyButton_Click(object parameter)
    {
        var dataPackage = new DataPackage
        {
            RequestedOperation = DataPackageOperation.Copy,
        };
        dataPackage.SetText(Hostname + Suffix);
        Clipboard.SetContent(dataPackage);

        if (parameter is not FrameworkElement frameworkElement) return;

        var flyout = new Flyout
        {
            Content = new TextBlock
            {
                Text = "DNS Copied",
                Margin = new Thickness(4),
            },
        };
        FlyoutBase.SetAttachedFlyout(frameworkElement, flyout);
        FlyoutBase.ShowAttachedFlyout(frameworkElement);
    }

    public void AgentHostnameText_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBlock textBlock) return;
        textBlock.Inlines.Clear();
        textBlock.Inlines.Add(new Run
        {
            Text = Hostname,
            Foreground =
                (SolidColorBrush)Application.Current.Resources.ThemeDictionaries[
                    "DefaultTextForegroundThemeBrush"],
        });
        textBlock.Inlines.Add(new Run
        {
            Text = Suffix,
            Foreground =
                (SolidColorBrush)Application.Current.Resources.ThemeDictionaries[
                    "SystemControlForegroundBaseMediumBrush"],
        });
    }
}

public sealed partial class TrayWindow : Window
{
    private const int WIDTH = 300;

    private NativeApi.POINT? _lastActivatePosition;

    public ObservableCollection<Agent> Agents =
    [
        new()
        {
            Hostname = "coder2",
            Suffix = ".coder",
            Status = AgentStatus.Green,
        },
        new()
        {
            Hostname = "coder3",
            Suffix = ".coder",
            Status = AgentStatus.Red,
        },
        new()
        {
            Hostname = "coder4",
            Suffix = ".coder",
            Status = AgentStatus.Gray,
        },
        new()
        {
            Hostname = "superlongworkspacenamewhyisitsolong",
            Suffix = ".coder",
            Status = AgentStatus.Gray,
        },
    ];

    public SignInViewModel SignInViewModel { get; }

    public TrayWindow(SignInViewModel signInViewModel)
    {
        SignInViewModel = signInViewModel;
        InitializeComponent();
        AppWindow.Hide();
        SystemBackdrop = new DesktopAcrylicBackdrop();
        Activated += Window_Activated;

        // Setting OpenCommand and ExitCommand directly in the .xaml doesn't seem to work for whatever reason.
        TrayIcon.OpenCommand = Tray_OpenCommand;
        TrayIcon.ExitCommand = Tray_ExitCommand;

        if (Content is FrameworkElement frameworkElement)
            frameworkElement.SizeChanged += Content_SizeChanged;
        else
            throw new Exception("Failed to get Content as FrameworkElement for window");

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
        var result = NativeApi.DwmSetWindowAttribute(windowHandle, 33, ref value, Marshal.SizeOf<int>());
        if (result != 0) throw new Exception("Failed to set window corner preference");
    }

    private void Content_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ResizeWindow();
        MoveWindow();
    }

    private void ResizeWindow()
    {
        if (Content is not FrameworkElement content)
            throw new Exception("Failed to get Content as FrameworkElement for window");

        // Measure the desired size of the content
        content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var desiredSize = content.DesiredSize;

        // Adjust the AppWindow size
        var scale = DisplayScale.WindowScale(this);
        var height = (int)(desiredSize.Height * scale);
        var width = (int)(WIDTH * scale);
        AppWindow.Resize(new SizeInt32(width, height));
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

    private void ButtonBase_OnClick(object sender, RoutedEventArgs e)
    {
        Agents.Add(new Agent
        {
            Hostname = "cool",
            Suffix = ".coder",
            Status = AgentStatus.Gray,
        });
    }

    [RelayCommand]
    private void Tray_Open()
    {
        MoveResizeAndActivate();
    }

    [RelayCommand]
    private void Tray_Exit()
    {
        // TODO: implement exit
    }

    public class NativeApi
    {
        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hwnd);

        public struct POINT
        {
            public int X;
            public int Y;
        }
    }

    [RelayCommand]
    private void SignIn_Click()
    {
        var window = new SignInWindow(SignInViewModel);
        window.Activate();
    }
}
