using SluiceBase.Core.Mcp;

namespace SluiceBase.Api.Mcp.Oauth;

/// <summary>
/// Decides which redirect URIs an MCP OAuth client may use. Registration is anonymous, so a
/// redirect URI must be one that cannot deliver a code to a remote party: the user's own loopback
/// interface (RFC 8252 §7.3), a native app's private-use scheme, or an operator-approved URI.
/// </summary>
internal static class McpRedirectUriPolicy
{
    // Delivered by the OS to the app on the user's own machine, so a remote attacker can't collect it.
    private static readonly HashSet<string> NativeAppCallbacks = new(StringComparer.Ordinal)
    {
        "cursor://anysphere.cursor-mcp/oauth/callback",
    };

    public static bool IsAllowed(string redirectUri, IReadOnlyCollection<string> operatorAllowlist) =>
        NativeAppCallbacks.Contains(redirectUri)
        || operatorAllowlist.Contains(redirectUri, StringComparer.Ordinal)
        || IsLoopback(redirectUri);

    /// <summary>Whether a client may hold tokens: it has redirect URIs and every one is allowed.</summary>
    public static bool IsConforming(McpOAuthClient client, IReadOnlyCollection<string> operatorAllowlist) =>
        client.RedirectUris.Count > 0 && client.RedirectUris.All(u => IsAllowed(u, operatorAllowlist));

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

    private static bool IsLoopback(string redirectUri)
    {
        // Backslashes, userinfo and control characters are where URI parsers disagree about the
        // host; reject them rather than risk .NET and the browser resolving different hosts.
        if (redirectUri.Contains('\\') || redirectUri.Any(char.IsControl) || redirectUri.Any(char.IsWhiteSpace))
        {
            return false;
        }

        return Uri.TryCreate(redirectUri, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttp
            && uri.IsLoopback
            && string.IsNullOrEmpty(uri.UserInfo)
            && string.IsNullOrEmpty(uri.Fragment);
    }
}
