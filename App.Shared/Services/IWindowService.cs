namespace Coder.Desktop.App.Services;

/// <summary>
/// Abstracts window creation so ViewModels don't depend on UI framework types.
/// </summary>
public interface IWindowService
{
    void ShowSignInWindow();
    void ShowSettingsWindow();
    void ShowFileSyncListWindow();
    void ShowMessageWindow(string title, string message, string windowTitle);
}
