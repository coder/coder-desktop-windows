using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Coder.Desktop.App.Utils;

/// <summary>
///     Decides whether an external app link may receive the user's session
///     token, using the same rules as the Coder dashboard.
/// </summary>
public static class SessionTokenLink
{
    public const string Placeholder = "$SESSION_TOKEN";
    public const string BlockedMessage = "This app's URL isn't trusted to sign you in to Coder.";

    // Base (scheme and authority) is case-insensitive; Path runs through the
    // first param and is exact.
    private record Handler(string Base, string Path);

    private static readonly Handler[] Handlers =
    [
        .. new[]
            {
                "vscode", "vscode-insiders", "windsurf", "devin", "cursor", "kiro", "positron", "antigravity",
                "antigravity-ide",
            }
            .SelectMany(scheme => new[] { "/open?", "/openDevContainer?" }
                .Select(path => new Handler($"{scheme}://coder.coder-remote", path))),
        new("jetbrains://gateway", "/coder?"),
        new("jetbrains://gateway", "/com.coder.toolbox?"),
        // Gateway also handles non-Coder providers, such as `type=ssh`.
        new("jetbrains-gateway://connect", "#type=coder&"),
    ];

    public static bool IsKnownHandler(string appUrl)
    {
        return GetHandler(appUrl) is not null;
    }

    public static bool IsBlocked(string appUrl, Uri? deploymentUrl)
    {
        return appUrl.Contains(Placeholder) && !IsDestinationTrusted(appUrl, deploymentUrl);
    }

    /// <summary>
    ///     Replaces the placeholder with <paramref name="token" />, or with nothing
    ///     for sub-agent apps. Returns null if the link isn't trusted.
    /// </summary>
    public static string? Substitute(string appUrl, Uri? deploymentUrl, string token, bool isSubAgentApp)
    {
        return IsDestinationTrusted(appUrl, deploymentUrl)
            ? appUrl.Replace(Placeholder, isSubAgentApp ? "" : token)
            : null;
    }

    /// <summary>
    ///     True when the link opens a known handler, its only <c>url</c> is the
    ///     deployment's bare origin, and the placeholder is its only <c>token</c>.
    /// </summary>
    public static bool IsDestinationTrusted(string appUrl, Uri? deploymentUrl)
    {
        // Some parsers drop tab, LF and CR, so `ur\tl` could become `url`.
        if (deploymentUrl is null || appUrl.Any(char.IsControl) || appUrl.Split(Placeholder).Length != 2)
            return false;
        var handler = GetHandler(appUrl);
        if (handler is null) return false;

        var parameters = appUrl[(handler.Base.Length + handler.Path.IndexOfAny(['?', '#']) + 1)..];
        var entries = parameters.Split('&');
        var decoded = Uri.UnescapeDataString(parameters);
        // The placeholder is replaced raw, so it must be the raw `token` entry.
        // Gateway doesn't decode names, and IDEs decode values twice.
        return !parameters.Contains('#') &&
               entries.Contains($"token={Placeholder}") &&
               entries.All(entry => !entry.Split('=')[0].Contains('%')) &&
               !decoded.Contains('%') &&
               HasTrustedParams(parameters, deploymentUrl) &&
               HasTrustedParams(decoded, deploymentUrl);
    }

    private static Handler? GetHandler(string appUrl)
    {
        return Handlers.FirstOrDefault(h =>
            appUrl.Length >= h.Base.Length &&
            Ascii.EqualsIgnoreCase(appUrl.AsSpan(0, h.Base.Length), h.Base) &&
            appUrl.AsSpan(h.Base.Length).StartsWith(h.Path, StringComparison.Ordinal));
    }

    private static bool HasTrustedParams(string query, Uri deploymentUrl)
    {
        var parsed = query.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(entry => entry.Split('=', 2).Select(p => Uri.UnescapeDataString(p.Replace('+', ' '))).ToArray())
            .Select(pair => (Name: pair[0], Value: pair.ElementAtOrDefault(1) ?? ""))
            .ToList();
        var url = parsed.FirstOrDefault(p => p.Name == "url").Value;
        // `url` must be a bare origin, matched raw since parsing normalizes `/.` and `?`.
        return parsed.DistinctBy(p => p.Name).Count() == parsed.Count &&
               parsed.FirstOrDefault(p => p.Name == "token").Value == Placeholder &&
               url is not null &&
               Regex.IsMatch(url, @"^https?://[^/\\?#@\s]+/?\z", RegexOptions.IgnoreCase) &&
               Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
               Uri.Compare(uri, deploymentUrl, UriComponents.SchemeAndServer, UriFormat.UriEscaped,
                   StringComparison.OrdinalIgnoreCase) == 0;
    }
}
