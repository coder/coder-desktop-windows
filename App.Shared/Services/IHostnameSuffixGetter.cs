namespace Coder.Desktop.App.Services;

public interface IHostnameSuffixGetter
{
    event EventHandler<string> SuffixChanged;
    string GetCachedSuffix();
}
