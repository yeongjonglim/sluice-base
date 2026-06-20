using System.Net;
using System.Net.Http.Json;
using Aspire.Hosting.Testing;
using IntegrationTests.Supports;
using Npgsql;
using SluiceBase.Core.Permissions;

namespace IntegrationTests;

public class DatabaseRoleEndpointTests(SluiceBaseStackFactory factory)
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

    // Creates a fresh server + credential + database for tests that need a real database ID.
    // Grants alice server:manage temporarily. Returns (session, xsrf, databaseId).
    private async Task<(AuthenticatedSession Session, string Xsrf, string DatabaseId)>
        AliceWithTestDatabaseAsync(CancellationToken ct)
    {
        var session = await LoginHelper.SignInAsync("alice", "dev", ct);
        var xsrf = await session.FetchXsrfTokenAsync(ct);

        var users = await session.Client.GetFromJsonAsync<ListUserBody>("/api/admin/user", ct);
        var alice = Assert.Single(users!.Users, u => u.Email == "alice@example.com");

        using var grantServer = MutationRequest(HttpMethod.Post,
            $"/api/admin/user/{alice.Id}/permission", xsrf,
            new { permission = Permissions.ServerManage });
        (await session.Client.SendAsync(grantServer, ct)).EnsureSuccessStatusCode();

        var blueConnStr = await factory.InitialisedApp.GetConnectionStringAsync("blue-appdb", ct);
        var blueBuilder = new NpgsqlConnectionStringBuilder(blueConnStr!);

        var serverName = $"role-{Guid.NewGuid():N}"[..20];
        using var sReq = MutationRequest(HttpMethod.Post, "/api/server", xsrf,
            new { name = serverName, kind = "postgres", host = blueBuilder.Host, port = blueBuilder.Port });
        var sResp = await session.Client.SendAsync(sReq, ct);
        sResp.EnsureSuccessStatusCode();
        var server = (await sResp.Content.ReadFromJsonAsync<ServerBody>(ct))!;

        using var cReq = MutationRequest(HttpMethod.Post, $"/api/server/{server.Id}/credential", xsrf,
            new { label = "read", username = blueBuilder.Username, password = blueBuilder.Password });
        var cResp = await session.Client.SendAsync(cReq, ct);
        cResp.EnsureSuccessStatusCode();
        var cred = (await cResp.Content.ReadFromJsonAsync<CredentialBody>(ct))!;

        using var dbReq = MutationRequest(HttpMethod.Post, $"/api/server/{server.Id}/database", xsrf,
            new { displayName = "test-db", databaseName = blueBuilder.Database ?? "postgres", readCredentialId = cred.Id });
        var dbResp = await session.Client.SendAsync(dbReq, ct);
        dbResp.EnsureSuccessStatusCode();
        var db = (await dbResp.Content.ReadFromJsonAsync<DatabaseBody>(ct))!;

        return (session, xsrf, db.Id);
    }

    // ── anonymous / unauthorized ──────────────────────────────────────────────

    [Fact]
    public async Task ListByDatabase_Anonymous_Returns401()
    {
        using var client = factory.InitialisedApp.CreateHttpClient("api", "https");
        var resp = await client.GetAsync(
            $"/api/admin/database/{Guid.NewGuid()}/role",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task ListByDatabase_Bob_Returns403()
    {
        using var session = await LoginHelper.SignInAsync("bob", "dev", TestContext.Current.CancellationToken);
        var resp = await session.Client.GetAsync(
            $"/api/admin/database/{Guid.NewGuid()}/role",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    // ── list by database ──────────────────────────────────────────────────────

    [Fact]
    public async Task ListByDatabase_NonExistentDatabase_ReturnsEmptyList()
    {
        var ct = TestContext.Current.CancellationToken;
        using var session = await LoginHelper.SignInAsync("alice", "dev", ct);
        var resp = await session.Client.GetAsync(
            $"/api/admin/database/{Guid.NewGuid()}/role", ct);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<RoleListBody>(ct);
        Assert.NotNull(body);
        Assert.Empty(body.Roles);
    }

    // ── assign by database (POST /api/admin/database/{id}/role) ──────────────

    [Fact]
    public async Task AssignByDatabase_HappyPath_Returns201AndAppearsInList()
    {
        var ct = TestContext.Current.CancellationToken;
        var (alice, xsrf, databaseId) = await AliceWithTestDatabaseAsync(ct);
        using var _ = alice;

        // ensure bob user row exists
        using var bob = await LoginHelper.SignInAsync("bob", "dev", ct);
        await bob.Client.GetAsync("/api/me", ct);

        var users = await alice.Client.GetFromJsonAsync<ListUserBody>("/api/admin/user", ct);
        var bobUser = Assert.Single(users!.Users, u => u.Email == "bob@example.com");

        using var req = MutationRequest(
            HttpMethod.Post, $"/api/admin/database/{databaseId}/role", xsrf,
            new { userId = bobUser.Id, permission = Permissions.QueryExecute });
        var resp = await alice.Client.SendAsync(req, ct);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

        var list = await alice.Client.GetFromJsonAsync<RoleListBody>(
            $"/api/admin/database/{databaseId}/role", ct);
        Assert.Contains(list!.Roles, r => r.UserId == bobUser.Id && r.Permission == Permissions.QueryExecute);

        await DatabaseRoleTestHelper.RemoveRoleAsync(alice, databaseId, bobUser.Id, Permissions.QueryExecute, xsrf, ct);
    }

    [Fact]
    public async Task AssignByDatabase_Duplicate_Returns200()
    {
        var ct = TestContext.Current.CancellationToken;
        var (alice, xsrf, databaseId) = await AliceWithTestDatabaseAsync(ct);
        using var _ = alice;

        var users = await alice.Client.GetFromJsonAsync<ListUserBody>("/api/admin/user", ct);
        var aliceUser = Assert.Single(users!.Users, u => u.Email == "alice@example.com");

        using var req1 = MutationRequest(
            HttpMethod.Post, $"/api/admin/database/{databaseId}/role", xsrf,
            new { userId = aliceUser.Id, permission = Permissions.QueryExecute });
        var resp1 = await alice.Client.SendAsync(req1, ct);
        Assert.True(resp1.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK);

        using var req2 = MutationRequest(
            HttpMethod.Post, $"/api/admin/database/{databaseId}/role", xsrf,
            new { userId = aliceUser.Id, permission = Permissions.QueryExecute });
        var resp2 = await alice.Client.SendAsync(req2, ct);
        Assert.Equal(HttpStatusCode.OK, resp2.StatusCode);
    }

    [Fact]
    public async Task AssignByDatabase_UnknownPermission_Returns400()
    {
        var ct = TestContext.Current.CancellationToken;
        var (alice, xsrf, databaseId) = await AliceWithTestDatabaseAsync(ct);
        using var _ = alice;

        var users = await alice.Client.GetFromJsonAsync<ListUserBody>("/api/admin/user", ct);
        var aliceUser = Assert.Single(users!.Users, u => u.Email == "alice@example.com");

        using var req = MutationRequest(
            HttpMethod.Post, $"/api/admin/database/{databaseId}/role", xsrf,
            new { userId = aliceUser.Id, permission = "not:real" });
        var resp = await alice.Client.SendAsync(req, ct);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task AssignByDatabase_NonScopeablePermission_Returns400()
    {
        var ct = TestContext.Current.CancellationToken;
        var (alice, xsrf, databaseId) = await AliceWithTestDatabaseAsync(ct);
        using var _ = alice;

        var users = await alice.Client.GetFromJsonAsync<ListUserBody>("/api/admin/user", ct);
        var aliceUser = Assert.Single(users!.Users, u => u.Email == "alice@example.com");

        using var req = MutationRequest(
            HttpMethod.Post, $"/api/admin/database/{databaseId}/role", xsrf,
            new { userId = aliceUser.Id, permission = Permissions.ServerManage });
        var resp = await alice.Client.SendAsync(req, ct);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // ── remove role ───────────────────────────────────────────────────────────

    [Fact]
    public async Task RemoveRole_HappyPath_Returns204AndDisappears()
    {
        var ct = TestContext.Current.CancellationToken;
        var (alice, xsrf, databaseId) = await AliceWithTestDatabaseAsync(ct);
        using var _ = alice;

        var users = await alice.Client.GetFromJsonAsync<ListUserBody>("/api/admin/user", ct);
        var aliceUser = Assert.Single(users!.Users, u => u.Email == "alice@example.com");

        using var assignReq = MutationRequest(
            HttpMethod.Post, $"/api/admin/database/{databaseId}/role", xsrf,
            new { userId = aliceUser.Id, permission = Permissions.UpdateSubmit });
        (await alice.Client.SendAsync(assignReq, ct)).EnsureSuccessStatusCode();

        using var removeReq = MutationRequest(
            HttpMethod.Delete, $"/api/admin/database/{databaseId}/role/{aliceUser.Id}/{Permissions.UpdateSubmit}", xsrf);
        var removeResp = await alice.Client.SendAsync(removeReq, ct);
        Assert.Equal(HttpStatusCode.NoContent, removeResp.StatusCode);

        var list = await alice.Client.GetFromJsonAsync<RoleListBody>(
            $"/api/admin/database/{databaseId}/role", ct);
        Assert.DoesNotContain(list!.Roles,
            r => r.UserId == aliceUser.Id && r.Permission == Permissions.UpdateSubmit);
    }

    [Fact]
    public async Task RemoveRole_Idempotent_Returns204WhenMissing()
    {
        var ct = TestContext.Current.CancellationToken;
        using var alice = await LoginHelper.SignInAsync("alice", "dev", ct);
        var xsrf = await alice.FetchXsrfTokenAsync(ct);

        using var req = MutationRequest(
            HttpMethod.Delete,
            $"/api/admin/database/{Guid.NewGuid()}/role/{Guid.NewGuid()}/{Permissions.QueryExecute}",
            xsrf);
        var resp = await alice.Client.SendAsync(req, ct);
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
    }

    // ── list by user ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ListByUser_Anonymous_Returns401()
    {
        using var client = factory.InitialisedApp.CreateHttpClient("api", "https");
        var resp = await client.GetAsync(
            $"/api/admin/user/{Guid.NewGuid()}/role",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task ListByUser_HappyPath_ReturnsAssignedRoles()
    {
        var ct = TestContext.Current.CancellationToken;
        var (alice, xsrf, databaseId) = await AliceWithTestDatabaseAsync(ct);
        using var _ = alice;

        var users = await alice.Client.GetFromJsonAsync<ListUserBody>("/api/admin/user", ct);
        var aliceUser = Assert.Single(users!.Users, u => u.Email == "alice@example.com");

        using var assignReq = MutationRequest(
            HttpMethod.Post, $"/api/admin/database/{databaseId}/role", xsrf,
            new { userId = aliceUser.Id, permission = Permissions.QueryAudit });
        (await alice.Client.SendAsync(assignReq, ct)).EnsureSuccessStatusCode();

        var list = await alice.Client.GetFromJsonAsync<UserRoleListBody>(
            $"/api/admin/user/{aliceUser.Id}/role", ct);
        Assert.Contains(list!.Roles, r => r.DatabaseId == databaseId && r.Permission == Permissions.QueryAudit);

        await DatabaseRoleTestHelper.RemoveRoleAsync(alice, databaseId, aliceUser.Id, Permissions.QueryAudit, xsrf, ct);
    }

    // ── assign by user (POST /api/admin/user/{id}/role) ──────────────────────

    [Fact]
    public async Task AssignByUser_HappyPath_Returns201()
    {
        var ct = TestContext.Current.CancellationToken;
        var (alice, xsrf, databaseId) = await AliceWithTestDatabaseAsync(ct);
        using var _ = alice;

        var users = await alice.Client.GetFromJsonAsync<ListUserBody>("/api/admin/user", ct);
        var aliceUser = Assert.Single(users!.Users, u => u.Email == "alice@example.com");

        using var req = MutationRequest(
            HttpMethod.Post, $"/api/admin/user/{aliceUser.Id}/role", xsrf,
            new { databaseId, permission = Permissions.UpdateApprove });
        var resp = await alice.Client.SendAsync(req, ct);
        Assert.True(resp.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK);

        await DatabaseRoleTestHelper.RemoveRoleAsync(alice, databaseId, aliceUser.Id, Permissions.UpdateApprove, xsrf, ct);
    }

    // ── admin server list ─────────────────────────────────────────────────────

    [Fact]
    public async Task AdminServerList_Anonymous_Returns401()
    {
        using var client = factory.InitialisedApp.CreateHttpClient("api", "https");
        var resp = await client.GetAsync("/api/admin/server", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task AdminServerList_Bob_Returns403()
    {
        using var session = await LoginHelper.SignInAsync("bob", "dev", TestContext.Current.CancellationToken);
        var resp = await session.Client.GetAsync("/api/admin/server", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task AdminServerList_Alice_ReturnsServers()
    {
        var ct = TestContext.Current.CancellationToken;
        using var alice = await LoginHelper.SignInAsync("alice", "dev", ct);
        var resp = await alice.Client.GetAsync("/api/admin/server", ct);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<AdminServerListBody>(ct);
        Assert.NotNull(body);
    }

    // ── response records ──────────────────────────────────────────────────────

    private sealed record RoleItem(string UserId, string Permission);
    private sealed record RoleListBody(RoleItem[] Roles);
    private sealed record UserRoleItem(string DatabaseId, string Permission);
    private sealed record UserRoleListBody(UserRoleItem[] Roles);
    private sealed record UserRow(string Id, string Email);
    private sealed record ListUserBody(UserRow[] Users);
    private sealed record AdminDbItem(string Id, string DisplayName);
    private sealed record AdminServerItem(string Id, string Name, AdminDbItem[] Databases);
    private sealed record AdminServerListBody(AdminServerItem[] Servers);

    // Used by AliceWithTestDatabaseAsync helper
    private sealed record ServerBody(string Id, string Name);
    private sealed record CredentialBody(string Id, string Label);
    private sealed record DatabaseBody(string Id, string DisplayName);
}
