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
            logger?.WarningPublicBaseUrlNotSet();
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

    [GeneratedRegex("^[A-Za-z0-9_-]+$")]
    private static partial Regex ServerNameRegex();
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
}
