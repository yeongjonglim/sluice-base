using System.Text.Json;
using SluiceBase.Api.Mcp.Oauth;

namespace SluiceBase.Api.Mcp.Clients;

internal static class CimdDocument
{
    public static bool IsAcceptableClientIdUrl(string clientId, out Uri url)
    {
        url = null!;

        if (!clientId.StartsWith("https://", StringComparison.Ordinal)
            // Non-ASCII is refused so a homograph host (Cyrillic 'а' in chаtgpt.com) can't pose as
            // a real one on the consent page; punycode (xn--) ids are still accepted and shown as such.
            || clientId.Any(c => c <= 0x20 || c >= 0x7F || c == '\\')
            || !Uri.TryCreate(clientId, UriKind.Absolute, out var parsed)
            || parsed.Scheme != Uri.UriSchemeHttps
            || string.IsNullOrEmpty(parsed.Host)
            || !string.IsNullOrEmpty(parsed.UserInfo)
            || clientId.Contains('#')
            || parsed.AbsolutePath == "/"
            || HasDotSegment(clientId))
        {
            return false;
        }

        url = parsed;
        return true;
    }

    public static ResolvedClient? Validate(Uri clientIdUrl, JsonElement document)
    {
        if (document.ValueKind != JsonValueKind.Object
            || document.TryGetProperty("client_secret", out _)
            || document.TryGetProperty("client_secret_expires_at", out _)
            || !document.TryGetProperty("client_id", out var id)
            || id.ValueKind != JsonValueKind.String
            || !string.Equals(id.GetString(), clientIdUrl.OriginalString, StringComparison.Ordinal)
            || !AllowsNoneAuth(document)
            || !document.TryGetProperty("redirect_uris", out var uris)
            || uris.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var redirectUris = uris.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString()!)
            .Where(McpRedirectUriPolicy.IsAllowedForCimd)
            .ToList();
        if (redirectUris.Count == 0)
        {
            return null;
        }

        var name = document.TryGetProperty("client_name", out var n) && n.ValueKind == JsonValueKind.String
            ? n.GetString()
            : null;

        return new ResolvedClient(
            clientIdUrl.OriginalString,
            string.IsNullOrEmpty(name) ? clientIdUrl.IdnHost : name,
            clientIdUrl.IdnHost,
            redirectUris,
            ClientKind.Cimd);
    }

    // The plural list states every method the client can use, so it wins over the singular
    // preference: ChatGPT prefers private_key_jwt but also lists "none".
    private static bool AllowsNoneAuth(JsonElement document)
    {
        if (document.TryGetProperty("token_endpoint_auth_methods_supported", out var supported))
        {
            return supported.ValueKind == JsonValueKind.Array
                && supported.EnumerateArray().Any(e => e.ValueKind == JsonValueKind.String && e.GetString() == "none");
        }

        return !document.TryGetProperty("token_endpoint_auth_method", out var method)
            || (method.ValueKind == JsonValueKind.String && method.GetString() == "none");
    }

    // Uri normalises dot segments away, so the raw string is the only place they are visible.
    private static bool HasDotSegment(string raw)
    {
        var start = raw.IndexOf("://", StringComparison.Ordinal) + 3;
        var pathStart = raw.IndexOf('/', start);
        if (pathStart < 0)
        {
            return false;
        }

        var pathEnd = raw.IndexOfAny(['?', '#'], pathStart);
        var path = pathEnd < 0 ? raw[pathStart..] : raw[pathStart..pathEnd];
        return path.Split('/').Any(s => s is "." or ".."
            || s.Equals("%2e", StringComparison.OrdinalIgnoreCase)
            || s.Equals("%2e%2e", StringComparison.OrdinalIgnoreCase)
            || s.Equals(".%2e", StringComparison.OrdinalIgnoreCase)
            || s.Equals("%2e.", StringComparison.OrdinalIgnoreCase));
    }
}
