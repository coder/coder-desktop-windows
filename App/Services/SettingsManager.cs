using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Coder.Desktop.App.Services;
/// <summary>
/// Generic persistence contract for simple key/value settings.
/// </summary>
public interface ISettingsManager
{
    /// <summary>
    /// Saves <paramref name="value"/> under <paramref name="name"/> and returns the value.
    /// </summary>
    T Save<T>(string name, T value);

    /// <summary>
    /// Reads the setting or returns <paramref name="defaultValue"/> when the key is missing.
    /// </summary>
    T Read<T>(string name, T defaultValue);
}
/// <summary>
/// JSON‑file implementation that works in unpackaged Win32/WinUI 3 apps.
/// </summary>
public sealed class SettingsManager : ISettingsManager
{
    private readonly string _settingsFilePath;
    private readonly string _fileName = "app-settings.json";
    private readonly object _lock = new();
    private Dictionary<string, JsonElement> _cache;

    public static readonly string ConnectOnLaunchKey = "ConnectOnLaunch";
    public static readonly string StartOnLoginKey = "StartOnLogin";

    /// <param name="appName">
    /// Sub‑folder under %LOCALAPPDATA% (e.g. "CoderDesktop").
    /// If <c>null</c> the folder name defaults to the executable name.
    /// For unit‑tests you can pass an absolute path that already exists.
    /// </param>
    public SettingsManager(string? appName = null)
    {
        // Allow unit‑tests to inject a fully‑qualified path.
        if (appName is not null && Path.IsPathRooted(appName))
        {
            _settingsFilePath = Path.Combine(appName, _fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsFilePath)!);
        }
        else
        {
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                appName ?? AppDomain.CurrentDomain.FriendlyName.ToLowerInvariant());
            Directory.CreateDirectory(folder);
            _settingsFilePath = Path.Combine(folder, _fileName);
        }

        _cache = Load();
    }

    public T Save<T>(string name, T value)
    {
        lock (_lock)
        {
            _cache[name] = JsonSerializer.SerializeToElement(value);
            Persist();
            return value;
        }
    }

    public T Read<T>(string name, T defaultValue)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(name, out var element))
            {
                try
                {
                    return element.Deserialize<T>() ?? defaultValue;
                }
                catch
                {
                    // Malformed value – fall back.
                    return defaultValue;
                }
            }
            return defaultValue; // key not found – return caller‑supplied default (false etc.)
        }
    }

    private Dictionary<string, JsonElement> Load()
    {
        if (!File.Exists(_settingsFilePath))
            return new();

        try
        {
            using var fs = File.OpenRead(_settingsFilePath);
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(fs) ?? new();
        }
        catch
        {
            // Corrupted file – start fresh.
            return new();
        }
    }

    private void Persist()
    {
        using var fs = File.Create(_settingsFilePath);
        var options = new JsonSerializerOptions { WriteIndented = true };
        JsonSerializer.Serialize(fs, _cache, options);
    }
}
