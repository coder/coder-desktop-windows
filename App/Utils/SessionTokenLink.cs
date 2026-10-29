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

    public const string UntrustedUrlMessage =
        "This app's URL isn't trusted to sign you in to Coder. Ask your template administrator to fix the " +
        "app's url in the template's coder_app configuration.";

    /// <param name="Base">Scheme and authority, matched case-insensitively.</param>
    /// <param name="Path">Matched exactly, up to where the params start.</param>
    /// <param name="FirstParam">Required leading param, matched exactly.</param>
    private record Handler(string Base, string Path, string FirstParam = "");

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
        new("jetbrains-gateway://connect", "#", "type=coder&"),
    ];

    public static bool IsKnownHandler(string appUrl)
    {
        return GetHandler(appUrl) is not null;
    }

    /// <summary>
    ///     Returns null if the link has no placeholder, or its only <c>url</c> is
    ///     <paramref name="signedInUrl" /> or <paramref name="dashboardUrl" />.
    /// </summary>
    public static string? BlockReason(string appUrl, Uri signedInUrl, Uri? dashboardUrl)
    {
        if (!appUrl.Contains(Placeholder)) return null;
        var handler = GetHandler(appUrl);
        if (handler is null || appUrl.Split(Placeholder).Length != 2) return UntrustedUrlMessage;

        // The placeholder is replaced raw, so it must be the raw `token` value.
        // Gateway doesn't decode names, IDEs decode values twice, and VS Code
        // ignores params after a `#`.
        var parameters = appUrl[(handler.Base.Length + handler.Path.Length)..];
        var entries = parameters.Split('&');
        var decoded = Uri.UnescapeDataString(parameters);
        var url = UrlParam(parameters);
        if (!entries.Contains($"token={Placeholder}") ||
            entries.Any(entry => entry.Split('=')[0].Contains('%')) ||
            parameters.Contains('#') ||
            decoded.Contains('%') ||
            url is null ||
            UrlParam(decoded) != url)
            return UntrustedUrlMessage;

        return IsSameOrigin(url, signedInUrl) || (dashboardUrl is not null && IsSameOrigin(url, dashboardUrl))
            ? null
            : $"This app signs in to {url}, but Coder Desktop is signed in to {signedInUrl}.";
    }

    /// <summary>
    ///     Links with control characters never match, since parsers drop tab, LF
    ///     and CR. <c>Ascii.EqualsIgnoreCase</c> doesn't fold a Kelvin sign into <c>k</c>.
    /// </summary>
    private static Handler? GetHandler(string appUrl)
    {
        if (appUrl.Any(char.IsControl)) return null;
        return Handlers.FirstOrDefault(handler =>
            appUrl.Length >= handler.Base.Length &&
            Ascii.EqualsIgnoreCase(appUrl.AsSpan(0, handler.Base.Length), handler.Base) &&
            appUrl.AsSpan(handler.Base.Length).StartsWith(handler.Path + handler.FirstParam, StringComparison.Ordinal));
    }

    /// <summary>
    ///     Returns <c>url</c> if the names are unique and it's a bare http(s)
    ///     origin, matched raw since parsing normalizes <c>/.</c> and drops an
    ///     empty <c>?</c>.
    /// </summary>
    private static string? UrlParam(string query)
    {
        // Parsed like `URLSearchParams`.
        var parameters = query.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(entry => entry.Split('=', 2).Select(part => Uri.UnescapeDataString(part.Replace('+', ' '))).ToArray())
            .ToList();
        var url = parameters.FirstOrDefault(parts => parts[0] == "url" && parts.Length == 2)?[1];
        return parameters.DistinctBy(parts => parts[0]).Count() == parameters.Count &&
               url is not null &&
               Regex.IsMatch(url, @"^https?://[^/\\?#@\s]+/?\z", RegexOptions.IgnoreCase)
            ? url
            : null;
    }

    private static bool IsSameOrigin(string link, Uri url)
    {
        return Uri.TryCreate(link, UriKind.Absolute, out var origin) &&
               Uri.Compare(origin, url, UriComponents.SchemeAndServer, UriFormat.UriEscaped,
                   StringComparison.OrdinalIgnoreCase) == 0;
    }
}
