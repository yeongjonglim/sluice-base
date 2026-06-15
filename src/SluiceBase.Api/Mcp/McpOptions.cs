namespace SluiceBase.Api.Mcp;

internal sealed class McpOptions
{
    public const string SectionName = "Mcp";
    public bool Enabled { get; set; } = true;
    public int AccessTokenMinutes { get; set; } = 60;
    public int RefreshTokenDays { get; set; } = 30;
    public int AuthCodeSeconds { get; set; } = 120;
}
