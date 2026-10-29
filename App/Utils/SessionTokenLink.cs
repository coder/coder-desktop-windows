using System;
using System.Collections.Generic;
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

    /// <summary>
    ///     Returns why the link can't get a session token, or null if it has no
    ///     placeholder or its only <c>url</c> is <paramref name="signedInUrl" />
    ///     or <paramref name="dashboardUrl" />.
    /// </summary>
    public static string? BlockReason(string appUrl, Uri signedInUrl, Uri? dashboardUrl)
    {
        if (!appUrl.Contains(Placeholder)) return null;
        // Some parsers drop tab, LF and CR, so `ur\tl` could become `url`.
        if (appUrl.Any(char.IsControl) || appUrl.Split(Placeholder).Length != 2) return UntrustedUrlMessage;
        var handler = GetHandler(appUrl);
        if (handler is null) return UntrustedUrlMessage;

        var paramsStart = handler.Base.Length + handler.Path.IndexOfAny(['?', '#']) + 1;
        var parameters = appUrl[paramsStart..];
        var entries = parameters.Split('&');
        var decoded = Uri.UnescapeDataString(parameters);
        // The placeholder is replaced raw, so it must be the raw `token` entry,
        // and unique names make it the only `token`. Gateway doesn't decode
        // names, and IDEs decode values twice.
        var origin = LinkOrigin(parameters);
        if (parameters.Contains('#') ||
            !entries.Contains($"token={Placeholder}") ||
            entries.Any(entry => entry.Split('=')[0].Contains('%')) ||
            decoded.Contains('%') ||
            origin is null ||
            LinkOrigin(decoded) != origin)
            return UntrustedUrlMessage;

        return IsSameOrigin(origin, signedInUrl) || (dashboardUrl is not null && IsSameOrigin(origin, dashboardUrl))
            ? null
            : $"This app signs in to {origin}, but Coder Desktop is signed in to {signedInUrl}.";
    }

    private static Handler? GetHandler(string appUrl)
    {
        // Ascii.EqualsIgnoreCase only folds ASCII, so a Kelvin sign isn't `k`.
        return Handlers.FirstOrDefault(handler =>
            appUrl.Length >= handler.Base.Length &&
            Ascii.EqualsIgnoreCase(appUrl.AsSpan(0, handler.Base.Length), handler.Base) &&
            appUrl.AsSpan(handler.Base.Length).StartsWith(handler.Path, StringComparison.Ordinal));
    }

    /// <summary>
    ///     Returns the <c>url</c> param if every name is unique and <c>url</c> is
    ///     a bare http(s) origin. Matched raw, since parsing normalizes <c>/.</c>
    ///     and drops an empty <c>?</c>.
    /// </summary>
    private static string? LinkOrigin(string query)
    {
        var parameters = ParseQuery(query);
        var url = parameters.FirstOrDefault(param => param.Name == "url").Value;
        return parameters.DistinctBy(param => param.Name).Count() == parameters.Count &&
               url is not null &&
               Regex.IsMatch(url, @"^https?://[^/\\?#@\s]+/?\z", RegexOptions.IgnoreCase)
            ? url
            : null;
    }

    /// <summary>Parsed like <c>URLSearchParams</c>.</summary>
    private static List<(string Name, string Value)> ParseQuery(string query)
    {
        return query.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(entry => entry.Split('=', 2).Select(part => Uri.UnescapeDataString(part.Replace('+', ' '))).ToArray())
            .Select(parts => (parts[0], parts.ElementAtOrDefault(1) ?? ""))
            .ToList();
    }

    private static bool IsSameOrigin(string origin, Uri url)
    {
        return Uri.TryCreate(origin, UriKind.Absolute, out var originUri) &&
               Uri.Compare(originUri, url, UriComponents.SchemeAndServer, UriFormat.UriEscaped,
                   StringComparison.OrdinalIgnoreCase) == 0;
    }
}
