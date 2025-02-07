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

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Coder.Desktop.App
{
    /// <summary>
    /// A sign in page to accept the user's Coder token.
    /// </summary>
    public sealed partial class SignInTokenPage : Page
    {
        public SignInTokenPage(SignInWindow parent, SignInViewModel viewModel)
        {
            InitializeComponent();
            ViewModel = viewModel;
            SignInWindow = parent;
        }

        public readonly SignInViewModel ViewModel;
        public readonly SignInWindow SignInWindow;
    }
}
