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

    private async Task<(AuthenticatedSession session, string xsrf, DatabaseId databaseId)> AuthorizedSessionWithBlueServerAsync(
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

        // Assign query:execute database role for alice on this database
        await DatabaseRoleTestHelper.AssignByDatabaseAsync(
            session, alice.Id, Permissions.QueryExecute, database.Id.ToString(), xsrf, ct);

        return (session, xsrf, database.Id);
    }

    [Fact]
    public async Task GetSchema_ReturnsTree_ForBlueDatabase()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, _, databaseId) = await AuthorizedSessionWithBlueServerAsync(ct);
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
        // alice creates a database; bob has no role on it → 403
        var (aliceSession, _, databaseId) = await AuthorizedSessionWithBlueServerAsync(ct);
        using var _a = aliceSession;

        using var bobSession = await LoginHelper.SignInAsync("bob", "dev", ct);
        var resp = await bobSession.Client.GetAsync($"/api/schema/{databaseId}", ct);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task GetSchema_Returns404_ForUnknownDatabase()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, _, _) = await AuthorizedSessionWithBlueServerAsync(ct);
        using var _ = session;

        // alice has a role on the real DB, but this is a completely unknown ID → 404
        var resp = await session.Client.GetAsync($"/api/schema/{Guid.NewGuid()}", ct);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task GetSchema_SensitiveColumn_MarkedAsRestricted()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf, databaseId) = await AuthorizedSessionWithBlueServerAsync(ct);
        using var _ = session;

        await SensitiveColumnTestHelper.MarkColumnAsync(
            session, databaseId.ToString(), "public", "users", "email", xsrf, ct);

        var schema = await session.Client.GetFromJsonAsync<SchemaTreeBody>(
            $"/api/schema/{databaseId}", ct);

        var col = schema!.Schemas
            .Single(s => s.Name == "public").Tables
            .Single(t => t.Name == "users").Columns
            .Single(c => c.Name == "email");

        Assert.True(col.IsRestricted);
    }

    [Fact]
    public async Task GetSchema_SensitiveColumnWithBypass_NotRestricted()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf, databaseId) = await AuthorizedSessionWithBlueServerAsync(ct);
        using var _ = session;

        var users = await session.Client.GetFromJsonAsync<ListUserBody>("/api/admin/user", ct);
        var alice = users!.Users.Single(u => u.Email == "alice@example.com");

        var columnId = await SensitiveColumnTestHelper.MarkColumnAsync(
            session, databaseId.ToString(), "public", "users", "email", xsrf, ct);
        await SensitiveColumnTestHelper.GrantBypassAsync(
            session, databaseId.ToString(), columnId, alice.Id, xsrf, ct);

        var schema = await session.Client.GetFromJsonAsync<SchemaTreeBody>(
            $"/api/schema/{databaseId}", ct);

        var col = schema!.Schemas
            .Single(s => s.Name == "public").Tables
            .Single(t => t.Name == "users").Columns
            .Single(c => c.Name == "email");

        Assert.False(col.IsRestricted);
    }

    private sealed record ListUserBody(UserRow[] Users);
    private sealed record UserRow(string Id, string Email);
    private sealed record SchemaTreeBody(SchemaInfoBody[] Schemas);
    private sealed record SchemaInfoBody(string Name, TableInfoBody[] Tables);
    private sealed record TableInfoBody(string Name, ColumnInfoBody[] Columns);
    private sealed record ColumnInfoBody(string Name, string DataType, bool IsNullable, bool IsRestricted);
}
