using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Coder.Desktop.App.ViewModels;

/// <summary>
/// The View Model backing the sign in window and all its associated pages.
/// </summary>
public partial class SignInViewModel : ObservableObject
{
    public SignInViewModel()
    {
        _url = string.Empty;
        CoderToken = string.Empty;
    }

    [ObservableProperty]
    private string _url;

    public string CoderToken;

    [ObservableProperty]
    private string? _loginError;

    [RelayCommand]
    public void SignIn_Click()
    {
        // TODO: this should call into the backing model to do the login with _url and Token.
        LoginError = "This is a placeholder error.";
    }

    public string GenTokenURL
    {
        get
        {
            // TODO: use a real URL parsing library
            return _url + "/cli-auth";
        }
    }
}
