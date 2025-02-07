using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Coder.Desktop.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.Foundation;
using Windows.Foundation.Collections;

namespace Coder.Desktop.App;

/// <summary>
/// A login page to enter the Coder Server URL
/// </summary>
public sealed partial class SignInURLPage : Page
{
    public SignInURLPage(SignInWindow parent, SignInViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        SignInWindow = parent;
    }

    public readonly SignInViewModel ViewModel;
    public readonly SignInWindow SignInWindow;
}
