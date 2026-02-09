using System.Diagnostics;
using Coder.Desktop.App.Services;

namespace Coder.Desktop.App;

public class ProcessLauncherService : ILauncherService
{
    public Task LaunchUriAsync(Uri uri)
    {
        Process.Start(new ProcessStartInfo(uri.ToString())
        {
            UseShellExecute = true
        });
        return Task.CompletedTask;
    }
}
