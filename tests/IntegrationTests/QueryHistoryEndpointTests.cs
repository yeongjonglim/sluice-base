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

    private async Task<(AuthenticatedSession session, string xsrf, string aliceId, DatabaseId databaseId)>
        AliceWithBlueServerAsync(CancellationToken ct)
    {
        var session = await LoginHelper.SignInAsync("alice", "dev", ct);
        var xsrf = await session.FetchXsrfTokenAsync(ct);

        var users = await session.Client.GetFromJsonAsync<ListUserBody>("/api/admin/user", ct);
        var alice = users!.Users.Single(u => u.Email == "alice@example.com");

        using var grantServer = MutationRequest(HttpMethod.Post,
            $"/api/admin/user/{alice.Id}/permission", xsrf, new { permission = Permissions.ServerManage });
        (await session.Client.SendAsync(grantServer, ct)).EnsureSuccessStatusCode();

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

        // Assign query:execute database role for alice on this database
        await DatabaseRoleTestHelper.AssignByDatabaseAsync(
            session, alice.Id, Permissions.QueryExecute, database.Id.ToString(), xsrf, ct);

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
    public async Task GetHistory_ReturnsEmpty_WithoutAnyDatabaseRoles()
    {
        var ct = TestContext.Current.CancellationToken;
        using var bobSession = await LoginHelper.SignInAsync("bob", "dev", ct);
        await bobSession.Client.GetAsync("/api/me", ct);

        using var adminSession = await LoginHelper.SignInAsync("alice", "dev", ct);
        var adminXsrf = await adminSession.FetchXsrfTokenAsync(ct);
        await PermissionTestHelper.RevokeAllDatabaseRolesAsync(adminSession, "bob@example.com", adminXsrf, ct);

        var resp = await bobSession.Client.GetAsync("/api/query/history", ct);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<HistoryBody>(ct);
        Assert.NotNull(body);
        Assert.Empty(body.Items);
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

        // Ensure bob is registered and has query:execute role on the test database
        using var bobSession = await LoginHelper.SignInAsync("bob", "dev", ct);
        var bobXsrf = await bobSession.FetchXsrfTokenAsync(ct);
        var users = await aliceSession.Client.GetFromJsonAsync<ListUserBody>("/api/admin/user", ct);
        var bob = users!.Users.Single(u => u.Email == "bob@example.com");
        await DatabaseRoleTestHelper.AssignByDatabaseAsync(
            aliceSession, bob.Id, Permissions.QueryExecute, databaseId.ToString(), xsrf, ct);

        // Alice and bob each run a uniquely-tagged query
        var aliceSql = $"SELECT 1 -- alice-{Guid.NewGuid():N}";
        var bobSql   = $"SELECT 1 -- bob-{Guid.NewGuid():N}";

        using var aliceReq = MutationRequest(HttpMethod.Post, "/api/query", xsrf,
            new { databaseId, sql = aliceSql });
        (await aliceSession.Client.SendAsync(aliceReq, ct)).EnsureSuccessStatusCode();

        using var bobReq = MutationRequest(HttpMethod.Post, "/api/query", bobXsrf,
            new { databaseId, sql = bobSql });
        (await bobSession.Client.SendAsync(bobReq, ct)).EnsureSuccessStatusCode();

        // Alice fetches history — she has query:execute but not query:audit, sees only her own
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

        // Ensure bob is registered and has query:execute role on the test database
        using var bobSession = await LoginHelper.SignInAsync("bob", "dev", ct);
        var bobXsrf = await bobSession.FetchXsrfTokenAsync(ct);
        var users = await aliceSession.Client.GetFromJsonAsync<ListUserBody>("/api/admin/user", ct);
        var bob = users!.Users.Single(u => u.Email == "bob@example.com");
        await DatabaseRoleTestHelper.AssignByDatabaseAsync(
            aliceSession, bob.Id, Permissions.QueryExecute, databaseId.ToString(), xsrf, ct);

        // Grant alice query:audit database role on this database
        await DatabaseRoleTestHelper.AssignByDatabaseAsync(
            aliceSession, aliceId, Permissions.QueryAudit, databaseId.ToString(), xsrf, ct);

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

    [Fact]
    public async Task GetHistory_NormalQuery_HasEmptySensitiveColumns()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf, _, databaseId) = await AliceWithBlueServerAsync(ct);
        using var _ = session;

        var sql = $"SELECT 1 -- no-sensitive-{Guid.NewGuid():N}";
        using var req = MutationRequest(HttpMethod.Post, "/api/query", xsrf,
            new { databaseId, sql });
        (await session.Client.SendAsync(req, ct)).EnsureSuccessStatusCode();

        var resp = await session.Client.GetFromJsonAsync<HistoryBody>("/api/query/history", ct);
        var item = Assert.Single(resp!.Items, i => i.QueryText == sql);
        Assert.Empty(item.SensitiveColumns);
    }

    [Fact]
    public async Task GetHistory_BlockedQuery_HasSensitiveColumnsPopulated()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf, _, databaseId) = await AliceWithBlueServerAsync(ct);
        using var _ = session;

        await SensitiveColumnTestHelper.MarkColumnAsync(
            session, databaseId.ToString(), "public", "users", "email", xsrf, ct);

        var sql = $"SELECT email FROM public.users -- blocked-{Guid.NewGuid():N}";
        using var req = MutationRequest(HttpMethod.Post, "/api/query", xsrf,
            new QueryEndpoints.QueryRequest(databaseId, sql));
        var resp = await session.Client.SendAsync(req, ct);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);

        var history = await session.Client.GetFromJsonAsync<HistoryBody>("/api/query/history", ct);
        var item = Assert.Single(history!.Items, i => i.QueryText == sql);
        Assert.Equal("Blocked", item.Status);
        Assert.Contains("public.users.email", item.SensitiveColumns);
    }

    [Fact]
    public async Task GetHistory_BypassedQuery_HasSensitiveColumnsPopulated()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf, aliceId, databaseId) = await AliceWithBlueServerAsync(ct);
        using var _ = session;

        var columnId = await SensitiveColumnTestHelper.MarkColumnAsync(
            session, databaseId.ToString(), "public", "users", "email", xsrf, ct);
        await SensitiveColumnTestHelper.GrantBypassAsync(
            session, databaseId.ToString(), columnId, aliceId, xsrf, ct);

        var sql = $"SELECT email FROM public.users -- bypassed-{Guid.NewGuid():N}";
        using var req = MutationRequest(HttpMethod.Post, "/api/query", xsrf,
            new QueryEndpoints.QueryRequest(databaseId, sql));
        (await session.Client.SendAsync(req, ct)).EnsureSuccessStatusCode();

        var history = await session.Client.GetFromJsonAsync<HistoryBody>("/api/query/history", ct);
        var item = Assert.Single(history!.Items, i => i.QueryText == sql);
        Assert.Equal("Success", item.Status);
        Assert.Contains("public.users.email", item.SensitiveColumns);
    }

    [Fact]
    public async Task GetHistory_FilterBySensitiveColumnAny_ReturnsOnlySensitive()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf, aliceId, databaseId) = await AliceWithBlueServerAsync(ct);
        using var _ = session;

        var columnId = await SensitiveColumnTestHelper.MarkColumnAsync(
            session, databaseId.ToString(), "public", "users", "email", xsrf, ct);
        await SensitiveColumnTestHelper.GrantBypassAsync(
            session, databaseId.ToString(), columnId, aliceId, xsrf, ct);

        var sensitiveSql = $"SELECT email FROM public.users -- filter-any-sensitive-{Guid.NewGuid():N}";
        var normalSql    = $"SELECT 1 -- filter-any-normal-{Guid.NewGuid():N}";

        using var r1 = MutationRequest(HttpMethod.Post, "/api/query", xsrf,
            new QueryEndpoints.QueryRequest(databaseId, sensitiveSql));
        (await session.Client.SendAsync(r1, ct)).EnsureSuccessStatusCode();

        using var r2 = MutationRequest(HttpMethod.Post, "/api/query", xsrf,
            new { databaseId, sql = normalSql });
        (await session.Client.SendAsync(r2, ct)).EnsureSuccessStatusCode();

        var resp = await session.Client.GetFromJsonAsync<HistoryBody>(
            "/api/query/history?sensitiveColumn=any", ct);
        Assert.NotNull(resp);
        Assert.Contains(resp.Items, i => i.QueryText == sensitiveSql);
        Assert.DoesNotContain(resp.Items, i => i.QueryText == normalSql);
    }

    [Fact]
    public async Task GetHistory_FilterBySpecificColumn_ReturnsMatching()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf, aliceId, databaseId) = await AliceWithBlueServerAsync(ct);
        using var _ = session;

        var columnId = await SensitiveColumnTestHelper.MarkColumnAsync(
            session, databaseId.ToString(), "public", "users", "email", xsrf, ct);
        await SensitiveColumnTestHelper.GrantBypassAsync(
            session, databaseId.ToString(), columnId, aliceId, xsrf, ct);

        var sql = $"SELECT email FROM public.users -- filter-specific-{Guid.NewGuid():N}";
        using var req = MutationRequest(HttpMethod.Post, "/api/query", xsrf,
            new QueryEndpoints.QueryRequest(databaseId, sql));
        (await session.Client.SendAsync(req, ct)).EnsureSuccessStatusCode();

        var matched = await session.Client.GetFromJsonAsync<HistoryBody>(
            "/api/query/history?sensitiveColumn=public.users.email", ct);
        Assert.Contains(matched!.Items, i => i.QueryText == sql);

        var unmatched = await session.Client.GetFromJsonAsync<HistoryBody>(
            "/api/query/history?sensitiveColumn=public.users.id", ct);
        Assert.DoesNotContain(unmatched!.Items, i => i.QueryText == sql);
    }

    [Fact]
    public async Task GetHistory_FilterByMultipleColumns_ReturnsUnion()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf, aliceId, databaseId) = await AliceWithBlueServerAsync(ct);
        using var _ = session;

        var columnId = await SensitiveColumnTestHelper.MarkColumnAsync(
            session, databaseId.ToString(), "public", "users", "email", xsrf, ct);
        await SensitiveColumnTestHelper.GrantBypassAsync(
            session, databaseId.ToString(), columnId, aliceId, xsrf, ct);

        var sql = $"SELECT email FROM public.users -- filter-union-{Guid.NewGuid():N}";
        using var req = MutationRequest(HttpMethod.Post, "/api/query", xsrf,
            new QueryEndpoints.QueryRequest(databaseId, sql));
        (await session.Client.SendAsync(req, ct)).EnsureSuccessStatusCode();

        var resp = await session.Client.GetFromJsonAsync<HistoryBody>(
            "/api/query/history?sensitiveColumn=public.users.id&sensitiveColumn=public.users.email", ct);
        Assert.Contains(resp!.Items, i => i.QueryText == sql);
    }

    private sealed record HistoryBody(HistoryItem[] Items);
    private sealed record HistoryItem(
        string QueryText, string Status, string? DatabaseId, string? DatabaseDisplayName,
        string? UserId, string? UserName, string ExecutedAt, string[] SensitiveColumns);
    private sealed record ListUserBody(UserRow[] Users);
    private sealed record UserRow(string Id, string Email);
}
