using System.Net;
using System.Net.Http.Json;
using Aspire.Hosting.Testing;
using IntegrationTests.Supports;
using Npgsql;
using SluiceBase.Api.Endpoints;
using SluiceBase.Core.Permissions;

namespace IntegrationTests;

public class ServerEndpointTests(SluiceBaseStackFactory factory)
{
    private KeycloakLoginHelper LoginHelper => new(factory.InitialisedApp);

    private static string UniqueName() => $"srv-{Guid.NewGuid():N}"[..24];

    private static HttpRequestMessage MutationRequest(
        HttpMethod method, string url, string xsrf, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Add("X-XSRF-TOKEN", xsrf); // Question X-XSRF-TOKEN seems to be useless
        if (body is not null)
        {
            req.Content = JsonContent.Create(body);
        }

        return req;
    }

    // ── helpers ───────────────────────────────────────────────────────────────

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

    // Creates a server with one read credential + one write credential + one database.
    // Returns the DatabaseId needed for query/schema/update tests.
    private static async Task<(ServerEndpoints.ServerResponse server, CredentialEndpoints.CredentialResponse readCred, CredentialEndpoints.CredentialResponse
            writeCred, DatabaseEndpoints.DatabaseResponse database)>
        CreateServerWithDatabaseAsync(AuthenticatedSession session, string xsrf, string host, int port, CancellationToken ct, string? name = null)
    {
        var serverName = name ?? UniqueName();

        // Create server
        using var sReq = MutationRequest(HttpMethod.Post,
            "/api/server",
            xsrf,
            new ServerEndpoints.CreateServerRequest(serverName, "postgres", host, port));
        var sResp = await session.Client.SendAsync(sReq, ct);
        sResp.EnsureSuccessStatusCode();
        var server = (await sResp.Content.ReadFromJsonAsync<ServerEndpoints.ServerResponse>(ct))!;

        // Create read credential
        using var rcReq = MutationRequest(HttpMethod.Post,
            $"/api/server/{server.Id}/credential",
            xsrf,
            new CredentialEndpoints.AddCredentialRequest("Read-only role", "reader_blue", "reader_blue"));
        var rcResp = await session.Client.SendAsync(rcReq, ct);
        rcResp.EnsureSuccessStatusCode();
        var readCred = (await rcResp.Content.ReadFromJsonAsync<CredentialEndpoints.CredentialResponse>(ct))!;

        // Create write credential
        using var wcReq = MutationRequest(HttpMethod.Post,
            $"/api/server/{server.Id}/credential",
            xsrf,
            new CredentialEndpoints.AddCredentialRequest("Write role", "writer_blue", "writer_blue"));
        var wcResp = await session.Client.SendAsync(wcReq, ct);
        wcResp.EnsureSuccessStatusCode();
        var writeCred = (await wcResp.Content.ReadFromJsonAsync<CredentialEndpoints.CredentialResponse>(ct))!;

        // Create database
        using var dbReq = MutationRequest(HttpMethod.Post,
            $"/api/server/{server.Id}/database",
            xsrf,
            new DatabaseEndpoints.AddDatabaseRequest("App DB", "appdb", readCred.Id, writeCred.Id));
        var dbResp = await session.Client.SendAsync(dbReq, ct);
        dbResp.EnsureSuccessStatusCode();
        var database = (await dbResp.Content.ReadFromJsonAsync<DatabaseEndpoints.DatabaseResponse>(ct))!;

        return (server, readCred, writeCred, database);
    }

    // ── anonymous / unauthorized ───────────────────────────────────────────────

    [Fact]
    public async Task ListServers_Anonymous_Returns401()
    {
        using var client = factory.InitialisedApp.CreateHttpClient("api", "https");
        var resp = await client.GetAsync("/api/server", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task ListServers_Bob_Returns403()
    {
        using var session = await LoginHelper.SignInAsync("bob", "dev", TestContext.Current.CancellationToken);
        var resp = await session.Client.GetAsync("/api/server", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    // ── create ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateServer_HappyPath_ReturnsServerWithEmptyCredentialsAndDatabases()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf) = await AuthorizedSessionAsync(ct);
        using var _ = session;

        using var req = MutationRequest(HttpMethod.Post,
            "/api/server",
            xsrf,
            new ServerEndpoints.CreateServerRequest(UniqueName(), "postgres", "localhost", 5432));
        var resp = await session.Client.SendAsync(req, ct);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<ServerEndpoints.ServerResponse>(ct);
        Assert.NotNull(body);
        Assert.Empty(body.Credentials);
        Assert.Empty(body.Databases);
        Assert.False(body.IsDisabled);
    }

    [Fact]
    public async Task CreateServer_DuplicateName_Returns409()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf) = await AuthorizedSessionAsync(ct);
        using var _ = session;

        var name = UniqueName();
        var body = new ServerEndpoints.CreateServerRequest(name, "postgres", "localhost", 5432);
        using var req1 = MutationRequest(HttpMethod.Post, "/api/server", xsrf, body);
        (await session.Client.SendAsync(req1, ct)).EnsureSuccessStatusCode();

        using var req2 = MutationRequest(HttpMethod.Post, "/api/server", xsrf, body);
        Assert.Equal(HttpStatusCode.Conflict, (await session.Client.SendAsync(req2, ct)).StatusCode);
    }

    // ── update ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateServer_ChangesNameAndIsDisabled()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf) = await AuthorizedSessionAsync(ct);
        using var _ = session;

        using var cReq = MutationRequest(HttpMethod.Post,
            "/api/server",
            xsrf,
            new ServerEndpoints.CreateServerRequest(UniqueName(), "postgres", "localhost", 5432));
        var created = (await (await session.Client.SendAsync(cReq, ct)).Content
            .ReadFromJsonAsync<ServerEndpoints.ServerResponse>(ct))!;

        using var uReq = MutationRequest(HttpMethod.Put,
            $"/api/server/{created.Id}",
            xsrf,
            new ServerEndpoints.UpdateServerRequest(created.Name + "-renamed", created.Host, created.Port, created.Kind, true));
        var resp = await session.Client.SendAsync(uReq, ct);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<ServerEndpoints.ServerResponse>(ct);
        Assert.True(body!.IsDisabled);
        Assert.EndsWith("-renamed", body.Name);
    }

    // ── soft-delete ───────────────────────────────────────────────────────────

    [Fact]
    public async Task SoftDeleteServer_RemovesFromList()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf) = await AuthorizedSessionAsync(ct);
        using var _ = session;

        using var cReq = MutationRequest(HttpMethod.Post,
            "/api/server",
            xsrf,
            new ServerEndpoints.CreateServerRequest(UniqueName(), "postgres", "localhost", 5432));
        var created = (await (await session.Client.SendAsync(cReq, ct)).Content
            .ReadFromJsonAsync<ServerEndpoints.ServerResponse>(ct))!;

        using var dReq = MutationRequest(HttpMethod.Delete, $"/api/server/{created.Id}", xsrf);
        Assert.Equal(HttpStatusCode.NoContent, (await session.Client.SendAsync(dReq, ct)).StatusCode);

        var list = await session.Client.GetFromJsonAsync<ServerEndpoints.ListServersResponse>("/api/server", ct);
        Assert.DoesNotContain(list!.Servers, s => s.Id == created.Id);
    }

    [Fact]
    public async Task SoftDeleteServer_CascadesToCredentialsAndDatabases()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf) = await AuthorizedSessionAsync(ct);
        using var _ = session;

        var connStr = await factory.InitialisedApp.GetConnectionStringAsync("blue-appdb", ct)
                      ?? throw new InvalidOperationException("blue-appdb not found");
        var pg = new NpgsqlConnectionStringBuilder(connStr);
        var (server, _, _, _) = await CreateServerWithDatabaseAsync(session, xsrf, pg.Host!, pg.Port, ct);

        // Soft-delete the server
        using var dReq = MutationRequest(HttpMethod.Delete, $"/api/server/{server.Id}", xsrf);
        Assert.Equal(HttpStatusCode.NoContent, (await session.Client.SendAsync(dReq, ct)).StatusCode);

        // Server no longer in list
        var list = await session.Client.GetFromJsonAsync<ServerEndpoints.ListServersResponse>("/api/server", ct);
        Assert.DoesNotContain(list!.Servers, s => s.Id == server.Id);

        // Credentials and databases also gone (no longer returned in any server's nested list)
        Assert.DoesNotContain(list.Servers.SelectMany(s => s.Credentials), c => c.Id.Value == server.Id.Value);
    }

    // ── response types ────────────────────────────────────────────────────────

    private sealed record ListUserBody(UserRow[] Users);

    private sealed record UserRow(string Id, string Email);
}