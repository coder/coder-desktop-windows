using Microsoft.Extensions.Configuration;
using Microsoft.Win32;

namespace Coder.Desktop.Vpn;

public class RegistryConfigurationSource : IConfigurationSource
{
    private readonly RegistryKey _root;
    private readonly string _subKeyName;

    // ReSharper disable once ConvertToPrimaryConstructor
    public RegistryConfigurationSource(RegistryKey root, string subKeyName)
    {
        _root = root;
        _subKeyName = subKeyName;
    }

    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        return new RegistryConfigurationProvider(_root, _subKeyName);
    }
}

public class RegistryConfigurationProvider : ConfigurationProvider
{
    private readonly RegistryKey _root;
    private readonly string _subKeyName;

    // ReSharper disable once ConvertToPrimaryConstructor
    public RegistryConfigurationProvider(RegistryKey root, string subKeyName)
    {
        _root = root;
        _subKeyName = subKeyName;
    }

    public override void Load()
    {
        using var key = _root.OpenSubKey(_subKeyName);
        if (key == null) return;

        foreach (var valueName in key.GetValueNames()) Data[valueName] = key.GetValue(valueName)?.ToString();
    }
}
