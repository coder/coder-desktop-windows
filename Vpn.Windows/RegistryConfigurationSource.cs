using Microsoft.Extensions.Configuration;
using Microsoft.Win32;

namespace Coder.Desktop.Vpn;

public class RegistryConfigurationSource : IConfigurationSource
{
    private readonly RegistryKey _root;
    private readonly string _subKeyName;
    private readonly string[] _ignoredPrefixes;

    // ReSharper disable once ConvertToPrimaryConstructor
    public RegistryConfigurationSource(RegistryKey root, string subKeyName, params string[] ignoredPrefixes)
    {
        _root = root;
        _subKeyName = subKeyName;
        _ignoredPrefixes = ignoredPrefixes;
    }

    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        return new RegistryConfigurationProvider(_root, _subKeyName, _ignoredPrefixes);
    }
}

public class RegistryConfigurationProvider : ConfigurationProvider
{
    private readonly RegistryKey _root;
    private readonly string _subKeyName;
    private readonly string[] _ignoredPrefixes;

    // ReSharper disable once ConvertToPrimaryConstructor
    public RegistryConfigurationProvider(RegistryKey root, string subKeyName, string[] ignoredPrefixes)
    {
        _root = root;
        _subKeyName = subKeyName;
        _ignoredPrefixes = ignoredPrefixes;
    }

    public override void Load()
    {
        using var key = _root.OpenSubKey(_subKeyName);
        if (key == null) return;

        foreach (var valueName in key.GetValueNames())
        {
            if (_ignoredPrefixes.Any(prefix => valueName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                continue;
            Data[valueName] = key.GetValue(valueName)?.ToString();
        }
    }
}
