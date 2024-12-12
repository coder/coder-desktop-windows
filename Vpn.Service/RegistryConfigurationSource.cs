using Microsoft.Extensions.Configuration;
using Microsoft.Win32;

namespace Coder.Desktop.Vpn.Service;

public class RegistryConfigurationSource(RegistryKey root, string subKeyName) : IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        return new RegistryConfigurationProvider(root, subKeyName);
    }
}

public class RegistryConfigurationProvider(RegistryKey root, string subKeyName) : ConfigurationProvider
{
    public override void Load()
    {
        using var key = root.OpenSubKey(subKeyName);
        if (key == null) return;

        foreach (var valueName in key.GetValueNames()) Data[valueName] = key.GetValue(valueName)?.ToString();
    }
}
