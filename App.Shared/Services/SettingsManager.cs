using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Coder.Desktop.App.Models;

namespace Coder.Desktop.App.Services;

public static class SettingsManagerUtils
{
    private const string AppName = "CoderDesktop";

    /// <summary>
    /// Generates the settings directory path and ensures it exists.
    /// </summary>
    /// <param name="settingsFilePath">Custom settings root, defaults to AppData/Local</param>
    public static string AppSettingsDirectory(string? settingsFilePath = null)
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
                AppName);

        Directory.CreateDirectory(folder);
        return folder;
    }
}

/// <summary>
/// Implementation of <see cref="ISettingsManager"/> that persists settings to
/// a JSON file located in the user's local application data folder.
/// </summary>
public sealed class SettingsManager<T> : ISettingsManager<T> where T : ISettings<T>, new()
{

    private readonly string _settingsFilePath;
    private readonly string _fileName;

    private T? _cachedSettings;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(3);

    /// <param name="settingsFilePath">
    /// For unit‑tests you can pass an absolute path that already exists.
    /// Otherwise the settings file will be created in the user's local application data folder.
    /// </param>
    public SettingsManager(string? settingsFilePath = null)
    {
        var folder = SettingsManagerUtils.AppSettingsDirectory(settingsFilePath);

        _fileName = T.SettingsFileName;
        _settingsFilePath = Path.Combine(folder, _fileName);
    }

    public async Task<T> Read(CancellationToken ct = default)
    {
        if (_cachedSettings is not null)
        {
            // return cached settings if available
            return _cachedSettings.Clone();
        }

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
            return _cachedSettings.Clone(); // return a fresh instance of the settings
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
}
