using System.Reflection;
using System.Runtime.InteropServices;

namespace Coder.Desktop.CoderSdk;

/// <summary>
///     Identifies which Coder Desktop process is making a request.
/// </summary>
public enum CoderComponent
{
    /// <summary>The tray application.</summary>
    Desktop,

    /// <summary>The privileged background service that manages the VPN tunnel.</summary>
    Core,
}

/// <summary>
///     Builds the User-Agent header value sent by every Coder Desktop HTTP client.
/// </summary>
public static class UserAgent
{
    private const string DesktopToken = "coder-desktop";
    private const string CoreToken = "coder-desktop-core";

    private const string UnknownVersion = "0.0.0";
    private const string UnknownPlatform = "unknown";

    /// <summary>
    ///     Builds a User-Agent for <paramref name="component" />, taking the version from the entry assembly.
    /// </summary>
    public static string Build(CoderComponent component)
    {
        return Build(component, Assembly.GetEntryAssembly());
    }

    /// <summary>
    ///     Builds a User-Agent for component, taking the version from versionSource. Prefer the single-argument overload outside of tests.
    /// </summary>
    public static string Build(CoderComponent component, Assembly? versionSource)
    {
        return $"{TokenOf(component)}/{VersionOf(versionSource)} ({Goos()}/{Goarch()})";
    }

    private static string TokenOf(CoderComponent component)
    {
        return component switch
        {
            CoderComponent.Desktop => DesktopToken,
            CoderComponent.Core => CoreToken,
            _ => throw new ArgumentOutOfRangeException(nameof(component), component, null),
        };
    }

    private static string VersionOf(Assembly? assembly)
    {
        // Assembly versions are four-part (0.8.4.0); the User-Agent reports the three-part release.
        var version = assembly?.GetName().Version;
        return version is null ? UnknownVersion : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    // Platform names deliberately match Go's GOOS/GOARCH Desktop clients, the CLI and the vpn-daemon
    private static string Goos()
    {
        if (OperatingSystem.IsWindows()) return "windows";
        if (OperatingSystem.IsMacOS()) return "darwin";
        if (OperatingSystem.IsLinux()) return "linux";
        return UnknownPlatform;
    }

    private static string Goarch()
    {
        return RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "amd64",
            Architecture.Arm => "arm",
            Architecture.Arm64 => "arm64",
            _ => UnknownPlatform,
        };
    }
}
