namespace SluiceBase.Core.Mcp;

public sealed class McpOAuthClient
{
#pragma warning disable CS8618
    private McpOAuthClient() { }
#pragma warning restore CS8618

    public McpOAuthClientId Id { get; private set; }
    public string ClientId { get; private set; }
    public string ClientName { get; private set; }
    public List<string> RedirectUris { get; private set; } = [];
    public DateTimeOffset CreatedAt { get; private set; }

    public static McpOAuthClient Register(string clientName, IEnumerable<string> redirectUris, DateTimeOffset at) => new()
    {
        Id = McpOAuthClientId.FromNewVersion7Guid(),
        ClientId = Guid.NewGuid().ToString("N"),
        ClientName = clientName,
        RedirectUris = redirectUris.ToList(),
        CreatedAt = at,
    };
}
