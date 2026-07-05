namespace Coder.Desktop.App.Services;

/// <summary>
/// Abstracts clipboard access. Replaces Windows.ApplicationModel.DataTransfer.Clipboard.
/// </summary>
public interface IClipboardService
{
    Task SetTextAsync(string text);
}
