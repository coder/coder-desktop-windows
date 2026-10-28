using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Coder.Desktop.App.Utils;

/// <summary>
///     Decides whether an external app link may receive the user's session
///     token. Applies the same rules as the Coder dashboard.
/// </summary>
public static partial class SessionTokenLink
{
    /// <summary>Replaced with the user's session token in trusted app links.</summary>
    public const string Placeholder = "$SESSION_TOKEN";

    /// <summary>Shown when an app needs a session token but its link isn't trusted with one.</summary>
    public const string BlockedMessage = "This app's URL isn't trusted to sign you in to Coder.";

    /// <param name="Base">Scheme and authority, matched case-insensitively.</param>
    /// <param name="Path">Path through the first required param, matched exactly.</param>
    private record Handler(string Base, string Path);

    /// <summary>The only deep-link handlers that may receive the session token.</summary>
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

    public static bool NeedsSessionToken(string appUrl)
    {
        return appUrl.Contains(Placeholder, StringComparison.Ordinal);
    }

    /// <summary>True when the link needs a session token but isn't trusted with one.</summary>
    public static bool IsBlocked(string appUrl, Uri? deploymentUrl)
    {
        return NeedsSessionToken(appUrl) && !IsDestinationTrusted(appUrl, deploymentUrl);
    }

    /// <summary>
    ///     Returns the link with the placeholder replaced by <paramref name="token" />,
    ///     or by an empty string for sub-agent apps. Returns null if the link
    ///     isn't trusted with a token.
    /// </summary>
    public static string? Substitute(string appUrl, Uri? deploymentUrl, string token, bool isSubAgentApp)
    {
        if (!IsDestinationTrusted(appUrl, deploymentUrl)) return null;
        // Sub-agent apps never get a token; `coder open app` refuses them too.
        return appUrl.Replace(Placeholder, isSubAgentApp ? "" : token, StringComparison.Ordinal);
    }

    /// <summary>
    ///     True when the link opens a known Coder handler, its only <c>url</c>
    ///     param is <paramref name="deploymentUrl" />'s origin, and the
    ///     placeholder is its only <c>token</c>.
    /// </summary>
    public static bool IsDestinationTrusted(string appUrl, Uri? deploymentUrl)
    {
        // Some URL parsers drop tab, LF and CR, so `ur\tl` could become a
        // second `url` in the IDE.
        if (deploymentUrl is null || appUrl.Any(char.IsControl) || appUrl.Split(Placeholder).Length != 2)
            return false;

        var handler = GetHandler(appUrl);
        if (handler is null) return false;

        var parameters = appUrl[(handler.Base.Length + handler.Path.IndexOfAny(['?', '#']) + 1)..];
        var entries = parameters.Split('&');
        var decoded = Uri.UnescapeDataString(parameters);
        // Substitution replaces the raw placeholder, so it must be the raw
        // `token` entry. JetBrains Gateway doesn't decode names, so they must
        // be literal. IDEs decode values twice, so check that view too and
        // reject leftovers. A later `#` would hide params from IDEs that read
        // only the query.
        return !parameters.Contains('#') &&
               entries.Contains($"token={Placeholder}") &&
               entries.All(entry => !entry.Split('=')[0].Contains('%')) &&
               !decoded.Contains('%') &&
               HasTrustedParams(parameters, deploymentUrl) &&
               HasTrustedParams(decoded, deploymentUrl);
    }

    /// <summary>True when the link opens a known Coder IDE handler.</summary>
    public static bool IsKnownHandler(string appUrl)
    {
        return GetHandler(appUrl) is not null;
    }

    private static Handler? GetHandler(string appUrl)
    {
        return Handlers.FirstOrDefault(h =>
            appUrl.Length >= h.Base.Length &&
            Ascii.EqualsIgnoreCase(appUrl.AsSpan(0, h.Base.Length), h.Base) &&
            appUrl.AsSpan(h.Base.Length).StartsWith(h.Path, StringComparison.Ordinal));
    }

    /// <summary>
    ///     True when <paramref name="url" /> is only an origin, optionally with a
    ///     trailing slash. A path could reach a workspace app on the deployment,
    ///     and userinfo can hide a different host from IDEs that decode it again.
    ///     Matched on the raw string, since parsing normalizes <c>/.</c> and drops
    ///     an empty <c>?</c>.
    /// </summary>
    public static bool IsBareOrigin(string url)
    {
        return BareOriginRegex().IsMatch(url) && Uri.TryCreate(url, UriKind.Absolute, out _);
    }

    [GeneratedRegex(@"^[a-z][a-z0-9+.-]*://[^/\\?#@\s]+/?\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BareOriginRegex();

    /// <summary>Every name appears once, <c>token</c> is the placeholder, <c>url</c> is the deployment.</summary>
    private static bool HasTrustedParams(string query, Uri deploymentUrl)
    {
        // Parsed like `URLSearchParams`.
        var parsed = query.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(entry => entry.Split('=', 2))
            .Select(pair => (Name: Decode(pair[0]), Value: pair.Length > 1 ? Decode(pair[1]) : ""))
            .ToList();
        var url = parsed.FirstOrDefault(p => p.Name == "url").Value;
        return parsed.DistinctBy(p => p.Name).Count() == parsed.Count &&
               parsed.FirstOrDefault(p => p.Name == "token").Value == Placeholder &&
               url is not null && IsBareOrigin(url) &&
               Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
               uri.Scheme is "http" or "https" &&
               Uri.Compare(uri, deploymentUrl, UriComponents.SchemeAndServer, UriFormat.UriEscaped,
                   StringComparison.OrdinalIgnoreCase) == 0;
    }

    private static string Decode(string value)
    {
        return Uri.UnescapeDataString(value.Replace('+', ' '));
    }
}
