namespace Coder.Desktop.App.Services;

public interface IUriHandler
{
    Task HandleUri(Uri uri, CancellationToken ct = default);
}
