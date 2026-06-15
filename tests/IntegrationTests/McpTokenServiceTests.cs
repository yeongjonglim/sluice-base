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
    // Image tag sourced from https://github.com/microsoft/aspire/blob/main/src/Aspire.Hosting.PostgreSQL/PostgresContainerImageTags.cs
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.3")
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

    private static UserId SampleUserId() => UserId.From(Guid.NewGuid());
    private const string ClientId = "test-client-001";

    /// <summary>
    /// Seeds the MCP OAuth client row required by FK constraints (if any).
    /// The service itself does not enforce FK — we skip seeding and rely on EF's schema.
    /// </summary>
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

        var userId = SampleUserId();
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

        var userId = SampleUserId();
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

        var userId = SampleUserId();
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

        var userId = SampleUserId();
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

        var userId = SampleUserId();
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

        var userId = SampleUserId();
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

        var userId = SampleUserId();
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

    /// <summary>Minimal <see cref="TimeProvider"/> that always returns the same instant.</summary>
    private sealed class FrozenTimeProvider(DateTimeOffset frozenAt) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => frozenAt;
    }
}
