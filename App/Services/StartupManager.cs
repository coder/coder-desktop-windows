using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Security;

namespace Coder.Desktop.App.Services;

public interface IStartupManager
{
    /// <summary>
    /// Adds the current executable to the per‑user Run key. Returns <c>true</c> if successful.
    /// Fails (returns <c>false</c>) when blocked by policy or lack of permissions.
    /// </summary>
    bool Enable();
    /// <summary>
    /// Removes the value from the Run key (no-op if missing).
    /// </summary>
    void Disable();
    /// <summary>
    /// Checks whether the value exists in the Run key.
    /// </summary>
    bool IsEnabled();
    /// <summary>
    /// Detects whether group policy disables per‑user startup programs.
    /// Mirrors <see cref="Windows.ApplicationModel.StartupTaskState.DisabledByPolicy"/>.
    /// </summary>
    bool IsDisabledByPolicy();
}

public class StartupManager : IStartupManager
{
    private const string RunKey = @"Software\\Microsoft\\Windows\\CurrentVersion\\Run";
    private const string PoliciesExplorerUser = @"Software\\Microsoft\\Windows\\CurrentVersion\\Policies\\Explorer";
    private const string PoliciesExplorerMachine = @"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\Explorer";
    private const string DisableCurrentUserRun = "DisableCurrentUserRun";
    private const string DisableLocalMachineRun = "DisableLocalMachineRun";

    private const string _defaultValueName = "CoderDesktopApp";

    public bool Enable()
    {
        if (IsDisabledByPolicy())
            return false;

        string exe = Process.GetCurrentProcess().MainModule!.FileName;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKey)!;
            key.SetValue(_defaultValueName, $"\"{exe}\"");
            return true;
        }
        catch (UnauthorizedAccessException) { return false; }
        catch (SecurityException) { return false; }
    }

    public void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(_defaultValueName, throwOnMissingValue: false);
    }

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(_defaultValueName) != null;
    }

    public bool IsDisabledByPolicy()
    {
        // User policy – HKCU
        using (var keyUser = Registry.CurrentUser.OpenSubKey(PoliciesExplorerUser))
        {
            if ((int?)keyUser?.GetValue(DisableCurrentUserRun) == 1) return true;
        }
        // Machine policy – HKLM
        using (var keyMachine = Registry.LocalMachine.OpenSubKey(PoliciesExplorerMachine))
        {
            if ((int?)keyMachine?.GetValue(DisableLocalMachineRun) == 1) return true;
        }
        return false;
    }
}

