using System.Net;
using System.Net.Http.Json;
using Aspire.Hosting.Testing;
using IntegrationTests.Supports;
using Npgsql;
using SluiceBase.Api.Endpoints;
using SluiceBase.Core.Permissions;

namespace IntegrationTests;

public class CatalogEndpointTests(SluiceBaseStackFactory factory)
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

    private async Task<(AuthenticatedSession Session, string Xsrf, string DatabaseId)> AdminSessionWithDatabaseAsync(CancellationToken ct)
    {
        var session = await LoginHelper.SignInAsync("alice", "dev", ct);
        var xsrf = await session.FetchXsrfTokenAsync(ct);

        var users = await session.Client.GetFromJsonAsync<ListUserBody>("/api/admin/user", ct);
        var alice = users!.Users.Single(u => u.Email == "alice@example.com");

        using var grantServer = MutationRequest(HttpMethod.Post,
            $"/api/admin/user/{alice.Id}/permission", xsrf,
            new { permission = Permissions.ServerManage });
        (await session.Client.SendAsync(grantServer, ct)).EnsureSuccessStatusCode();

        var blueConnStr = await factory.InitialisedApp.GetConnectionStringAsync("blue-appdb", ct);
        var blueBuilder = new NpgsqlConnectionStringBuilder(blueConnStr!);

        var serverName = $"cat-{Guid.NewGuid():N}"[..24];
        using var sReq = MutationRequest(HttpMethod.Post, "/api/server", xsrf,
            new ServerEndpoints.CreateServerRequest(serverName, "postgres", blueBuilder.Host!, blueBuilder.Port));
        var sResp = await session.Client.SendAsync(sReq, ct);
        sResp.EnsureSuccessStatusCode();
        var server = (await sResp.Content.ReadFromJsonAsync<ServerBody>(ct))!;

        using var cReq = MutationRequest(HttpMethod.Post, $"/api/server/{server.Id}/credential", xsrf,
            new { label = "read", username = blueBuilder.Username, password = blueBuilder.Password });
        var cResp = await session.Client.SendAsync(cReq, ct);
        cResp.EnsureSuccessStatusCode();
        var cred = (await cResp.Content.ReadFromJsonAsync<CredentialBody>(ct))!;

        using var dbReq = MutationRequest(HttpMethod.Post, $"/api/server/{server.Id}/database", xsrf,
            new { displayName = "cat-db", databaseName = blueBuilder.Database ?? "postgres", readCredentialId = cred.Id });
        var dbResp = await session.Client.SendAsync(dbReq, ct);
        dbResp.EnsureSuccessStatusCode();
        var db = (await dbResp.Content.ReadFromJsonAsync<DatabaseBody>(ct))!;

        return (session, xsrf, db.Id);
    }

    [Fact]
    public async Task GetCatalog_Returns200_ForQueryExecuteOnly()
    {
        var ct = TestContext.Current.CancellationToken;
        var (adminSession, adminXsrf, databaseId) = await AdminSessionWithDatabaseAsync(ct);
        using var admin = adminSession;

        using var bobSession = await LoginHelper.SignInAsync("bob", "dev", ct);
        await bobSession.Client.GetAsync("/api/me", ct); // ensure bob's user row exists

        var users = await admin.Client.GetFromJsonAsync<ListUserBody>("/api/admin/user", ct);
        var bob = users!.Users.Single(u => u.Email == "bob@example.com");

        await DatabaseRoleTestHelper.AssignByDatabaseAsync(admin, bob.Id, Permissions.QueryExecute, databaseId, adminXsrf, ct);

        var resp = await bobSession.Client.GetAsync("/api/catalog/server", ct);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<CatalogEndpoints.CatalogServersResponse>(ct);
        Assert.NotNull(body);
        Assert.NotEmpty(body.Servers);
    }

    [Fact]
    public async Task GetCatalog_Returns200_ForUpdateSubmitOnly()
    {
        var ct = TestContext.Current.CancellationToken;
        var (adminSession, adminXsrf, databaseId) = await AdminSessionWithDatabaseAsync(ct);
        using var admin = adminSession;

        using var bobSession = await LoginHelper.SignInAsync("bob", "dev", ct);
        await bobSession.Client.GetAsync("/api/me", ct);

        var users = await admin.Client.GetFromJsonAsync<ListUserBody>("/api/admin/user", ct);
        var bob = users!.Users.Single(u => u.Email == "bob@example.com");

        await DatabaseRoleTestHelper.AssignByDatabaseAsync(admin, bob.Id, Permissions.UpdateSubmit, databaseId, adminXsrf, ct);

        var resp = await bobSession.Client.GetAsync("/api/catalog/server", ct);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task GetCatalog_Returns200_ForServerManage()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, _, _) = await AdminSessionWithDatabaseAsync(ct);
        using var _ = session;

        var resp = await session.Client.GetAsync("/api/catalog/server", ct);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task GetCatalog_Returns401_ForAnonymous()
    {
        using var client = factory.InitialisedApp.CreateHttpClient("api", "https");
        var resp = await client.GetAsync("/api/catalog/server", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task GetCatalog_ReturnsEmptyList_ForNoRoles()
    {
        var ct = TestContext.Current.CancellationToken;
        using var bobSession = await LoginHelper.SignInAsync("bob", "dev", ct);
        await bobSession.Client.GetAsync("/api/me", ct);

        using var adminSession = await LoginHelper.SignInAsync("alice", "dev", ct);
        var adminXsrf = await adminSession.FetchXsrfTokenAsync(ct);
        await PermissionTestHelper.RevokeAllDatabaseRolesAsync(adminSession, "bob@example.com", adminXsrf, ct);

        var resp = await bobSession.Client.GetAsync("/api/catalog/server", ct);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<CatalogEndpoints.CatalogServersResponse>(ct);
        Assert.NotNull(body);
        Assert.Empty(body.Servers);
    }

    [Fact]
    public async Task GetCatalog_ExcludesDisabledServers()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf, _) = await AdminSessionWithDatabaseAsync(ct);
        using var _ = session;

        var list = await session.Client.GetFromJsonAsync<ServerEndpoints.ListServersResponse>("/api/server", ct);
        var srv = list!.Servers[0];
        using var disableReq = MutationRequest(HttpMethod.Put, $"/api/server/{srv.Id}", xsrf,
            new ServerEndpoints.UpdateServerRequest(srv.Name, srv.Host, srv.Port, srv.Kind, true));
        (await session.Client.SendAsync(disableReq, ct)).EnsureSuccessStatusCode();

        var catalog = await session.Client.GetFromJsonAsync<CatalogEndpoints.CatalogServersResponse>("/api/catalog/server", ct);
        Assert.DoesNotContain(catalog!.Servers, s => s.Id == srv.Id);
    }

    [Fact]
    public async Task GetCatalog_ResponseDoesNotContainSensitiveFields()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, _, _) = await AdminSessionWithDatabaseAsync(ct);
        using var _ = session;

        var raw = await session.Client.GetStringAsync("/api/catalog/server", ct);
        Assert.DoesNotContain("\"host\"", raw);
        Assert.DoesNotContain("\"port\"", raw);
        Assert.DoesNotContain("\"credentials\"", raw);
        Assert.DoesNotContain("\"createdAt\"", raw);
    }

    private sealed record ListUserBody(UserRow[] Users);
    private sealed record UserRow(string Id, string Email);
    private sealed record ServerBody(string Id, string Name);
    private sealed record CredentialBody(string Id, string Label);
    private sealed record DatabaseBody(string Id, string DisplayName);
}
