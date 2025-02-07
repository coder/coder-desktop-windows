using Coder.Desktop.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Coder.Desktop.App.Views.Pages;

/// <summary>
///     A sign in page to accept the user's Coder token.
/// </summary>
public sealed partial class SignInTokenPage : Page
{
    public readonly SignInViewModel ViewModel;
    public readonly SignInWindow SignInWindow;

    public SignInTokenPage(SignInWindow parent, SignInViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        SignInWindow = parent;
    }
}
