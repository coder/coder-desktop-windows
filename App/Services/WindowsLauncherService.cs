using System;
using System.Threading.Tasks;
using Windows.System;

namespace Coder.Desktop.App.Services;

/// <summary>
///     WinUI implementation of ILauncherService using the Windows shell launcher.
/// </summary>
public class WindowsLauncherService : ILauncherService
{
    public async Task LaunchUriAsync(Uri uri)
    {
        await Launcher.LaunchUriAsync(uri);
    }
}
