using Coder.Desktop.App.Models;

namespace Coder.Desktop.App.Services;

public interface ISettingsManager<T> where T : ISettings<T>, new()
{
    Task<T> Read(CancellationToken ct = default);
    Task Write(T settings, CancellationToken ct = default);
}
