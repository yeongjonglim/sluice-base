using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SluiceBase.Api.Data;
using SluiceBase.Core.Mcp;
using SluiceBase.Core.Users;

namespace SluiceBase.Api.Mcp;

internal sealed record IssuedTokens(string AccessToken, string RefreshToken, int ExpiresInSeconds);

internal interface IMcpTokenService
{
    Task<string> IssueAuthCodeAsync(string clientId, UserId userId, string redirectUri, string codeChallenge, CancellationToken ct);
    Task<IssuedTokens?> RedeemAuthCodeAsync(string clientId, string code, string redirectUri, string codeVerifier, CancellationToken ct);
    Task<IssuedTokens?> RefreshAsync(string clientId, string refreshToken, CancellationToken ct);
    Task<UserId?> ValidateAccessTokenAsync(string accessToken, CancellationToken ct);
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

        authCode.Consume();
        var tokens = await IssueTokenPairAsync(authCode.UserId, clientId, ct);
        return tokens;
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

        // Rotate: revoke old refresh token and issue a fresh pair
        token.Revoke();
        var tokens = await IssueTokenPairAsync(token.UserId, clientId, ct);
        return tokens;
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

        token.Touch(now);
        await db.SaveChangesAsync(ct);
        return token.UserId;
    }

    private async Task<IssuedTokens> IssueTokenPairAsync(UserId userId, string clientId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();

        var rawAccess = TokenHasher.Generate();
        var rawRefresh = TokenHasher.Generate();

        var accessToken = McpToken.Issue(
            TokenHasher.Hash(rawAccess),
            McpTokenType.Access,
            userId,
            clientId,
            now,
            now.AddMinutes(Options.AccessTokenMinutes));

        var refreshToken = McpToken.Issue(
            TokenHasher.Hash(rawRefresh),
            McpTokenType.Refresh,
            userId,
            clientId,
            now,
            now.AddDays(Options.RefreshTokenDays));

        db.McpTokens.Add(accessToken);
        db.McpTokens.Add(refreshToken);
        await db.SaveChangesAsync(ct);

        return new IssuedTokens(rawAccess, rawRefresh, Options.AccessTokenMinutes * 60);
    }
}
