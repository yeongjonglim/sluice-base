using SluiceBase.Core.Users;
namespace SluiceBase.Core.Mcp;

public sealed class McpAuthCode
{
#pragma warning disable CS8618
    private McpAuthCode() { }
#pragma warning restore CS8618

    public McpAuthCodeId Id { get; private set; }
    public string CodeHash { get; private set; }
    public string ClientId { get; private set; }
    public UserId UserId { get; private set; }
    public string RedirectUri { get; private set; }
    public string CodeChallenge { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public bool Consumed { get; private set; }

    public static McpAuthCode Issue(string codeHash, string clientId, UserId userId,
        string redirectUri, string codeChallenge, DateTimeOffset expiresAt) => new()
    {
        Id = McpAuthCodeId.FromNewVersion7Guid(),
        CodeHash = codeHash, ClientId = clientId, UserId = userId,
        RedirectUri = redirectUri, CodeChallenge = codeChallenge, ExpiresAt = expiresAt,
    };

    public void Consume() => Consumed = true;
}
