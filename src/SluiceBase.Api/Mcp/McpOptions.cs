using System.Text.RegularExpressions;

namespace SluiceBase.Api.Mcp;

internal sealed partial class McpOptions
{
    public const string SectionName = "Mcp";
    public bool Enabled { get; set; } = true;
    public string ServerName { get; set; } = "sluicebase";
    public int AccessTokenMinutes { get; set; } = 60;
    public int RefreshTokenDays { get; set; } = 30;
    public int AuthCodeSeconds { get; set; } = 120;

    public string? PublicBaseUrl { get; set; }

    public List<McpConfiguredClient> Clients { get; set; } = [];

    public McpCimdOptions Cimd { get; set; } = new();

    // The server name becomes a client-side alias used verbatim in a TOML table
    // name ([mcp_servers.<name>]) and JSON keys, so it must be a safe identifier.
    public string GetValidatedServerName(ILogger? logger = null)
    {
        if (!string.IsNullOrEmpty(ServerName) && ServerNameRegex().IsMatch(ServerName))
        {
            return ServerName;
        }

        logger?.WarningInvalidServerName(ServerName);
        return "sluicebase";
    }

    public string? GetValidatedPublicBaseUrl(ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(PublicBaseUrl))
        {
            return null;
        }

        if (Uri.TryCreate(PublicBaseUrl, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && string.IsNullOrEmpty(uri.UserInfo)
            && string.IsNullOrEmpty(uri.Query)
            && string.IsNullOrEmpty(uri.Fragment)
            && (uri.AbsolutePath == "/" || uri.AbsolutePath.Length == 0))
        {
            return uri.GetLeftPart(UriPartial.Authority);
        }

        logger?.WarningInvalidPublicBaseUrl(PublicBaseUrl);
        return null;
    }

    public IReadOnlyList<McpConfiguredClient> GetValidatedClients(ILogger? logger = null)
    {
        var valid = new List<McpConfiguredClient>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var client in Clients)
        {
            if (string.IsNullOrWhiteSpace(client.ClientId))
            {
                logger?.WarningInvalidConfiguredClient(client.Name, "ClientId is empty");
            }
            else if (client.RedirectUris is not { Count: > 0 })
            {
                logger?.WarningInvalidConfiguredClient(client.ClientId, "no RedirectUris");
            }
            else if (client.RedirectUris.Any(u => !Uri.TryCreate(u, UriKind.Absolute, out _)))
            {
                logger?.WarningInvalidConfiguredClient(client.ClientId, "a RedirectUri is not an absolute URI");
            }
            else if (!seen.Add(client.ClientId))
            {
                logger?.WarningInvalidConfiguredClient(client.ClientId, "duplicate ClientId");
            }
            else
            {
                valid.Add(client);
            }
        }

        return valid;
    }

    [GeneratedRegex("^[A-Za-z0-9_-]+$")]
    private static partial Regex ServerNameRegex();
}

internal sealed class McpCimdOptions
{
    // Lets the integration tests fetch canned documents from a loopback stub; startup refuses it
    // outside Development so SSRF protection can't be weakened in a real deployment.
    public bool AllowLoopbackForTesting { get; set; }
}

internal sealed class McpConfiguredClient
{
    public string ClientId { get; set; } = "";
    public string Name { get; set; } = "";
    public List<string> RedirectUris { get; set; } = [];
}

internal static partial class McpLoggerMessage
{
    [LoggerMessage(
        LogLevel.Warning,
        Message = "Mcp:ServerName '{ServerName}' is not a valid identifier (allowed: letters, digits, '-', '_'). Falling back to 'sluicebase'.")]
    public static partial void WarningInvalidServerName(this ILogger logger, string serverName);

    [LoggerMessage(
        LogLevel.Warning,
        Message = "Mcp:PublicBaseUrl '{PublicBaseUrl}' is not a valid origin (expected scheme://host[:port] with no path, query, fragment or user info). Ignoring it.")]
    public static partial void WarningInvalidPublicBaseUrl(this ILogger logger, string publicBaseUrl);

    [LoggerMessage(
        LogLevel.Warning,
        Message = "Mcp:PublicBaseUrl is not set; the OAuth issuer is derived from request headers. Set it when running behind a reverse proxy.")]
    public static partial void WarningPublicBaseUrlNotSet(this ILogger logger);

    [LoggerMessage(
        LogLevel.Warning,
        Message = "Mcp:Clients entry '{ClientId}' ignored: {Reason}.")]
    public static partial void WarningInvalidConfiguredClient(this ILogger logger, string clientId, string reason);
}
