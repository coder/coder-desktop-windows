using Google.Protobuf.WellKnownTypes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Coder.Desktop.App.Services;

/// <summary>
/// Settings contract exposing properties for app settings.
/// </summary>
public interface ISettingsManager<T> where T : ISettings, new()
{
    /// <summary>
    /// Reads the settings from the file system.
    /// Always returns the latest settings, even if they were modified by another instance of the app.
    /// Returned object is always a fresh instance, so it can be modified without affecting the stored settings.
    /// </summary>
    /// <param name="ct"></param>
    /// <returns></returns>
    public Task<T> Read(CancellationToken ct = default);
    /// <summary>
    /// Writes the settings to the file system.
    /// </summary>
    /// <param name="settings">Object containing the settings.</param>
    /// <param name="ct"></param>
    /// <returns></returns>
    public Task Write(T settings, CancellationToken ct = default);
    /// <summary>
    /// Returns null if the settings are not cached or not available.
    /// </summary>
    /// <returns></returns>
    public T? GetFromCache();
}

/// <summary>
/// Implemention of <see cref="ISettingsManager"/> that persists settings to a JSON file
/// located in the user's local application data folder.
/// </summary>
public sealed class SettingsManager<T> : ISettingsManager<T> where T : ISettings, new()
{
    private readonly string _settingsFilePath;
    private readonly string _appName = "CoderDesktop";
    private string _fileName;
    private readonly object _lock = new();

    private T? _cachedSettings;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(3);

    /// <param name="settingsFilePath">
    /// For unit‑tests you can pass an absolute path that already exists.
    /// Otherwise the settings file will be created in the user's local application data folder.
    /// </param>
    public SettingsManager(string? settingsFilePath = null)
    {
        if (settingsFilePath is null)
        {
            settingsFilePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        }
        else if (!Path.IsPathRooted(settingsFilePath))
        {
            throw new ArgumentException("settingsFilePath must be an absolute path if provided", nameof(settingsFilePath));
        }

        var folder = Path.Combine(
                settingsFilePath,
                _appName);

        Directory.CreateDirectory(folder);

        _fileName = T.SettingsFileName;
        _settingsFilePath = Path.Combine(folder, _fileName);
    }

    public async Task<T> Read(CancellationToken ct = default)
    {
        // try to get the lock with short timeout
        if (!await _gate.WaitAsync(LockTimeout, ct).ConfigureAwait(false))
            throw new InvalidOperationException(
                $"Could not acquire the settings lock within {LockTimeout.TotalSeconds} s.");

        try
        {
            if (!File.Exists(_settingsFilePath))
                return new();

            var json = await File.ReadAllTextAsync(_settingsFilePath, ct)
                                 .ConfigureAwait(false);

            // deserialize; fall back to default(T) if empty or malformed
            var result = JsonSerializer.Deserialize<T>(json)!;
            _cachedSettings = result;
            return result;
        }
        catch (OperationCanceledException)
        {
            throw; // propagate caller-requested cancellation
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to read settings from {_settingsFilePath}. " +
                "The file may be corrupted, malformed or locked.", ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task Write(T settings, CancellationToken ct = default)
    {
        // try to get the lock with short timeout
        if (!await _gate.WaitAsync(LockTimeout, ct).ConfigureAwait(false))
            throw new InvalidOperationException(
                $"Could not acquire the settings lock within {LockTimeout.TotalSeconds} s.");

        try
        {
            // overwrite the settings file with the new settings
            var json = JsonSerializer.Serialize(
                settings, new JsonSerializerOptions() { WriteIndented = true });
            _cachedSettings = settings; // cache the settings
            await File.WriteAllTextAsync(_settingsFilePath, json, ct)
                      .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;  // let callers observe cancellation
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to persist settings to {_settingsFilePath}. " +
                "The file may be corrupted, malformed or locked.", ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    public T? GetFromCache()
    {
        return _cachedSettings;
    }
}

public interface ISettings
{
    /// <summary>
    /// Gets the version of the settings schema.
    /// </summary>
    int Version { get; }

    /// <summary>
    /// FileName where the settings are stored.
    /// </summary>
    static abstract string SettingsFileName { get; }
}

/// <summary>
/// CoderConnect settings class that holds the settings for the CoderConnect feature.
/// </summary>
public class CoderConnectSettings : ISettings
{
    /// <summary>
    /// CoderConnect settings version. Increment this when the settings schema changes.
    /// In future iterations we will be able to handle migrations when the user has
    /// an older version.
    /// </summary>
    public int Version { get; set; }
    public bool ConnectOnLaunch { get; set; }
    public static string SettingsFileName { get; } = "coder-connect-settings.json";

    private const int VERSION = 1; // Default version for backward compatibility
    public CoderConnectSettings()
    {
        Version = VERSION;
        ConnectOnLaunch = false;
    }

    public CoderConnectSettings(int? version, bool connectOnLogin)
    {
        Version = version ?? VERSION;
        ConnectOnLaunch = connectOnLogin;
    }

    public CoderConnectSettings Clone()
    {
        return new CoderConnectSettings(Version, ConnectOnLaunch);
    }


}
