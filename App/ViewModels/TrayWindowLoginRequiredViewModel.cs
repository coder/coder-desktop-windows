using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Coder.Desktop.App.ViewModels;

public partial class TrayWindowLoginRequiredViewModel : ObservableObject
{
    [RelayCommand]
    public void Login()
    {
        // TODO: open the login window
    }
}
