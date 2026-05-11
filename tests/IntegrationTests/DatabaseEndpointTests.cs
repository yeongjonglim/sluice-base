using System.Net;
using System.Net.Http.Json;
using Aspire.Hosting.Testing;
using IntegrationTests.Supports;
using Npgsql;
using SluiceBase.Api.Endpoints;
using SluiceBase.Core.Permissions;

namespace IntegrationTests;

public class DatabaseEndpointTests(SluiceBaseStackFactory factory)
{
    private KeycloakLoginHelper LoginHelper => new(factory.InitialisedApp);
    private static string UniqueName() => $"srv-{Guid.NewGuid():N}"[..24];

    private static HttpRequestMessage MutationRequest(HttpMethod method, string url, string xsrf, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Add("X-XSRF-TOKEN", xsrf);
        if (body is not null)
        {
            req.Content = JsonContent.Create(body);
        }

        return req;
    }

    private async Task<(AuthenticatedSession session, string xsrf)> AuthorizedSessionAsync(CancellationToken ct)
    {
        var session = await LoginHelper.SignInAsync("alice", "dev", ct);
        var xsrf = await session.FetchXsrfTokenAsync(ct);
        var users = await session.Client.GetFromJsonAsync<ListUserBody>("/api/admin/user", ct);
        var alice = Assert.Single(users!.Users, u => u.Email == "alice@example.com");
        using var grantReq = MutationRequest(HttpMethod.Post,
            $"/api/admin/user/{alice.Id}/permission",
            xsrf,
            new { permission = Permissions.ServerManage });
        (await session.Client.SendAsync(grantReq, ct)).EnsureSuccessStatusCode();
        return (session, xsrf);
    }

    // Helper: server + two credentials + one read-write database against the real blue-appdb
    private async Task<(ServerEndpoints.ServerResponse server, CredentialEndpoints.CredentialResponse readCred, CredentialEndpoints.CredentialResponse writeCred
            , DatabaseEndpoints.DatabaseResponse database)>
        SetupBlueServerAsync(AuthenticatedSession session, string xsrf, CancellationToken ct)
    {
        var connStr = await factory.InitialisedApp.GetConnectionStringAsync("blue-appdb", ct)
                      ?? throw new InvalidOperationException("blue-appdb not found");
        var pg = new NpgsqlConnectionStringBuilder(connStr);

        using var sReq = MutationRequest(HttpMethod.Post,
            "/api/server",
            xsrf,
            new ServerEndpoints.CreateServerRequest(UniqueName(), "postgres", pg.Host!, pg.Port));
        var server = (await (await session.Client.SendAsync(sReq, ct)).Content.ReadFromJsonAsync<ServerEndpoints.ServerResponse>(ct))!;

        using var rcReq = MutationRequest(HttpMethod.Post,
            $"/api/server/{server.Id}/credential",
            xsrf,
            new CredentialEndpoints.AddCredentialRequest("read", "reader_blue", "reader_blue"));
        var readCred = (await (await session.Client.SendAsync(rcReq, ct)).Content.ReadFromJsonAsync<CredentialEndpoints.CredentialResponse>(ct))!;

        using var wcReq = MutationRequest(HttpMethod.Post,
            $"/api/server/{server.Id}/credential",
            xsrf,
            new CredentialEndpoints.AddCredentialRequest("write", "writer_blue", "writer_blue"));
        var writeCred = (await (await session.Client.SendAsync(wcReq, ct)).Content.ReadFromJsonAsync<CredentialEndpoints.CredentialResponse>(ct))!;

        using var dbReq = MutationRequest(HttpMethod.Post,
            $"/api/server/{server.Id}/database",
            xsrf,
            new DatabaseEndpoints.AddDatabaseRequest("App DB", "appdb", readCred.Id, writeCred.Id));
        var database = (await (await session.Client.SendAsync(dbReq, ct)).Content.ReadFromJsonAsync<DatabaseEndpoints.DatabaseResponse>(ct))!;

        return (server, readCred, writeCred, database);
    }

    [Fact]
    public async Task AddDatabase_HappyPath_CanWrite_IsTrue()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf) = await AuthorizedSessionAsync(ct);
        using var _ = session;
        var (_, _, _, database) = await SetupBlueServerAsync(session, xsrf, ct);

        Assert.True(database.CanWrite);
        Assert.False(database.IsDisabled);
    }

    [Fact]
    public async Task CreateDatabase_WithSharedCredential_Succeeds()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf) = await AuthorizedSessionAsync(ct);
        using var _ = session;

        using var sReq = MutationRequest(HttpMethod.Post,
            "/api/server",
            xsrf,
            new ServerEndpoints.CreateServerRequest(UniqueName(), "postgres", "localhost", 5432));
        var server = (await (await session.Client.SendAsync(sReq, ct)).Content.ReadFromJsonAsync<ServerEndpoints.ServerResponse>(ct))!;

        using var rcReq = MutationRequest(HttpMethod.Post,
            $"/api/server/{server.Id}/credential",
            xsrf,
            new CredentialEndpoints.AddCredentialRequest("shared", "shared_user", "pass"));
        var sharedCred = (await (await session.Client.SendAsync(rcReq, ct)).Content.ReadFromJsonAsync<CredentialEndpoints.CredentialResponse>(ct))!;

        // Two databases on same server, same credential
        using var db1Req = MutationRequest(HttpMethod.Post,
            $"/api/server/{server.Id}/database",
            xsrf,
            new DatabaseEndpoints.AddDatabaseRequest("DB One", "db1", sharedCred.Id));
        using var db2Req = MutationRequest(HttpMethod.Post,
            $"/api/server/{server.Id}/database",
            xsrf,
            new DatabaseEndpoints.AddDatabaseRequest("DB Two", "db2", sharedCred.Id));

        Assert.Equal(HttpStatusCode.Created, (await session.Client.SendAsync(db1Req, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await session.Client.SendAsync(db2Req, ct)).StatusCode);
    }

    [Fact]
    public async Task TestConnection_MovedToDatabaseLevel_ReturnsReadAndWrite()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf) = await AuthorizedSessionAsync(ct);
        using var _ = session;
        var (server, _, _, database) = await SetupBlueServerAsync(session, xsrf, ct);

        using var req = MutationRequest(HttpMethod.Post, $"/api/server/{server.Id}/database/{database.Id}/test", xsrf);
        var resp = await session.Client.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        var body = await resp.Content.ReadFromJsonAsync<DatabaseEndpoints.TestConnectionResponse>(ct);
        Assert.True(body!.Read.Ok, body.Read.Error);
        Assert.True(body.Write?.Ok, body.Write?.Error);
    }

    [Fact]
    public async Task TestConnection_ReadOnlyDatabase_WriteIsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf) = await AuthorizedSessionAsync(ct);
        using var _ = session;

        using var sReq = MutationRequest(HttpMethod.Post,
            "/api/server",
            xsrf,
            new ServerEndpoints.CreateServerRequest(UniqueName(), "postgres", "localhost", 5432));
        var server = (await (await session.Client.SendAsync(sReq, ct)).Content.ReadFromJsonAsync<ServerEndpoints.ServerResponse>(ct))!;

        using var rcReq = MutationRequest(HttpMethod.Post,
            $"/api/server/{server.Id}/credential",
            xsrf,
            new CredentialEndpoints.AddCredentialRequest("read", "reader", "pass"));
        var readCred = (await (await session.Client.SendAsync(rcReq, ct)).Content.ReadFromJsonAsync<CredentialEndpoints.CredentialResponse>(ct))!;

        using var dbReq = MutationRequest(HttpMethod.Post,
            $"/api/server/{server.Id}/database",
            xsrf,
            new DatabaseEndpoints.AddDatabaseRequest("Read-only DB", "mydb", readCred.Id));
        var database = (await (await session.Client.SendAsync(dbReq, ct)).Content.ReadFromJsonAsync<DatabaseEndpoints.DatabaseResponse>(ct))!;

        using var testReq = MutationRequest(HttpMethod.Post, $"/api/server/{server.Id}/database/{database.Id}/test", xsrf);
        var body = await (await session.Client.SendAsync(testReq, ct)).Content
            .ReadFromJsonAsync<DatabaseEndpoints.TestConnectionResponse>(ct);
        Assert.Null(body!.Write);
    }

    private sealed record ListUserBody(UserRow[] Users);

    private sealed record UserRow(string Id, string Email);
}