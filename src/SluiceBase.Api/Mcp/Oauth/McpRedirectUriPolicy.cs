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
            "http" => uri.IsLoopback,
            "https" => !string.IsNullOrEmpty(uri.Host),
            "javascript" or "data" or "file" or "vbscript" => false,
            _ => true,
        };
    }

    public static bool IsLoopback(string redirectUri) =>
        TryParseSafe(redirectUri, out var uri) && uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback;

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
            || !string.IsNullOrEmpty(parsed.Fragment))
        {
            return false;
        }

        uri = parsed;
        return true;
    }
}
