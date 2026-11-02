using System.Net;

namespace SluiceBase.Api.Mcp.Oauth;

/// <summary>
/// Decides which redirect URIs a CIMD client document may list, and how a requested redirect URI
/// matches a registered one. Loopback URIs (RFC 8252 §7.3) match on any port, since native clients
/// bind an ephemeral port per sign-in.
/// </summary>
internal static class McpRedirectUriPolicy
{
    /// <summary>
    /// Whether a requested redirect URI matches a registered one. Loopback URIs match on any port,
    /// since native clients bind an ephemeral port per sign-in (RFC 8252 §7.3).
    /// </summary>
    public static bool Matches(string registered, string requested)
    {
        if (string.Equals(registered, requested, StringComparison.Ordinal))
        {
            return true;
        }

        return IsLoopback(registered) && IsLoopback(requested)
            && Uri.TryCreate(registered, UriKind.Absolute, out var r)
            && Uri.TryCreate(requested, UriKind.Absolute, out var q)
            && string.Equals(r.Host, q.Host, StringComparison.OrdinalIgnoreCase)
            && string.Equals(r.PathAndQuery, q.PathAndQuery, StringComparison.Ordinal);
    }

    public static bool IsAllowedForCimd(string redirectUri)
    {
        // Non-ASCII would let a homograph host pass for a familiar one on the consent page.
        if (redirectUri.Any(c => c > 0x7E) || !TryParseSafe(redirectUri, out var uri))
        {
            return false;
        }

        return uri.Scheme switch
        {
            "http" => IsLoopbackHost(uri),
            "https" => !string.IsNullOrEmpty(uri.Host),
            "javascript" or "data" or "file" or "vbscript" => false,
            _ => true,
        };
    }

    public static bool IsLoopback(string redirectUri) =>
        TryParseSafe(redirectUri, out var uri) && uri.Scheme == Uri.UriSchemeHttp && IsLoopbackHost(uri);

    // Browsers only treat `localhost` and loopback IP literals as local. Uri.IsLoopback also
    // accepts the name `loopback`, which a browser resolves through DNS like any other host.
    private static bool IsLoopbackHost(Uri uri) => uri.HostNameType switch
    {
        UriHostNameType.IPv4 or UriHostNameType.IPv6 =>
            IPAddress.TryParse(uri.Host.Trim('[', ']'), out var ip) && IPAddress.IsLoopback(ip),
        UriHostNameType.Dns => uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase),
        _ => false,
    };

    private static bool TryParseSafe(string redirectUri, out Uri uri)
    {
        uri = null!;

        // Backslashes, userinfo and control characters are where URI parsers disagree about the
        // host; reject them rather than risk .NET and the browser resolving different hosts.
        if (redirectUri.Contains('\\') || redirectUri.Any(char.IsControl) || redirectUri.Any(char.IsWhiteSpace))
        {
            return false;
        }

        if (!Uri.TryCreate(redirectUri, UriKind.Absolute, out var parsed)
            || !string.IsNullOrEmpty(parsed.UserInfo)
            || !string.IsNullOrEmpty(parsed.Fragment)
            || !string.Equals(parsed.Host, WrittenHost(redirectUri), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        uri = parsed;
        return true;
    }

    // The host exactly as the client wrote it. Uri rewrites some hosts (`loopback` becomes
    // `localhost`, `127.1` becomes `127.0.0.1`), and the browser follows the written one, so a
    // rewritten host is rejected rather than validated in a form the browser never uses.
    private static string WrittenHost(string redirectUri)
    {
        var separator = redirectUri.IndexOf("://", StringComparison.Ordinal);
        if (separator < 0)
        {
            return "";
        }

        var start = separator + 3;
        var end = redirectUri.IndexOfAny(['/', '?', '#'], start);
        var authority = end < 0 ? redirectUri[start..] : redirectUri[start..end];
        if (authority.StartsWith('['))
        {
            var close = authority.IndexOf(']');
            return close < 0 ? authority : authority[..(close + 1)];
        }

        var colon = authority.LastIndexOf(':');
        return colon < 0 ? authority : authority[..colon];
    }
}
