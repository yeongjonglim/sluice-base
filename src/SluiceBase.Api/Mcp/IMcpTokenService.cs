using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SluiceBase.Api.Data;
using SluiceBase.Api.Mcp.Oauth;
using SluiceBase.Core.Mcp;
using SluiceBase.Core.Users;

namespace SluiceBase.Api.Mcp;

internal sealed record IssuedTokens(string AccessToken, string RefreshToken, int ExpiresInSeconds);

internal sealed record LiveGrant(bool IsLoopback);

internal sealed record McpGrantSummary(
    McpGrantId Id,
    string ClientId,
    bool IsLoopback,
    DateTimeOffset SignedInAt,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset ExpiresAt);

internal interface IMcpTokenService
{
    Task<string> IssueAuthCodeAsync(string clientId, UserId userId, string redirectUri, string codeChallenge, CancellationToken ct);
    Task<IssuedTokens?> RedeemAuthCodeAsync(string clientId, string code, string redirectUri, string codeVerifier, CancellationToken ct);
    Task<IssuedTokens?> RefreshAsync(string clientId, string refreshToken, CancellationToken ct);
    Task<UserId?> ValidateAccessTokenAsync(string accessToken, CancellationToken ct);
    Task<LiveGrant?> FindLiveAuthCodeAsync(string clientId, string code, CancellationToken ct);
    Task<LiveGrant?> FindLiveRefreshTokenAsync(string clientId, string refreshToken, CancellationToken ct);

    // Called when a refresh token is not live. A rotated refresh token presented again means it leaked, so the
    // whole grant is revoked (RFC 9700 §4.14.2).
    Task RevokeGrantIfRefreshTokenReusedAsync(string clientId, string refreshToken, CancellationToken ct);

    // RFC 7009: revoking a refresh token ends its grant; revoking an access token ends only that token.
    Task RevokeTokenAsync(string clientId, string token, CancellationToken ct);

    Task<IReadOnlyList<McpGrantSummary>> ListGrantsAsync(UserId userId, CancellationToken ct);
    Task<bool> RevokeGrantAsync(UserId userId, McpGrantId grantId, CancellationToken ct);
    Task RevokeAllAsync(UserId userId, CancellationToken ct);
}

internal sealed class McpTokenService(AppDbContext db, TimeProvider clock, IOptions<McpOptions> options) : IMcpTokenService
{
    private McpOptions Options => options.Value;

    public async Task<string> IssueAuthCodeAsync(string clientId, UserId userId, string redirectUri, string codeChallenge, CancellationToken ct)
    {
        var code = TokenHasher.Generate();
        var authCode = McpAuthCode.Issue(
            TokenHasher.Hash(code),
            clientId,
            userId,
            redirectUri,
            codeChallenge,
            clock.GetUtcNow().AddSeconds(Options.AuthCodeSeconds));
        db.McpAuthCodes.Add(authCode);
        await db.SaveChangesAsync(ct);
        return code;
    }

    public async Task<IssuedTokens?> RedeemAuthCodeAsync(string clientId, string code, string redirectUri, string codeVerifier, CancellationToken ct)
    {
        var codeHash = TokenHasher.Hash(code);
        var authCode = await db.McpAuthCodes
            .FirstOrDefaultAsync(c => c.CodeHash == codeHash, ct);

        if (authCode is null)
        {
            return null;
        }

        var now = clock.GetUtcNow();

        // Validate all conditions
        if (authCode.Consumed)
        {
            return null;
        }

        if (authCode.ExpiresAt <= now)
        {
            return null;
        }

        if (authCode.ClientId != clientId)
        {
            return null;
        }

        if (authCode.RedirectUri != redirectUri)
        {
            return null;
        }

        // PKCE S256 check using constant-time comparison
        var computedChallenge = TokenHasher.ComputePkceS256Challenge(codeVerifier);
        var computedBytes = Encoding.UTF8.GetBytes(computedChallenge);
        var storedBytes = Encoding.UTF8.GetBytes(authCode.CodeChallenge);
        if (!CryptographicOperations.FixedTimeEquals(computedBytes, storedBytes))
        {
            return null;
        }

        if (await IsDisabledAsync(authCode.UserId, ct))
        {
            return null;
        }

        // Single-use: mark consumed. The Consume() mutation and the new token rows are flushed
        // together by the single SaveChangesAsync inside IssueTokenPairAsync (one unit of work).
        authCode.Consume();
        return await TryIssueTokenPairAsync(
            authCode.UserId, clientId, McpRedirectUriPolicy.IsLoopback(authCode.RedirectUri),
            McpGrantId.FromNewVersion7Guid(), now.AddDays(Options.MaxGrantDays), ct);
    }

    public async Task<IssuedTokens?> RefreshAsync(string clientId, string refreshToken, CancellationToken ct)
    {
        var tokenHash = TokenHasher.Hash(refreshToken);
        var token = await db.McpTokens
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

        if (token is null)
        {
            return null;
        }

        var now = clock.GetUtcNow();

        if (token.Revoked)
        {
            return null;
        }

        if (token.ExpiresAt <= now)
        {
            return null;
        }

        if (token.Type != McpTokenType.Refresh)
        {
            return null;
        }

        if (token.ClientId != clientId)
        {
            return null;
        }

        if (token.GrantExpiresAt <= now || await IsDisabledAsync(token.UserId, ct))
        {
            return null;
        }

        // Rotate: revoke old refresh token and issue a fresh pair
        token.Revoke();
        return await TryIssueTokenPairAsync(
            token.UserId, clientId, token.IsLoopbackGrant, token.GrantId, token.GrantExpiresAt, ct);
    }

    public async Task<UserId?> ValidateAccessTokenAsync(string accessToken, CancellationToken ct)
    {
        var tokenHash = TokenHasher.Hash(accessToken);
        var token = await db.McpTokens
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

        if (token is null)
        {
            return null;
        }

        var now = clock.GetUtcNow();

        if (token.Revoked)
        {
            return null;
        }

        if (token.ExpiresAt <= now)
        {
            return null;
        }

        if (token.Type != McpTokenType.Access)
        {
            return null;
        }

        if (await IsDisabledAsync(token.UserId, ct))
        {
            return null;
        }

        token.Touch(now);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Revoked between the read and the save.
            return null;
        }

        return token.UserId;
    }

    public async Task<LiveGrant?> FindLiveAuthCodeAsync(string clientId, string code, CancellationToken ct)
    {
        var hash = TokenHasher.Hash(code);
        var now = clock.GetUtcNow();
        var redirectUri = await db.McpAuthCodes.AsNoTracking()
            .Where(c => c.CodeHash == hash && c.ClientId == clientId && !c.Consumed && c.ExpiresAt > now)
            .Select(c => c.RedirectUri)
            .FirstOrDefaultAsync(ct);
        return redirectUri is null ? null : new LiveGrant(McpRedirectUriPolicy.IsLoopback(redirectUri));
    }

    public async Task<LiveGrant?> FindLiveRefreshTokenAsync(string clientId, string refreshToken, CancellationToken ct)
    {
        var hash = TokenHasher.Hash(refreshToken);
        var now = clock.GetUtcNow();
        var isLoopback = await db.McpTokens.AsNoTracking()
            .Where(t => t.TokenHash == hash && t.ClientId == clientId && t.Type == McpTokenType.Refresh
                && !t.Revoked && t.ExpiresAt > now && t.GrantExpiresAt > now)
            .Select(t => (bool?)t.IsLoopbackGrant)
            .FirstOrDefaultAsync(ct);
        return isLoopback is null ? null : new LiveGrant(isLoopback.Value);
    }

    public async Task RevokeGrantIfRefreshTokenReusedAsync(string clientId, string refreshToken, CancellationToken ct)
    {
        var hash = TokenHasher.Hash(refreshToken);
        var reused = await db.McpTokens.AsNoTracking()
            .Where(t => t.TokenHash == hash && t.ClientId == clientId && t.Type == McpTokenType.Refresh && t.Revoked)
            .Select(t => new { t.UserId, t.GrantId })
            .FirstOrDefaultAsync(ct);
        if (reused is not null)
        {
            await RevokeWhereAsync(t => t.UserId == reused.UserId && t.GrantId == reused.GrantId, ct);
        }
    }

    public async Task RevokeTokenAsync(string clientId, string token, CancellationToken ct)
    {
        var hash = TokenHasher.Hash(token);
        var found = await db.McpTokens.AsNoTracking()
            .Where(t => t.TokenHash == hash && t.ClientId == clientId)
            .Select(t => new { t.Id, t.Type, t.UserId, t.GrantId })
            .FirstOrDefaultAsync(ct);
        if (found is null)
        {
            return;
        }

        if (found.Type == McpTokenType.Refresh)
        {
            await RevokeWhereAsync(t => t.UserId == found.UserId && t.GrantId == found.GrantId, ct);
        }
        else
        {
            await RevokeWhereAsync(t => t.Id == found.Id, ct);
        }
    }

    public async Task<IReadOnlyList<McpGrantSummary>> ListGrantsAsync(UserId userId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var grants = await db.McpTokens.AsNoTracking()
            .Where(t => t.UserId == userId && !t.Revoked && t.ExpiresAt > now)
            .GroupBy(t => new { t.GrantId, t.ClientId, t.IsLoopbackGrant })
            .Select(g => new McpGrantSummary(
                g.Key.GrantId,
                g.Key.ClientId,
                g.Key.IsLoopbackGrant,
                g.Min(t => t.CreatedAt),
                g.Max(t => t.LastUsedAt),
                g.Max(t => t.ExpiresAt)))
            .ToListAsync(ct);
        return [.. grants.OrderByDescending(g => g.LastUsedAt ?? g.SignedInAt)];
    }

    public async Task<bool> RevokeGrantAsync(UserId userId, McpGrantId grantId, CancellationToken ct) =>
        await RevokeWhereAsync(t => t.UserId == userId && t.GrantId == grantId, ct) > 0;

    public async Task RevokeAllAsync(UserId userId, CancellationToken ct)
    {
        await RevokeWhereAsync(t => t.UserId == userId, ct);
        await db.McpAuthCodes
            .Where(c => c.UserId == userId && !c.Consumed)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Consumed, true), ct);
    }

    private Task<int> RevokeWhereAsync(System.Linq.Expressions.Expression<Func<McpToken, bool>> predicate, CancellationToken ct) =>
        db.McpTokens.Where(predicate).Where(t => !t.Revoked)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Revoked, true), ct);

    private Task<bool> IsDisabledAsync(UserId userId, CancellationToken ct) =>
        db.Users.AnyAsync(u => u.Id == userId && u.DisabledAt != null, ct);

    // Saves the consumed code or rotated refresh token together with the new pair. A concurrency conflict means
    // another request redeemed the same code or refresh token first, so this one gets nothing.
    private async Task<IssuedTokens?> TryIssueTokenPairAsync(UserId userId, string clientId, bool isLoopbackGrant,
        McpGrantId grantId, DateTimeOffset grantExpiresAt, CancellationToken ct)
    {
        try
        {
            return await IssueTokenPairAsync(userId, clientId, isLoopbackGrant, grantId, grantExpiresAt, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return null;
        }
    }

    private async Task<IssuedTokens> IssueTokenPairAsync(UserId userId, string clientId, bool isLoopbackGrant,
        McpGrantId grantId, DateTimeOffset grantExpiresAt, CancellationToken ct)
    {
        var now = clock.GetUtcNow();

        var rawAccess = TokenHasher.Generate();
        var rawRefresh = TokenHasher.Generate();

        var accessToken = McpToken.Issue(
            TokenHasher.Hash(rawAccess),
            McpTokenType.Access,
            userId,
            clientId,
            isLoopbackGrant,
            grantId,
            grantExpiresAt,
            now,
            now.AddMinutes(Options.AccessTokenMinutes));

        var refreshToken = McpToken.Issue(
            TokenHasher.Hash(rawRefresh),
            McpTokenType.Refresh,
            userId,
            clientId,
            isLoopbackGrant,
            grantId,
            grantExpiresAt,
            now,
            now.AddDays(Options.RefreshTokenDays));

        db.McpTokens.Add(accessToken);
        db.McpTokens.Add(refreshToken);
        await db.SaveChangesAsync(ct);

        return new IssuedTokens(rawAccess, rawRefresh, (int)(accessToken.ExpiresAt - now).TotalSeconds);
    }
}
