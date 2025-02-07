using Coder.Desktop.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Coder.Desktop.App.Views.Pages;

/// <summary>
///     A login page to enter the Coder Server URL
/// </summary>
public sealed partial class SignInUrlPage : Page
{
    public readonly SignInViewModel ViewModel;
    public readonly SignInWindow SignInWindow;

    public SignInUrlPage(SignInWindow parent, SignInViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        SignInWindow = parent;
    }
}
