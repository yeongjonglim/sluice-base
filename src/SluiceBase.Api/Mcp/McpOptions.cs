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

    [GeneratedRegex("^[A-Za-z0-9_-]+$")]
    private static partial Regex ServerNameRegex();
}

internal static partial class McpLoggerMessage
{
    [LoggerMessage(
        LogLevel.Warning,
        Message = "Mcp:ServerName '{ServerName}' is not a valid identifier (allowed: letters, digits, '-', '_'). Falling back to 'sluicebase'.")]
    public static partial void WarningInvalidServerName(this ILogger logger, string serverName);
}
