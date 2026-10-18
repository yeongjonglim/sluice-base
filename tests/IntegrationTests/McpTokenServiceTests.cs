using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SluiceBase.Api.Data;
using SluiceBase.Api.Mcp;
using SluiceBase.Core.Mcp;
using SluiceBase.Core.Users;
using Testcontainers.PostgreSql;

namespace IntegrationTests;

/// <summary>
/// DB-backed unit tests for <see cref="McpTokenService"/>.
/// Uses Testcontainers to spin up a real Postgres instance — does NOT require the full Aspire stack.
/// </summary>
public sealed class McpTokenServiceTests : IAsyncLifetime
{
    // Pinned to the Postgres major version the app runs against (Aspire default / docker-compose postgres:17).
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17")
        .WithDatabase("sluice_test_mcp")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public async ValueTask InitializeAsync() => await _postgres.StartAsync();
    public async ValueTask DisposeAsync() => await _postgres.DisposeAsync();

    private AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options);

    private static McpTokenService CreateService(AppDbContext db, TimeProvider? clock = null)
    {
        var opts = Options.Create(new McpOptions
        {
            AccessTokenMinutes = 60,
            RefreshTokenDays = 30,
            AuthCodeSeconds = 120,
        });
        return new McpTokenService(db, clock ?? TimeProvider.System, opts);
    }

    private const string ClientId = "test-client-001";

    /// <summary>
    /// Seeds a real <see cref="User"/> row so FK constraints on <c>mcp_auth_code.user_id</c>
    /// and <c>mcp_token.user_id</c> are satisfied.
    /// </summary>
    private static async Task<UserId> SeedUserAsync(AppDbContext db, CancellationToken ct = default)
    {
        var user = User.Create("test@example.com", "Test User", DateTimeOffset.UtcNow);
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return user.Id;
    }

    private static async Task MigrateAsync(AppDbContext db)
    {
        await db.Database.MigrateAsync();
    }

    [Fact]
    public async Task IssueAndRedeem_WithCorrectVerifier_ReturnsTokens()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        await MigrateAsync(db);
        var svc = CreateService(db);

        var userId = await SeedUserAsync(db, ct);
        const string redirectUri = "https://example.com/callback";
        const string codeVerifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        var codeChallenge = TokenHasher.ComputePkceS256Challenge(codeVerifier);

        // Register a placeholder client so ClientId can be matched
        db.McpOAuthClients.Add(McpOAuthClient.Register("Test Client", [redirectUri], DateTimeOffset.UtcNow));
        await db.SaveChangesAsync(ct);

        var code = await svc.IssueAuthCodeAsync(ClientId, userId, redirectUri, codeChallenge, ct);
        var tokens = await svc.RedeemAuthCodeAsync(ClientId, code, redirectUri, codeVerifier, ct);

        Assert.NotNull(tokens);
        Assert.NotEmpty(tokens.AccessToken);
        Assert.NotEmpty(tokens.RefreshToken);
        Assert.Equal(3600, tokens.ExpiresInSeconds);
    }

    [Fact]
    public async Task RedeemAuthCode_WithWrongVerifier_ReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        await MigrateAsync(db);
        var svc = CreateService(db);

        var userId = await SeedUserAsync(db, ct);
        const string redirectUri = "https://example.com/callback";
        const string correctVerifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        const string wrongVerifier = "wrong-verifier-value-that-does-not-match-challenge";
        var codeChallenge = TokenHasher.ComputePkceS256Challenge(correctVerifier);

        var code = await svc.IssueAuthCodeAsync(ClientId, userId, redirectUri, codeChallenge, ct);
        var tokens = await svc.RedeemAuthCodeAsync(ClientId, code, redirectUri, wrongVerifier, ct);

        Assert.Null(tokens);
    }

    [Fact]
    public async Task RedeemAuthCode_ReplayAttack_SecondRedemptionReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        await MigrateAsync(db);
        var svc = CreateService(db);

        var userId = await SeedUserAsync(db, ct);
        const string redirectUri = "https://example.com/callback";
        const string codeVerifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        var codeChallenge = TokenHasher.ComputePkceS256Challenge(codeVerifier);

        var code = await svc.IssueAuthCodeAsync(ClientId, userId, redirectUri, codeChallenge, ct);

        // First redemption succeeds
        var first = await svc.RedeemAuthCodeAsync(ClientId, code, redirectUri, codeVerifier, ct);
        Assert.NotNull(first);

        // Second redemption (replay) must fail
        var second = await svc.RedeemAuthCodeAsync(ClientId, code, redirectUri, codeVerifier, ct);
        Assert.Null(second);
    }

    [Fact]
    public async Task Refresh_RotatesToken_OldRefreshBecomesInvalid()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        await MigrateAsync(db);
        var svc = CreateService(db);

        var userId = await SeedUserAsync(db, ct);
        const string redirectUri = "https://example.com/callback";
        const string codeVerifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        var codeChallenge = TokenHasher.ComputePkceS256Challenge(codeVerifier);

        var code = await svc.IssueAuthCodeAsync(ClientId, userId, redirectUri, codeChallenge, ct);
        var initial = await svc.RedeemAuthCodeAsync(ClientId, code, redirectUri, codeVerifier, ct);
        Assert.NotNull(initial);

        // Refresh to get new tokens
        var refreshed = await svc.RefreshAsync(ClientId, initial.RefreshToken, ct);
        Assert.NotNull(refreshed);
        Assert.NotEqual(initial.RefreshToken, refreshed.RefreshToken);
        Assert.NotEqual(initial.AccessToken, refreshed.AccessToken);

        // Old refresh token is now revoked — second use must fail
        var reuse = await svc.RefreshAsync(ClientId, initial.RefreshToken, ct);
        Assert.Null(reuse);
    }

    [Fact]
    public async Task ValidateAccessToken_ValidToken_ReturnsUserId()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        await MigrateAsync(db);
        var svc = CreateService(db);

        var userId = await SeedUserAsync(db, ct);
        const string redirectUri = "https://example.com/callback";
        const string codeVerifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        var codeChallenge = TokenHasher.ComputePkceS256Challenge(codeVerifier);

        var code = await svc.IssueAuthCodeAsync(ClientId, userId, redirectUri, codeChallenge, ct);
        var tokens = await svc.RedeemAuthCodeAsync(ClientId, code, redirectUri, codeVerifier, ct);
        Assert.NotNull(tokens);

        var validated = await svc.ValidateAccessTokenAsync(tokens.AccessToken, ct);

        Assert.NotNull(validated);
        Assert.Equal(userId.Value, validated.Value.Value);
    }

    [Fact]
    public async Task ValidateAccessToken_RevokedToken_ReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        await MigrateAsync(db);
        var svc = CreateService(db);

        var userId = await SeedUserAsync(db, ct);
        const string redirectUri = "https://example.com/callback";
        const string codeVerifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        var codeChallenge = TokenHasher.ComputePkceS256Challenge(codeVerifier);

        var code = await svc.IssueAuthCodeAsync(ClientId, userId, redirectUri, codeChallenge, ct);
        var tokens = await svc.RedeemAuthCodeAsync(ClientId, code, redirectUri, codeVerifier, ct);
        Assert.NotNull(tokens);

        // Revoke the access token directly via EF
        var accessHash = TokenHasher.Hash(tokens.AccessToken);
        var tokenRow = await db.McpTokens.SingleAsync(t => t.TokenHash == accessHash, ct);
        tokenRow.Revoke();
        await db.SaveChangesAsync(ct);

        var validated = await svc.ValidateAccessTokenAsync(tokens.AccessToken, ct);
        Assert.Null(validated);
    }

    [Fact]
    public async Task ValidateAccessToken_ExpiredToken_ReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        await MigrateAsync(db);

        // Use a clock that is already past the token expiry
        var frozenPast = new FrozenTimeProvider(DateTimeOffset.UtcNow.AddMinutes(-120));
        var svc = CreateService(db, frozenPast);

        var userId = await SeedUserAsync(db, ct);
        const string redirectUri = "https://example.com/callback";
        const string codeVerifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        var codeChallenge = TokenHasher.ComputePkceS256Challenge(codeVerifier);

        // Issue code and redeem — at this frozen time the code is still valid (issued at same time, +120s window)
        var code = await svc.IssueAuthCodeAsync(ClientId, userId, redirectUri, codeChallenge, ct);
        var tokens = await svc.RedeemAuthCodeAsync(ClientId, code, redirectUri, codeVerifier, ct);
        Assert.NotNull(tokens);

        // Now validate with "current" time 2 hours after token expiry (AccessTokenMinutes=60, so issued+60min=past)
        var futureService = CreateService(db, new FrozenTimeProvider(DateTimeOffset.UtcNow.AddHours(2)));
        var validated = await futureService.ValidateAccessTokenAsync(tokens.AccessToken, ct);
        Assert.Null(validated);
    }

    [Fact]
    public async Task HasLiveGrant_AuthCode_TrueOnlyWhileUnconsumedForSameClient()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        await MigrateAsync(db);
        var svc = CreateService(db);

        var userId = await SeedUserAsync(db, ct);
        const string redirectUri = "https://example.com/callback";
        const string codeVerifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        var code = await svc.IssueAuthCodeAsync(
            ClientId, userId, redirectUri, TokenHasher.ComputePkceS256Challenge(codeVerifier), ct);

        Assert.True(await svc.HasLiveGrantAsync("authorization_code", ClientId, code, ct));
        Assert.False(await svc.HasLiveGrantAsync("authorization_code", "other-client", code, ct));
        Assert.False(await svc.HasLiveGrantAsync("refresh_token", ClientId, code, ct));
        Assert.False(await svc.HasLiveGrantAsync("authorization_code", ClientId, "bogus", ct));

        var expired = CreateService(db, new FrozenTimeProvider(DateTimeOffset.UtcNow.AddHours(1)));
        Assert.False(await expired.HasLiveGrantAsync("authorization_code", ClientId, code, ct));

        Assert.NotNull(await svc.RedeemAuthCodeAsync(ClientId, code, redirectUri, codeVerifier, ct));
        Assert.False(await svc.HasLiveGrantAsync("authorization_code", ClientId, code, ct));
    }

    [Fact]
    public async Task HasLiveGrant_RefreshToken_TrueOnlyWhileUnrevokedAndDoesNotTrack()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        await MigrateAsync(db);
        var svc = CreateService(db);

        var userId = await SeedUserAsync(db, ct);
        const string redirectUri = "https://example.com/callback";
        const string codeVerifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        var code = await svc.IssueAuthCodeAsync(
            ClientId, userId, redirectUri, TokenHasher.ComputePkceS256Challenge(codeVerifier), ct);
        var tokens = await svc.RedeemAuthCodeAsync(ClientId, code, redirectUri, codeVerifier, ct);
        Assert.NotNull(tokens);
        db.ChangeTracker.Clear();

        Assert.True(await svc.HasLiveGrantAsync("refresh_token", ClientId, tokens.RefreshToken, ct));
        Assert.False(await svc.HasLiveGrantAsync("refresh_token", "other-client", tokens.RefreshToken, ct));
        Assert.False(await svc.HasLiveGrantAsync("refresh_token", ClientId, tokens.AccessToken, ct));
        Assert.Empty(db.ChangeTracker.Entries());

        Assert.NotNull(await svc.RefreshAsync(ClientId, tokens.RefreshToken, ct));
        Assert.False(await svc.HasLiveGrantAsync("refresh_token", ClientId, tokens.RefreshToken, ct));
    }

    /// <summary>Minimal <see cref="TimeProvider"/> that always returns the same instant.</summary>
    private sealed class FrozenTimeProvider(DateTimeOffset frozenAt) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => frozenAt;
    }
}
