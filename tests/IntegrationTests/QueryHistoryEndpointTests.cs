using System.Net;
using System.Net.Http.Json;
using Aspire.Hosting.Testing;
using IntegrationTests.Supports;
using Npgsql;
using SluiceBase.Api.Endpoints;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Servers;

namespace IntegrationTests;

public class QueryHistoryEndpointTests(SluiceBaseStackFactory factory)
{
    private KeycloakLoginHelper LoginHelper => new(factory.InitialisedApp);

    private static HttpRequestMessage MutationRequest(
        HttpMethod method, string url, string xsrf, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Add("X-XSRF-TOKEN", xsrf);
        if (body is not null)
        {
            req.Content = JsonContent.Create(body);
        }
        return req;
    }

    /// <summary>
    /// Signs in as Alice, grants her server:manage + query:execute, and creates a
    /// server/database against the blue Postgres instance. Returns Alice's session,
    /// her XSRF token, Alice's user-id, and the new database id.
    /// </summary>
    private async Task<(AuthenticatedSession session, string xsrf, string aliceId, DatabaseId databaseId)>
        AliceWithBlueServerAsync(CancellationToken ct)
    {
        var session = await LoginHelper.SignInAsync("alice", "dev", ct);
        var xsrf = await session.FetchXsrfTokenAsync(ct);

        var users = await session.Client.GetFromJsonAsync<ListUserBody>("/api/admin/user", ct);
        var alice = users!.Users.Single(u => u.Email == "alice@example.com");

        foreach (var perm in new[] { Permissions.ServerManage, Permissions.QueryExecute })
        {
            using var grant = MutationRequest(HttpMethod.Post,
                $"/api/admin/user/{alice.Id}/permission", xsrf, new { permission = perm });
            (await session.Client.SendAsync(grant, ct)).EnsureSuccessStatusCode();
        }
        await PermissionTestHelper.RevokePermissionAsync(
            session,
            "alice@example.com",
            Permissions.QueryAudit,
            xsrf,
            ct);

        var blueConnStr = await factory.InitialisedApp.GetConnectionStringAsync("blue-appdb", ct);
        var blueBuilder = new NpgsqlConnectionStringBuilder(blueConnStr!);

        var serverName = $"hist-{Guid.NewGuid():N}"[..24];
        using var sReq = MutationRequest(HttpMethod.Post, "/api/server", xsrf,
            new ServerEndpoints.CreateServerRequest(serverName, "postgres", blueBuilder.Host!, blueBuilder.Port));
        var sResp = await session.Client.SendAsync(sReq, ct);
        sResp.EnsureSuccessStatusCode();
        var server = (await sResp.Content.ReadFromJsonAsync<ServerEndpoints.ServerResponse>(ct))!;

        using var rcReq = MutationRequest(HttpMethod.Post,
            $"/api/server/{server.Id}/credential", xsrf,
            new CredentialEndpoints.AddCredentialRequest("Read-only role", "reader_blue", "reader_blue"));
        var rcResp = await session.Client.SendAsync(rcReq, ct);
        rcResp.EnsureSuccessStatusCode();
        var readCred = (await rcResp.Content.ReadFromJsonAsync<CredentialEndpoints.CredentialResponse>(ct))!;

        using var dbReq = MutationRequest(HttpMethod.Post,
            $"/api/server/{server.Id}/database", xsrf,
            new DatabaseEndpoints.AddDatabaseRequest("App DB", "appdb", readCred.Id));
        var dbResp = await session.Client.SendAsync(dbReq, ct);
        dbResp.EnsureSuccessStatusCode();
        var database = (await dbResp.Content.ReadFromJsonAsync<DatabaseEndpoints.DatabaseResponse>(ct))!;

        return (session, xsrf, alice.Id, database.Id);
    }

    [Fact]
    public async Task GetHistory_Returns401_ForAnonymous()
    {
        using var client = factory.InitialisedApp.CreateHttpClient("api", "https");
        var resp = await client.GetAsync("/api/query/history", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task GetHistory_Returns403_WithoutQueryExecute()
    {
        var ct = TestContext.Current.CancellationToken;
        using var initialBobSession = await LoginHelper.SignInAsync("bob", "dev", ct);
        await initialBobSession.Client.GetAsync("/api/me", ct);

        using var adminSession = await LoginHelper.SignInAsync("alice", "dev", ct);
        var xsrf = await adminSession.FetchXsrfTokenAsync(ct);
        await PermissionTestHelper.RevokePermissionAsync(
            adminSession,
            "bob@example.com",
            Permissions.QueryExecute,
            xsrf,
            ct);

        using var session = await LoginHelper.SignInAsync("bob", "dev", ct);
        var resp = await session.Client.GetAsync("/api/query/history", ct);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task GetHistory_ReturnsBadRequest_WhenFromIsAfterTo()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, _, _, _) = await AliceWithBlueServerAsync(ct);
        using var _ = session;

        var resp = await session.Client.GetAsync(
            "/api/query/history?from=2030-01-01&to=2020-01-01", ct);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task GetHistory_ReturnsOnlyOwnEntries_WithoutQueryAudit()
    {
        var ct = TestContext.Current.CancellationToken;
        var (aliceSession, xsrf, aliceId, databaseId) = await AliceWithBlueServerAsync(ct);
        using var _a = aliceSession;

        // Ensure bob is registered and has query:execute
        using var bobSession = await LoginHelper.SignInAsync("bob", "dev", ct);
        var bobXsrf = await bobSession.FetchXsrfTokenAsync(ct);
        var users = await aliceSession.Client.GetFromJsonAsync<ListUserBody>("/api/admin/user", ct);
        var bob = users!.Users.Single(u => u.Email == "bob@example.com");
        using var grantBob = MutationRequest(HttpMethod.Post,
            $"/api/admin/user/{bob.Id}/permission", xsrf,
            new { permission = Permissions.QueryExecute });
        (await aliceSession.Client.SendAsync(grantBob, ct)).EnsureSuccessStatusCode();

        // Alice and bob each run a uniquely-tagged query
        var aliceSql = $"SELECT 1 -- alice-{Guid.NewGuid():N}";
        var bobSql   = $"SELECT 1 -- bob-{Guid.NewGuid():N}";

        using var aliceReq = MutationRequest(HttpMethod.Post, "/api/query", xsrf,
            new { databaseId, sql = aliceSql });
        (await aliceSession.Client.SendAsync(aliceReq, ct)).EnsureSuccessStatusCode();

        using var bobReq = MutationRequest(HttpMethod.Post, "/api/query", bobXsrf,
            new { databaseId, sql = bobSql });
        (await bobSession.Client.SendAsync(bobReq, ct)).EnsureSuccessStatusCode();

        // Alice fetches history — she has no query:audit
        var resp = await aliceSession.Client.GetFromJsonAsync<HistoryBody>("/api/query/history", ct);
        Assert.NotNull(resp);
        Assert.Contains(resp.Items, i => i.QueryText == aliceSql);
        Assert.DoesNotContain(resp.Items, i => i.QueryText == bobSql);
    }

    [Fact]
    public async Task GetHistory_ReturnsAllEntries_WithQueryAudit()
    {
        var ct = TestContext.Current.CancellationToken;
        var (aliceSession, xsrf, aliceId, databaseId) = await AliceWithBlueServerAsync(ct);
        using var _a = aliceSession;

        // Ensure bob is registered and has query:execute
        using var bobSession = await LoginHelper.SignInAsync("bob", "dev", ct);
        var bobXsrf = await bobSession.FetchXsrfTokenAsync(ct);
        var users = await aliceSession.Client.GetFromJsonAsync<ListUserBody>("/api/admin/user", ct);
        var bob = users!.Users.Single(u => u.Email == "bob@example.com");
        using var grantBob = MutationRequest(HttpMethod.Post,
            $"/api/admin/user/{bob.Id}/permission", xsrf,
            new { permission = Permissions.QueryExecute });
        (await aliceSession.Client.SendAsync(grantBob, ct)).EnsureSuccessStatusCode();

        // Grant alice query:audit
        using var grantAudit = MutationRequest(HttpMethod.Post,
            $"/api/admin/user/{aliceId}/permission", xsrf,
            new { permission = Permissions.QueryAudit });
        (await aliceSession.Client.SendAsync(grantAudit, ct)).EnsureSuccessStatusCode();

        var aliceSql = $"SELECT 1 -- audit-alice-{Guid.NewGuid():N}";
        var bobSql   = $"SELECT 1 -- audit-bob-{Guid.NewGuid():N}";

        using var aliceReq = MutationRequest(HttpMethod.Post, "/api/query", xsrf,
            new { databaseId, sql = aliceSql });
        (await aliceSession.Client.SendAsync(aliceReq, ct)).EnsureSuccessStatusCode();

        using var bobReq = MutationRequest(HttpMethod.Post, "/api/query", bobXsrf,
            new { databaseId, sql = bobSql });
        (await bobSession.Client.SendAsync(bobReq, ct)).EnsureSuccessStatusCode();

        // Alice fetches history — she has query:audit so she sees both
        var resp = await aliceSession.Client.GetFromJsonAsync<HistoryBody>("/api/query/history", ct);
        Assert.NotNull(resp);
        Assert.Contains(resp.Items, i => i.QueryText == aliceSql);
        Assert.Contains(resp.Items, i => i.QueryText == bobSql);
    }

    [Fact]
    public async Task GetHistory_FiltersByStatus()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf, _, databaseId) = await AliceWithBlueServerAsync(ct);
        using var _ = session;

        var successSql = $"SELECT 1 -- status-ok-{Guid.NewGuid():N}";
        var errorSql   = $"SELECT nonexistent_col_xyz -- status-err-{Guid.NewGuid():N}";

        using var successReq = MutationRequest(HttpMethod.Post, "/api/query", xsrf,
            new { databaseId, sql = successSql });
        await session.Client.SendAsync(successReq, ct); // 200 or 400, either is fine

        using var errorReq = MutationRequest(HttpMethod.Post, "/api/query", xsrf,
            new { databaseId, sql = errorSql });
        await session.Client.SendAsync(errorReq, ct);

        // Filter by Error — should not contain the success entry
        var resp = await session.Client.GetFromJsonAsync<HistoryBody>("/api/query/history?status=Error", ct);
        Assert.NotNull(resp);
        Assert.All(resp.Items, i => Assert.Equal("Error", i.Status));
        Assert.DoesNotContain(resp.Items, i => i.QueryText == successSql);
    }

    [Fact]
    public async Task GetHistory_FiltersByDatabaseId()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf, _, databaseId) = await AliceWithBlueServerAsync(ct);
        using var _ = session;

        var sql = $"SELECT 1 -- db-filter-{Guid.NewGuid():N}";
        using var req = MutationRequest(HttpMethod.Post, "/api/query", xsrf,
            new { databaseId, sql });
        await session.Client.SendAsync(req, ct);

        // Filter by known databaseId
        var resp = await session.Client.GetFromJsonAsync<HistoryBody>(
            $"/api/query/history?databaseId={databaseId}", ct);
        Assert.NotNull(resp);
        Assert.Contains(resp.Items, i => i.QueryText == sql);
        Assert.All(resp.Items, i => Assert.Equal(databaseId.ToString(), i.DatabaseId));

        // Filter by unknown databaseId → empty
        var empty = await session.Client.GetFromJsonAsync<HistoryBody>(
            $"/api/query/history?databaseId={Guid.NewGuid()}", ct);
        Assert.NotNull(empty);
        Assert.Empty(empty.Items);
    }

    [Fact]
    public async Task GetHistory_FiltersByDateRange()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf, _, databaseId) = await AliceWithBlueServerAsync(ct);
        using var _ = session;

        var sql = $"SELECT 1 -- date-filter-{Guid.NewGuid():N}";
        using var req = MutationRequest(HttpMethod.Post, "/api/query", xsrf,
            new { databaseId, sql });
        await session.Client.SendAsync(req, ct);

        // Far-future `to` → no results
        var noResults = await session.Client.GetFromJsonAsync<HistoryBody>(
            "/api/query/history?to=2000-01-01", ct);
        Assert.NotNull(noResults);
        Assert.DoesNotContain(noResults.Items, i => i.QueryText == sql);

        // Far-past `from` still returns the entry
        var withResults = await session.Client.GetFromJsonAsync<HistoryBody>(
            "/api/query/history?from=2020-01-01", ct);
        Assert.NotNull(withResults);
        Assert.Contains(withResults.Items, i => i.QueryText == sql);
    }

    // ── Private DTO records (mirrors the JSON the API will return) ──────────

    private sealed record HistoryBody(HistoryItem[] Items);
    private sealed record HistoryItem(string QueryText, string Status, string? DatabaseId, string? DatabaseDisplayName, string? UserId, string? UserName, string ExecutedAt);
    private sealed record ListUserBody(UserRow[] Users);
    private sealed record UserRow(string Id, string Email);
}
