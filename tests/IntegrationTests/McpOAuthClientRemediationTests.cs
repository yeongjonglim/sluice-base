using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SluiceBase.Api.Data;
using SluiceBase.Api.Mcp;
using SluiceBase.Api.Mcp.Oauth;
using SluiceBase.Core.Mcp;
using SluiceBase.Core.Users;
using Testcontainers.PostgreSql;

namespace IntegrationTests;

/// <summary>
/// DB-backed tests for <see cref="McpOAuthClientRemediation"/>. Uses Testcontainers — does NOT
/// require the full Aspire stack.
/// </summary>
public sealed class McpOAuthClientRemediationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17")
        .WithDatabase("sluice_test_mcp_remediation")
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

    private static async Task<McpOAuthClient> SeedClientWithTokensAsync(
        AppDbContext db, UserId userId, string[] redirectUris, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var client = McpOAuthClient.Register("client", redirectUris, now);
        db.McpOAuthClients.Add(client);
        db.McpTokens.Add(McpToken.Issue(Guid.NewGuid().ToString(), McpTokenType.Access, userId, client.ClientId, now, now.AddHours(1)));
        db.McpTokens.Add(McpToken.Issue(Guid.NewGuid().ToString(), McpTokenType.Refresh, userId, client.ClientId, now, now.AddDays(30)));
        db.McpAuthCodes.Add(McpAuthCode.Issue(Guid.NewGuid().ToString(), client.ClientId, userId, redirectUris[0], "challenge", now.AddMinutes(2)));
        await db.SaveChangesAsync(ct);
        return client;
    }

    [Fact]
    public async Task RunAsync_StripsDisallowedUrisAndRevokesThatClientsTokensOnly()
    {
        var ct = TestContext.Current.CancellationToken;
        await using (var setup = CreateContext())
        {
            await setup.Database.MigrateAsync(ct);
        }

        string attackerId, mixedId, loopbackId;
        await using (var db = CreateContext())
        {
            var user = User.Create("victim@example.com", "Victim", DateTimeOffset.UtcNow);
            db.Users.Add(user);
            await db.SaveChangesAsync(ct);

            attackerId = (await SeedClientWithTokensAsync(db, user.Id, ["https://attacker.example/cb"], ct)).ClientId;
            mixedId = (await SeedClientWithTokensAsync(db, user.Id,
                ["https://vscode.dev/redirect", "http://127.0.0.1:33418/"], ct)).ClientId;
            loopbackId = (await SeedClientWithTokensAsync(db, user.Id, ["http://127.0.0.1:5000/callback"], ct)).ClientId;
        }

        // Twice, to show it is idempotent on every startup.
        for (var run = 0; run < 2; run++)
        {
            await using var db = CreateContext();
            await McpOAuthClientRemediation.RunAsync(db, new McpOptions(), NullLogger.Instance, ct);
        }

        await using var assertDb = CreateContext();
        var clients = await assertDb.McpOAuthClients.AsNoTracking().ToDictionaryAsync(c => c.ClientId, ct);
        Assert.Empty(clients[attackerId].RedirectUris);
        Assert.Equal(["http://127.0.0.1:33418/"], clients[mixedId].RedirectUris);
        Assert.Equal(["http://127.0.0.1:5000/callback"], clients[loopbackId].RedirectUris);

        var tokens = await assertDb.McpTokens.AsNoTracking().ToListAsync(ct);
        Assert.All(tokens.Where(t => t.ClientId == attackerId || t.ClientId == mixedId), t => Assert.True(t.Revoked));
        Assert.All(tokens.Where(t => t.ClientId == loopbackId), t => Assert.False(t.Revoked));

        var codes = await assertDb.McpAuthCodes.AsNoTracking().ToListAsync(ct);
        Assert.All(codes.Where(c => c.ClientId == attackerId || c.ClientId == mixedId), c => Assert.True(c.Consumed));
        Assert.All(codes.Where(c => c.ClientId == loopbackId), c => Assert.False(c.Consumed));
    }
}
