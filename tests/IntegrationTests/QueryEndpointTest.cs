using System.Net;
using System.Net.Http.Json;
using Aspire.Hosting.Testing;
using IntegrationTests.Supports;
using Npgsql;
using SluiceBase.Api.Endpoints;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Servers;

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

    private async Task<(AuthenticatedSession session, string xsrf, DatabaseId databaseId)>
        AuthorizedSessionWithBlueServerAsync(CancellationToken ct)
    {
        var session = await LoginHelper.SignInAsync("alice", "dev", ct);
        var xsrf = await session.FetchXsrfTokenAsync(ct);

        var users = await session.Client.GetFromJsonAsync<ListUserBody>("/api/admin/user", ct);
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

        return (session, xsrf, database.Id);
    }

    [Fact]
    public async Task PostQuery_ReturnsData_ForValidSelect()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf, databaseId) = await AuthorizedSessionWithBlueServerAsync(ct);
        using var _ = session;

        using var req = MutationRequest(HttpMethod.Post,
            "/api/query",
            xsrf,
            new { databaseId, sql = "SELECT id, email FROM public.users ORDER BY id LIMIT 5" });

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
        var (session, xsrf, databaseId) = await AuthorizedSessionWithBlueServerAsync(ct);
        using var _ = session;

        using var req = MutationRequest(HttpMethod.Post,
            "/api/query",
            xsrf,
            new { databaseId, sql = "SELECT naem FROM public.users" });

        var resp = await session.Client.SendAsync(req, ct);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var result = await resp.Content.ReadFromJsonAsync<QueryEndpoints.QueryResponse>(ct);
        Assert.NotNull(result);
        Assert.NotNull(result.Error);
        Assert.Null(result.Columns);
    }

    [Fact]
    public async Task PostQuery_Returns404_ForUnknownDatabase()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = await LoginHelper.SignInAsync("alice", "dev", ct);
        using var _ = session;
        var xsrf = await session.FetchXsrfTokenAsync(ct);

        var users = await session.Client.GetFromJsonAsync<ListUserBody>("/api/admin/user", ct);
        var alice = users!.Users.Single(u => u.Email == "alice@example.com");
        using var grant = MutationRequest(HttpMethod.Post,
            $"/api/admin/user/{alice.Id}/permission",
            xsrf,
            new { permission = Permissions.QueryExecute });
        (await session.Client.SendAsync(grant, ct)).EnsureSuccessStatusCode();

        using var req = MutationRequest(HttpMethod.Post,
            "/api/query",
            xsrf,
            new { databaseId = Guid.NewGuid().ToString(), sql = "SELECT 1" });
        var resp = await session.Client.SendAsync(req, ct);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task PostQuery_Returns401_ForAnonymous()
    {
        using var client = factory.InitialisedApp.CreateHttpClient("api", "https");
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/query");
        req.Content = JsonContent.Create(new { databaseId = Guid.NewGuid().ToString(), sql = "SELECT 1" });
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
            new { databaseId = Guid.NewGuid().ToString(), sql = "SELECT 1" });
        var resp = await session.Client.SendAsync(req, ct);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Query_DisabledDatabase_Returns400()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf, databaseId) = await AuthorizedSessionWithBlueServerAsync(ct);
        using var _ = session;

        var list = await session.Client.GetFromJsonAsync<ServerEndpoints.ListServersResponse>("/api/server", ct);
        var srv = list!.Servers.First(s => s.Databases.Any(d => d.Id == databaseId));
        var db = srv.Databases.First(d => d.Id == databaseId);

        using var disableReq = MutationRequest(HttpMethod.Put, $"/api/server/{srv.Id}/database/{db.Id}", xsrf,
            new DatabaseEndpoints.UpdateDatabaseRequest(db.DisplayName, db.DatabaseName, db.ReadCredentialId, db.WriteCredentialId, true));
        (await session.Client.SendAsync(disableReq, ct)).EnsureSuccessStatusCode();

        using var queryReq = MutationRequest(HttpMethod.Post, "/api/query", xsrf,
            new QueryEndpoints.QueryRequest(databaseId, "SELECT 1"));
        Assert.Equal(HttpStatusCode.BadRequest, (await session.Client.SendAsync(queryReq, ct)).StatusCode);
    }

    [Fact]
    public async Task Query_DisabledServer_Returns400()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf, databaseId) = await AuthorizedSessionWithBlueServerAsync(ct);
        using var _ = session;

        var list = await session.Client.GetFromJsonAsync<ServerEndpoints.ListServersResponse>("/api/server", ct);
        var srv = list!.Servers.First(s => s.Databases.Any(d => d.Id == databaseId));

        using var disableReq = MutationRequest(HttpMethod.Put, $"/api/server/{srv.Id}", xsrf,
            new ServerEndpoints.UpdateServerRequest(srv.Name, srv.Host, srv.Port, srv.Kind, true));
        (await session.Client.SendAsync(disableReq, ct)).EnsureSuccessStatusCode();

        using var queryReq = MutationRequest(HttpMethod.Post, "/api/query", xsrf,
            new QueryEndpoints.QueryRequest(databaseId, "SELECT 1"));
        Assert.Equal(HttpStatusCode.BadRequest, (await session.Client.SendAsync(queryReq, ct)).StatusCode);
    }

    private sealed record ListUserBody(UserRow[] Users);
    private sealed record UserRow(string Id, string Email);
}
