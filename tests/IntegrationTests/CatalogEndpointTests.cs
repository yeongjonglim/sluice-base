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

    private async Task<(AuthenticatedSession session, string xsrf)> AdminSessionWithServerAsync(CancellationToken ct)
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
        (await session.Client.SendAsync(sReq, ct)).EnsureSuccessStatusCode();

        return (session, xsrf);
    }

    private async Task GrantAsync(AuthenticatedSession session, string xsrf, string email, string permission, CancellationToken ct)
    {
        var users = await session.Client.GetFromJsonAsync<ListUserBody>("/api/admin/user", ct);
        var user = users!.Users.Single(u => u.Email == email);
        using var req = MutationRequest(HttpMethod.Post,
            $"/api/admin/user/{user.Id}/permission", xsrf,
            new { permission });
        (await session.Client.SendAsync(req, ct)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task GetCatalog_Returns200_ForQueryExecuteOnly()
    {
        var ct = TestContext.Current.CancellationToken;
        var (adminSession, adminXsrf) = await AdminSessionWithServerAsync(ct);
        using var admin = adminSession;

        using var bobSession = await LoginHelper.SignInAsync("bob", "dev", ct);

        await GrantAsync(admin, adminXsrf, "bob@example.com", Permissions.QueryExecute, ct);

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
        var (adminSession2, adminXsrf) = await AdminSessionWithServerAsync(ct);
        using var admin2 = adminSession2;

        using var bobSession = await LoginHelper.SignInAsync("bob", "dev", ct);
        await GrantAsync(admin2, adminXsrf, "bob@example.com", Permissions.UpdateSubmit, ct);

        var resp = await bobSession.Client.GetAsync("/api/catalog/server", ct);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task GetCatalog_Returns200_ForServerManage()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, _) = await AdminSessionWithServerAsync(ct);
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
    public async Task GetCatalog_Returns403_ForNoRelevantPermission()
    {
        var ct = TestContext.Current.CancellationToken;
        using var adminSession = await LoginHelper.SignInAsync("alice", "dev", ct);
        var adminXsrf = await adminSession.FetchXsrfTokenAsync(ct);

        using var bobSession = await LoginHelper.SignInAsync("bob", "dev", ct);
        await PermissionTestHelper.RevokePermissionAsync(adminSession, "bob@example.com", Permissions.QueryExecute, adminXsrf, ct);
        await PermissionTestHelper.RevokePermissionAsync(adminSession, "bob@example.com", Permissions.UpdateSubmit, adminXsrf, ct);

        await bobSession.Client.GetAsync("/api/me", ct);

        var resp = await bobSession.Client.GetAsync("/api/catalog/server", ct);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task GetCatalog_ExcludesDisabledServers()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf) = await AdminSessionWithServerAsync(ct);
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
        var (session2, _) = await AdminSessionWithServerAsync(ct);
        using var _s = session2;

        var raw = await session2.Client.GetStringAsync("/api/catalog/server", ct);
        Assert.DoesNotContain("\"host\"", raw);
        Assert.DoesNotContain("\"port\"", raw);
        Assert.DoesNotContain("\"credentials\"", raw);
        Assert.DoesNotContain("\"createdAt\"", raw);
    }

    private sealed record ListUserBody(UserRow[] Users);
    private sealed record UserRow(string Id, string Email);
}