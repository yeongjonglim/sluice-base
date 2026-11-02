using SluiceBase.Core.Users;
namespace SluiceBase.Core.Mcp;

public sealed class McpToken
{
#pragma warning disable CS8618
    private McpToken() { }
#pragma warning restore CS8618

    public McpTokenId Id { get; private set; }
    public string TokenHash { get; private set; }
    public McpTokenType Type { get; private set; }
    public UserId UserId { get; private set; }
    public string ClientId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? LastUsedAt { get; private set; }
    public bool Revoked { get; private set; }

    // The sign-in behind this token went to a loopback callback, so its client is never resolved.
    public bool IsLoopbackGrant { get; private set; }

    // Every token issued from one sign-in shares a grant. Refreshing keeps it, revoking it ends the whole session,
    // and no token in it outlives GrantExpiresAt however often it is refreshed.
    public McpGrantId GrantId { get; private set; }
    public DateTimeOffset GrantExpiresAt { get; private set; }

    public static McpToken Issue(string tokenHash, McpTokenType type, UserId userId, string clientId,
        bool isLoopbackGrant, McpGrantId grantId, DateTimeOffset grantExpiresAt, DateTimeOffset createdAt,
        DateTimeOffset expiresAt) => new()
    {
        Id = McpTokenId.FromNewVersion7Guid(),
        TokenHash = tokenHash, Type = type, UserId = userId, ClientId = clientId,
        IsLoopbackGrant = isLoopbackGrant, GrantId = grantId, GrantExpiresAt = grantExpiresAt,
        CreatedAt = createdAt, ExpiresAt = expiresAt < grantExpiresAt ? expiresAt : grantExpiresAt,
    };

    public void Touch(DateTimeOffset at) => LastUsedAt = at;
    public void Revoke() => Revoked = true;
}
