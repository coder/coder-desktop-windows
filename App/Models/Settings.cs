namespace Coder.Desktop.App.Models;

public interface ISettings<T> : ICloneable<T>
{
    /// <summary>
    /// FileName where the settings are stored.
    /// </summary>
    static abstract string SettingsFileName { get; }

    /// <summary>
    /// Gets the version of the settings schema.
    /// </summary>
    int Version { get; }
}

public interface ICloneable<T>
{
    /// <summary>
    /// Creates a deep copy of the settings object.
    /// </summary>
    /// <returns>A new instance of the settings object with the same values.</returns>
    T Clone();
}

/// <summary>
/// CoderConnect settings class that holds the settings for the CoderConnect feature.
/// </summary>
public class CoderConnectSettings : ISettings<CoderConnectSettings>
{
    public static string SettingsFileName { get; } = "coder-connect-settings.json";
    public int Version { get; set; }
    /// <summary>
    /// When this is true, CoderConnect will automatically connect to the Coder VPN when the application starts.
    /// </summary>
    public bool ConnectOnLaunch { get; set; }

    /// <summary>
    /// CoderConnect current settings version. Increment this when the settings schema changes.
    /// In future iterations we will be able to handle migrations when the user has
    /// an older version.
    /// </summary>
    private const int VERSION = 1;

    public CoderConnectSettings()
    {
        Version = VERSION;

        ConnectOnLaunch = false;
    }

    public CoderConnectSettings(int? version, bool connectOnLaunch)
    {
        Version = version ?? VERSION;

        ConnectOnLaunch = connectOnLaunch;
    }

    public CoderConnectSettings Clone()
    {
        return new CoderConnectSettings(Version, ConnectOnLaunch);
    }
}
