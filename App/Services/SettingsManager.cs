using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Coder.Desktop.App.Services;

/// <summary>
/// Settings contract exposing properties for app settings.
/// </summary>
public interface ISettingsManager
{
    /// <summary>
    /// Returns the value of the StartOnLogin setting. Returns <c>false</c> if the key is not found.
    /// </summary>
    bool StartOnLogin { get; set; }

    /// <summary>
    /// Returns the value of the ConnectOnLaunch setting. Returns <c>false</c> if the key is not found.
    /// </summary>
    bool ConnectOnLaunch { get; set; }
}

/// <summary>
/// Implemention of <see cref="ISettingsManager"/> that persists settings to a JSON file
/// located in the user's local application data folder.
/// </summary>
public sealed class SettingsManager : ISettingsManager
{
    private readonly string _settingsFilePath;
    private Settings _settings;
    private readonly string _fileName = "app-settings.json";
    private readonly string _appName = "CoderDesktop";
    private readonly object _lock = new();

    public const string ConnectOnLaunchKey = "ConnectOnLaunch";
    public const string StartOnLoginKey = "StartOnLogin";

    public bool StartOnLogin
    {
        get
        {
            return Read(StartOnLoginKey, false);
        }
        set
        {
            Save(StartOnLoginKey, value);
        }
    }

    public bool ConnectOnLaunch
    {
        get
        {
            return Read(ConnectOnLaunchKey, false);
        }
        set
        {
            Save(ConnectOnLaunchKey, value);
        }
    }

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

        string folder = Path.Combine(
                settingsFilePath,
                _appName);

        Directory.CreateDirectory(folder);
        _settingsFilePath = Path.Combine(folder, _fileName);

        if (!File.Exists(_settingsFilePath))
        {
            // Create the settings file if it doesn't exist
            string emptyJson = JsonSerializer.Serialize(new { });
            File.WriteAllText(_settingsFilePath, emptyJson);
            _settings = new();
        }
        else
        {
            _settings = Load();
        }
    }

    private void Save(string name, bool value)
    {
        lock (_lock)
        {
            try
            {
                // We lock the file for the entire operation to prevent concurrent writes   
                using var fs = new FileStream(_settingsFilePath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None);

                // Ensure cache is loaded before saving 
                var freshCache = JsonSerializer.Deserialize<Settings>(fs) ?? new();
                _settings = freshCache;
                _settings.Options[name] = JsonSerializer.SerializeToElement(value);
                fs.Position = 0; // Reset stream position to the beginning before writing

                JsonSerializer.Serialize(fs, _settings, new JsonSerializerOptions { WriteIndented = true });

                // This ensures the file is truncated to the new length
                // if the new content is shorter than the old content
                fs.SetLength(fs.Position);
            }
            catch
            {
                throw new InvalidOperationException($"Failed to persist settings to {_settingsFilePath}. The file may be corrupted, malformed or locked.");
            }
        }
    }

    private bool Read(string name, bool defaultValue)
    {
        lock (_lock)
        {
            if (_settings.Options.TryGetValue(name, out var element))
            {
                try
                {
                    return element.Deserialize<bool?>() ?? defaultValue;
                }
                catch
                {
                    // malformed value – return default value
                    return defaultValue;
                }
            }
            return defaultValue; // key not found – return default value
        }
    }

    private Settings Load()
    {
        try
        {
            using var fs = File.OpenRead(_settingsFilePath);
            return JsonSerializer.Deserialize<Settings>(fs) ?? new(null, new Dictionary<string, JsonElement>());
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to load settings from {_settingsFilePath}. The file may be corrupted or malformed. Exception: {ex.Message}");
        }
    }

    [JsonSerializable(typeof(Settings))]
    private class Settings
    {
        /// <summary>
        /// User settings version. Increment this when the settings schema changes.
        /// In future iterations we will be able to handle migrations when the user has
        /// an older version.
        /// </summary>
        public int Version { get; set; } = 1;
        public Dictionary<string, JsonElement> Options { get; set; }
        public Settings()
        {
            Options = new Dictionary<string, JsonElement>();
        }

        public Settings(int? version, Dictionary<string, JsonElement> options)
        {
            Version = version ?? Version;
            Options = options;
        }
    }
}
