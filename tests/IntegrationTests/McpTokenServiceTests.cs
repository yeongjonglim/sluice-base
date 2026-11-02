using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SluiceBase.Api.Data;
using SluiceBase.Api.Mcp;
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

    // Both contexts read the code before either saves, which is the interleaving a concurrent replay produces.
    private const string Verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";

    private static async Task<IssuedTokens> SignInAsync(McpTokenService svc, UserId userId, CancellationToken ct)
    {
        var code = await svc.IssueAuthCodeAsync(
            ClientId, userId, "https://example.com/callback", TokenHasher.ComputePkceS256Challenge(Verifier), ct);
        return (await svc.RedeemAuthCodeAsync(ClientId, code, "https://example.com/callback", Verifier, ct))!;
    }

    [Fact]
    public async Task Refresh_ReusingRotatedToken_RevokesTheWholeGrant()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        await MigrateAsync(db);
        var svc = CreateService(db);
        var userId = await SeedUserAsync(db, ct);
        var first = await SignInAsync(svc, userId, ct);
        var second = (await svc.RefreshAsync(ClientId, first.RefreshToken, ct))!;
        var other = await SignInAsync(svc, userId, ct);

        await svc.RevokeGrantIfRefreshTokenReusedAsync(ClientId, first.RefreshToken, ct);
        db.ChangeTracker.Clear();

        Assert.Null(await svc.ValidateAccessTokenAsync(second.AccessToken, ct));
        Assert.Null(await svc.FindLiveRefreshTokenAsync(ClientId, second.RefreshToken, ct));
        // A separate sign-in by the same user is a different grant and keeps working.
        Assert.Equal(userId, await svc.ValidateAccessTokenAsync(other.AccessToken, ct));
    }

    [Fact]
    public async Task Refresh_CannotExtendGrantPastItsAbsoluteLifetime()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        await MigrateAsync(db);
        var userId = await SeedUserAsync(db, ct);
        var signedInAt = DateTimeOffset.UtcNow;
        var tokens = await SignInAsync(CreateService(db, new FrozenTimeProvider(signedInAt)), userId, ct);

        // A client refreshing every 29 days stays signed in until the grant ends, but the tokens issued near
        // the end stop at the grant's end rather than a full refresh lifetime later.
        var refreshed = tokens;
        foreach (var day in new[] { 29, 58, 87 })
        {
            refreshed = await CreateService(db, new FrozenTimeProvider(signedInAt.AddDays(day)))
                .RefreshAsync(ClientId, refreshed.RefreshToken, ct);
            Assert.NotNull(refreshed);
        }
        var refreshRow = await db.McpTokens.AsNoTracking()
            .SingleAsync(t => t.TokenHash == TokenHasher.Hash(refreshed.RefreshToken), ct);
        Assert.Equal(refreshRow.GrantExpiresAt, refreshRow.ExpiresAt);

        var afterEnd = CreateService(db, new FrozenTimeProvider(signedInAt.AddDays(91)));
        Assert.Null(await afterEnd.RefreshAsync(ClientId, refreshed.RefreshToken, ct));
    }

    [Fact]
    public async Task DisabledUser_TokensStopWorkingAndCannotRefresh()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        await MigrateAsync(db);
        var svc = CreateService(db);
        var userId = await SeedUserAsync(db, ct);
        var tokens = await SignInAsync(svc, userId, ct);

        var user = await db.Users.SingleAsync(u => u.Id == userId, ct);
        user.Disable(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(ct);

        Assert.Null(await svc.ValidateAccessTokenAsync(tokens.AccessToken, ct));
        Assert.Null(await svc.RefreshAsync(ClientId, tokens.RefreshToken, ct));
    }

    [Fact]
    public async Task RevokeToken_RefreshTokenEndsGrant_WrongClientDoesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        await MigrateAsync(db);
        var svc = CreateService(db);
        var userId = await SeedUserAsync(db, ct);
        var tokens = await SignInAsync(svc, userId, ct);

        await svc.RevokeTokenAsync("other-client", tokens.RefreshToken, ct);
        db.ChangeTracker.Clear();
        Assert.Equal(userId, await svc.ValidateAccessTokenAsync(tokens.AccessToken, ct));

        await svc.RevokeTokenAsync(ClientId, tokens.RefreshToken, ct);
        db.ChangeTracker.Clear();
        Assert.Null(await svc.ValidateAccessTokenAsync(tokens.AccessToken, ct));
        Assert.Null(await svc.FindLiveRefreshTokenAsync(ClientId, tokens.RefreshToken, ct));
    }

    [Fact]
    public async Task ListAndRevokeGrants_PerUser()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        await MigrateAsync(db);
        var svc = CreateService(db);
        var userId = await SeedUserAsync(db, ct);
        var kept = await SignInAsync(svc, userId, ct);
        var revoked = await SignInAsync(svc, userId, ct);
        await svc.RefreshAsync(ClientId, revoked.RefreshToken, ct);

        var grants = await svc.ListGrantsAsync(userId, ct);
        Assert.Equal(2, grants.Count);

        var revokedGrantId = (await db.McpTokens.AsNoTracking()
            .SingleAsync(t => t.TokenHash == TokenHasher.Hash(revoked.AccessToken), ct)).GrantId;
        Assert.False(await svc.RevokeGrantAsync(UserId.From(Guid.NewGuid()), revokedGrantId, ct));
        Assert.True(await svc.RevokeGrantAsync(userId, revokedGrantId, ct));
        db.ChangeTracker.Clear();

        Assert.Single(await svc.ListGrantsAsync(userId, ct));
        Assert.Equal(userId, await svc.ValidateAccessTokenAsync(kept.AccessToken, ct));

        await svc.RevokeAllAsync(userId, ct);
        db.ChangeTracker.Clear();
        Assert.Empty(await svc.ListGrantsAsync(userId, ct));
    }

    [Fact]
    public async Task RedeemAuthCode_ConsumedConcurrently_SecondSaveConflicts()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        await MigrateAsync(db);
        var userId = await SeedUserAsync(db, ct);
        var code = await CreateService(db).IssueAuthCodeAsync(
            ClientId, userId, "https://example.com/callback", TokenHasher.ComputePkceS256Challenge("v"), ct);
        var hash = TokenHasher.Hash(code);

        await using var first = CreateContext();
        await using var second = CreateContext();
        var a = await first.McpAuthCodes.SingleAsync(c => c.CodeHash == hash, ct);
        var b = await second.McpAuthCodes.SingleAsync(c => c.CodeHash == hash, ct);
        a.Consume();
        b.Consume();

        await first.SaveChangesAsync(ct);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync(ct));
    }

    [Fact]
    public async Task RedeemAndRefresh_ConcurrentAttempts_OnlyOneSucceeds()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        await MigrateAsync(db);
        var userId = await SeedUserAsync(db, ct);
        const string redirectUri = "https://example.com/callback";
        const string codeVerifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        var code = await CreateService(db).IssueAuthCodeAsync(
            ClientId, userId, redirectUri, TokenHasher.ComputePkceS256Challenge(codeVerifier), ct);

        var redeemed = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var ctx = CreateContext();
            return await CreateService(ctx).RedeemAuthCodeAsync(ClientId, code, redirectUri, codeVerifier, ct);
        }));
        var tokens = Assert.Single(redeemed, t => t is not null)!;

        var refreshed = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var ctx = CreateContext();
            return await CreateService(ctx).RefreshAsync(ClientId, tokens.RefreshToken, ct);
        }));
        Assert.Single(refreshed, t => t is not null);
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
    public async Task FindLiveAuthCode_FoundOnlyWhileUnconsumedForSameClient()
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

        Assert.Equal(new LiveGrant(IsLoopback: false), await svc.FindLiveAuthCodeAsync(ClientId, code, ct));
        Assert.Null(await svc.FindLiveAuthCodeAsync("other-client", code, ct));
        Assert.Null(await svc.FindLiveRefreshTokenAsync(ClientId, code, ct));
        Assert.Null(await svc.FindLiveAuthCodeAsync(ClientId, "bogus", ct));

        var expired = CreateService(db, new FrozenTimeProvider(DateTimeOffset.UtcNow.AddHours(1)));
        Assert.Null(await expired.FindLiveAuthCodeAsync(ClientId, code, ct));

        Assert.NotNull(await svc.RedeemAuthCodeAsync(ClientId, code, redirectUri, codeVerifier, ct));
        Assert.Null(await svc.FindLiveAuthCodeAsync(ClientId, code, ct));
    }

    [Fact]
    public async Task FindLiveRefreshToken_FoundOnlyWhileUnrevokedAndDoesNotTrack()
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

        Assert.Equal(new LiveGrant(IsLoopback: false), await svc.FindLiveRefreshTokenAsync(ClientId, tokens.RefreshToken, ct));
        Assert.Null(await svc.FindLiveRefreshTokenAsync("other-client", tokens.RefreshToken, ct));
        Assert.Null(await svc.FindLiveRefreshTokenAsync(ClientId, tokens.AccessToken, ct));
        Assert.Empty(db.ChangeTracker.Entries());

        Assert.NotNull(await svc.RefreshAsync(ClientId, tokens.RefreshToken, ct));
        Assert.Null(await svc.FindLiveRefreshTokenAsync(ClientId, tokens.RefreshToken, ct));
    }

    /// <summary>Minimal <see cref="TimeProvider"/> that always returns the same instant.</summary>
    private sealed class FrozenTimeProvider(DateTimeOffset frozenAt) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => frozenAt;
    }

    [Fact]
    public async Task LoopbackGrant_FlagFollowsCodeIntoRotatedRefreshTokens()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        await MigrateAsync(db);
        var svc = CreateService(db);

        var userId = await SeedUserAsync(db, ct);
        const string redirectUri = "http://127.0.0.1:51234/callback";
        const string codeVerifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        var code = await svc.IssueAuthCodeAsync(
            ClientId, userId, redirectUri, TokenHasher.ComputePkceS256Challenge(codeVerifier), ct);

        Assert.Equal(new LiveGrant(IsLoopback: true), await svc.FindLiveAuthCodeAsync(ClientId, code, ct));

        var tokens = await svc.RedeemAuthCodeAsync(ClientId, code, redirectUri, codeVerifier, ct);
        Assert.NotNull(tokens);
        Assert.Equal(new LiveGrant(IsLoopback: true), await svc.FindLiveRefreshTokenAsync(ClientId, tokens.RefreshToken, ct));

        var rotated = await svc.RefreshAsync(ClientId, tokens.RefreshToken, ct);
        Assert.NotNull(rotated);
        Assert.Equal(new LiveGrant(IsLoopback: true), await svc.FindLiveRefreshTokenAsync(ClientId, rotated.RefreshToken, ct));
    }
}
