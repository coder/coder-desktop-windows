namespace Coder.Desktop.App.Services;

/// <summary>
/// Abstracts launching URIs/URLs. Replaces Windows.System.Launcher.
/// </summary>
public interface ILauncherService
{
    Task LaunchUriAsync(Uri uri);
}
