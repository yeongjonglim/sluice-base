using System.Net;
using System.Net.Http.Json;
using Aspire.Hosting.Testing;
using IntegrationTests.Supports;
using Npgsql;
using SluiceBase.Api.Endpoints;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Schemas;
using SluiceBase.Core.Servers;

namespace IntegrationTests;

public class SchemaEndpointTests(SluiceBaseStackFactory factory)
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

    private async Task<(AuthenticatedSession session, DatabaseId databaseId)> AuthorizedSessionWithBlueServerAsync(
        CancellationToken ct)
    {
        var session = await LoginHelper.SignInAsync("alice", "dev", ct);
        var xsrf = await session.FetchXsrfTokenAsync(ct);

        var users = await session.Client.GetFromJsonAsync<ListUserBody>("/api/admin/user", ct);
        var alice = users!.Users.Single(u => u.Email == "alice@example.com");

        using var grantServer = MutationRequest(HttpMethod.Post,
            $"/api/admin/user/{alice.Id}/permission", xsrf,
            new { permission = Permissions.ServerManage });
        (await session.Client.SendAsync(grantServer, ct)).EnsureSuccessStatusCode();

        using var grantQuery = MutationRequest(HttpMethod.Post,
            $"/api/admin/user/{alice.Id}/permission", xsrf,
            new { permission = Permissions.QueryExecute });
        (await session.Client.SendAsync(grantQuery, ct)).EnsureSuccessStatusCode();

        var blueConnStr = await factory.InitialisedApp.GetConnectionStringAsync("blue-appdb", ct);
        var blueBuilder = new NpgsqlConnectionStringBuilder(blueConnStr!);

        var serverName = $"sch-{Guid.NewGuid():N}"[..24];
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

        return (session, database.Id);
    }

    [Fact]
    public async Task GetSchema_ReturnsTree_ForBlueDatabase()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, databaseId) = await AuthorizedSessionWithBlueServerAsync(ct);
        using var _ = session;

        var resp = await session.Client.GetAsync($"/api/schema/{databaseId}", ct);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var tree = await resp.Content.ReadFromJsonAsync<SchemaTree>(ct);
        Assert.NotNull(tree);
        Assert.DoesNotContain(tree.Schemas, s => s.Name == "information_schema");
        var publicSchema = Assert.Single(tree.Schemas, s => s.Name == "public");
        Assert.Contains(publicSchema.Tables, t => t.Name == "users");
        var usersTable = publicSchema.Tables.Single(t => t.Name == "users");
        Assert.NotEmpty(usersTable.Columns);
        Assert.All(usersTable.Columns, c => Assert.NotEmpty(c.DataType));
    }

    [Fact]
    public async Task GetSchema_Returns401_ForAnonymous()
    {
        using var client = factory.InitialisedApp.CreateHttpClient("api", "https");
        var resp = await client.GetAsync(
            $"/api/schema/{Guid.NewGuid()}",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task GetSchema_Returns403_ForBob()
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

        using var session = await LoginHelper.SignInAsync(
            "bob", "dev", TestContext.Current.CancellationToken);
        var resp = await session.Client.GetAsync(
            $"/api/schema/{Guid.NewGuid()}",
            ct);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task GetSchema_Returns404_ForUnknownDatabase()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = await LoginHelper.SignInAsync("alice", "dev", ct);
        using var _ = session;
        var xsrf = await session.FetchXsrfTokenAsync(ct);

        var users = await session.Client.GetFromJsonAsync<ListUserBody>("/api/admin/user", ct);
        var alice = users!.Users.Single(u => u.Email == "alice@example.com");
        using var grant = MutationRequest(HttpMethod.Post,
            $"/api/admin/user/{alice.Id}/permission", xsrf,
            new { permission = Permissions.QueryExecute });
        (await session.Client.SendAsync(grant, ct)).EnsureSuccessStatusCode();

        var resp = await session.Client.GetAsync($"/api/schema/{Guid.NewGuid()}", ct);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    private sealed record ListUserBody(UserRow[] Users);
    private sealed record UserRow(string Id, string Email);
}
