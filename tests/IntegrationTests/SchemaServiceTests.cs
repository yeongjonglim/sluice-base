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

/// <summary>
/// Verifies ISchemaService behaviour (via the /api/schema endpoint) after extraction
/// from the endpoint handler. Behaviour must be identical to the original endpoint tests.
/// </summary>
public class SchemaServiceTests(SluiceBaseStackFactory factory)
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
    /// Creates an alice session, registers a server+database, and grants alice query:execute on it.
    /// Returns the session and the new databaseId.
    /// </summary>
    private async Task<(AuthenticatedSession session, string xsrf, DatabaseId databaseId)> AliceSessionWithDatabaseAsync(
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

        var targetConnStr = await factory.InitialisedApp.GetConnectionStringAsync("blue-appdb", ct);
        var targetBuilder = new NpgsqlConnectionStringBuilder(targetConnStr!);

        var serverName = $"svc-{Guid.NewGuid():N}"[..24];
        using var sReq = MutationRequest(HttpMethod.Post, "/api/server", xsrf,
            new ServerEndpoints.CreateServerRequest(serverName, "postgres", targetBuilder.Host!, targetBuilder.Port));
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

        await DatabaseRoleTestHelper.AssignByDatabaseAsync(
            session, alice.Id, Permissions.QueryExecute, database.Id.ToString(), xsrf, ct);

        return (session, xsrf, database.Id);
    }

    [Fact]
    public async Task GetSchema_ReturnsAnnotatedTree_WhenUserHasQueryExecuteRole()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, _, databaseId) = await AliceSessionWithDatabaseAsync(ct);
        using var _ = session;

        var resp = await session.Client.GetAsync($"/api/schema/{databaseId}", ct);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var tree = await resp.Content.ReadFromJsonAsync<SchemaTree>(ct);
        Assert.NotNull(tree);
        Assert.NotEmpty(tree.Schemas);
        var publicSchema = Assert.Single(tree.Schemas, s => s.Name == "public");
        Assert.Contains(publicSchema.Tables, t => t.Name == "users");
    }

    [Fact]
    public async Task GetSchema_ReturnsForbidden_WhenUserLacksQueryExecuteRole()
    {
        var ct = TestContext.Current.CancellationToken;
        // alice creates the database; bob has no role on it → service must return Forbidden
        var (aliceSession, _, databaseId) = await AliceSessionWithDatabaseAsync(ct);
        using var _a = aliceSession;

        using var bobSession = await LoginHelper.SignInAsync("bob", "dev", ct);
        var resp = await bobSession.Client.GetAsync($"/api/schema/{databaseId}", ct);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task GetSchema_SensitiveColumn_MarkedAsSensitiveAndRestricted()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf, databaseId) = await AliceSessionWithDatabaseAsync(ct);
        using var _ = session;

        await SensitiveColumnTestHelper.MarkColumnAsync(
            session, databaseId.ToString(), "public", "users", "email", xsrf, ct);

        var schema = await session.Client.GetFromJsonAsync<SchemaTreeBody>(
            $"/api/schema/{databaseId}", ct);

        var col = schema!.Schemas
            .Single(s => s.Name == "public").Tables
            .Single(t => t.Name == "users").Columns
            .Single(c => c.Name == "email");

        Assert.True(col.IsSensitive);
        Assert.True(col.IsRestricted);
    }

    [Fact]
    public async Task GetSchema_SensitiveColumnWithBypass_IsSensitiveButNotRestricted()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf, databaseId) = await AliceSessionWithDatabaseAsync(ct);
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

        Assert.True(col.IsSensitive);
        Assert.False(col.IsRestricted);
    }

    private sealed record ListUserBody(UserRow[] Users);
    private sealed record UserRow(string Id, string Email);
    private sealed record SchemaTreeBody(SchemaInfoBody[] Schemas);
    private sealed record SchemaInfoBody(string Name, TableInfoBody[] Tables);
    private sealed record TableInfoBody(string Name, ColumnInfoBody[] Columns);
    private sealed record ColumnInfoBody(string Name, string DataType, bool IsNullable, bool IsSensitive, bool IsRestricted);
}
