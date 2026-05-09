using System.Net;
using System.Net.Http.Json;
using Aspire.Hosting.Testing;
using IntegrationTests.Supports;
using Npgsql;
using SluiceBase.Api.Endpoints;
using SluiceBase.Core.Permissions;

namespace IntegrationTests;

public class QueryEndpointTests(SluiceBaseStackFactory factory)
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

    private async Task<(AuthenticatedSession session, string serverId)> AuthorizedSessionWithBlueServerAsync(
        CancellationToken ct)
    {
        var session = await LoginHelper.SignInAsync("alice", "dev", ct);
        var xsrf = await session.FetchXsrfTokenAsync(ct);

        var users = await session.Client.GetFromJsonAsync<ListUsersResponse>("/api/admin/user", ct);
        var alice = users!.Users.Single(u => u.Email == "alice@example.com");

        using var grantServer = MutationRequest(HttpMethod.Post,
            $"/api/admin/user/{alice.Id}/permission",
            xsrf,
            new { permission = Permissions.ServerManage });
        (await session.Client.SendAsync(grantServer, ct)).EnsureSuccessStatusCode();

        using var grantQuery = MutationRequest(HttpMethod.Post,
            $"/api/admin/user/{alice.Id}/permission",
            xsrf,
            new { permission = Permissions.QueryExecute });
        (await session.Client.SendAsync(grantQuery, ct)).EnsureSuccessStatusCode();

        var blueConnStr = await factory.InitialisedApp.GetConnectionStringAsync("blue-appdb", ct);
        var blueBuilder = new NpgsqlConnectionStringBuilder(blueConnStr!);

        var serverName = $"qry-{Guid.NewGuid():N}"[..24];
        using var createReq = MutationRequest(HttpMethod.Post,
            "/api/server",
            xsrf,
            new ServerEndpoints.CreateServerRequest(
                serverName,
                "postgres",
                blueBuilder.Host!,
                blueBuilder.Port,
                "appdb",
                "reader_blue",
                "reader_blue"));
        var createResp = await session.Client.SendAsync(createReq, ct);
        createResp.EnsureSuccessStatusCode();
        var server = await createResp.Content.ReadFromJsonAsync<ServerEndpoints.ServerResponse>(ct);

        return (session, server!.Id.Value.ToString());
    }

    [Fact]
    public async Task PostQuery_ReturnsData_ForValidSelect()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, serverId) = await AuthorizedSessionWithBlueServerAsync(ct);
        using var _ = session;
        var xsrf = await session.FetchXsrfTokenAsync(ct);

        using var req = MutationRequest(HttpMethod.Post,
            "/api/query",
            xsrf,
            new { serverId, sql = "SELECT id, email FROM public.users ORDER BY id LIMIT 5" });

        var resp = await session.Client.SendAsync(req, ct);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var result = await resp.Content.ReadFromJsonAsync<QueryEndpoints.QueryResponse>(ct);
        Assert.NotNull(result);
        Assert.Null(result.Error);
        Assert.NotNull(result.Columns);
        Assert.Equal(2, result.Columns.Length);
        Assert.Equal("id", result.Columns[0]);
        Assert.Equal("email", result.Columns[1]);
        Assert.NotNull(result.Rows);
        Assert.NotEmpty(result.Rows);
        Assert.True(result.DurationMs >= 0);
        Assert.True(result.RowCount > 0);
    }

    [Fact]
    public async Task PostQuery_ReturnsError_ForInvalidSql()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, serverId) = await AuthorizedSessionWithBlueServerAsync(ct);
        using var _ = session;
        var xsrf = await session.FetchXsrfTokenAsync(ct);

        using var req = MutationRequest(HttpMethod.Post,
            "/api/query",
            xsrf,
            new { serverId, sql = "SELECT naem FROM public.users" });

        var resp = await session.Client.SendAsync(req, ct);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var result = await resp.Content.ReadFromJsonAsync<QueryEndpoints.QueryResponse>(ct);
        Assert.NotNull(result);
        Assert.NotNull(result.Error);
        Assert.Null(result.Columns);
    }

    [Fact]
    public async Task PostQuery_Returns404_ForUnknownServer()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = await LoginHelper.SignInAsync("alice", "dev", ct);
        using var _ = session;
        var xsrf = await session.FetchXsrfTokenAsync(ct);

        var users = await session.Client.GetFromJsonAsync<ListUsersResponse>("/api/admin/user", ct);
        var alice = users!.Users.Single(u => u.Email == "alice@example.com");
        using var grant = MutationRequest(HttpMethod.Post,
            $"/api/admin/user/{alice.Id}/permission",
            xsrf,
            new { permission = Permissions.QueryExecute });
        (await session.Client.SendAsync(grant, ct)).EnsureSuccessStatusCode();

        using var req = MutationRequest(HttpMethod.Post,
            "/api/query",
            xsrf,
            new { serverId = Guid.NewGuid().ToString(), sql = "SELECT 1" });
        var resp = await session.Client.SendAsync(req, ct);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task PostQuery_Returns401_ForAnonymous()
    {
        using var client = factory.InitialisedApp.CreateHttpClient("api", "https");
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/query");
        req.Content = JsonContent.Create(new { serverId = Guid.NewGuid().ToString(), sql = "SELECT 1" });
        var resp = await client.SendAsync(req, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task PostQuery_Returns403_ForBob()
    {
        var ct = TestContext.Current.CancellationToken;
        using var session = await LoginHelper.SignInAsync("bob", "dev", ct);
        var xsrf = await session.FetchXsrfTokenAsync(ct);
        using var req = MutationRequest(HttpMethod.Post,
            "/api/query",
            xsrf,
            new { serverId = Guid.NewGuid().ToString(), sql = "SELECT 1" });
        var resp = await session.Client.SendAsync(req, ct);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }
}