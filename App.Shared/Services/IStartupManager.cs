namespace Coder.Desktop.App.Services;

public interface IStartupManager
{
    bool Enable();
    void Disable();
    bool IsEnabled();
    bool IsDisabledByPolicy();
}
