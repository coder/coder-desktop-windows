using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;

namespace Coder.Desktop.App.Services;

/// <summary>
///     WinUI implementation of IClipboardService.
/// </summary>
public class WinUIClipboardService : IClipboardService
{
    public Task SetTextAsync(string text)
    {
        var dataPackage = new DataPackage();
        dataPackage.SetText(text);
        Clipboard.SetContent(dataPackage);
        return Task.CompletedTask;
    }
}
