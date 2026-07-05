namespace Coder.Desktop.App.Services;

/// <summary>
/// Manages autostart via XDG autostart desktop entry.
/// </summary>
public class LinuxXdgStartupManager : IStartupManager
{
    private static readonly string AutostartDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "autostart");

    private static readonly string DesktopFilePath = Path.Combine(
        AutostartDir, "coder-desktop.desktop");

    private readonly string _execPath;

    public LinuxXdgStartupManager(string? execPath = null)
    {
        _execPath = execPath ?? "/usr/bin/coder-desktop";
    }

    public bool Enable()
    {
        Directory.CreateDirectory(AutostartDir);
        var content = $"""
            [Desktop Entry]
            Type=Application
            Name=Coder Desktop
            Exec={_execPath} --minimized
            X-GNOME-Autostart-enabled=true
            NoDisplay=true
            """;
        File.WriteAllText(DesktopFilePath, content);
        return true;
    }

    public void Disable()
    {
        if (File.Exists(DesktopFilePath))
            File.Delete(DesktopFilePath);
    }

    public bool IsEnabled() => File.Exists(DesktopFilePath);

    public bool IsDisabledByPolicy() => false;
}
