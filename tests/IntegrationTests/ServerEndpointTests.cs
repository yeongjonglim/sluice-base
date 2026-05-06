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

        var users = await session.Client.GetFromJsonAsync<ListUserBody>(
            "/api/admin/user",
            ct);
        var alice = Assert.Single(users!.Users, u => u.Email == "alice@example.com");
        using var grantReq = MutationRequest(
            HttpMethod.Post,
            $"/api/admin/user/{alice.Id}/permission",
            xsrf,
            new { permission = Permissions.ServerManage });
        var grantResp = await session.Client.SendAsync(grantReq, ct);
        grantResp.EnsureSuccessStatusCode();

        return (session, xsrf);
    }

    private static async Task<ServerEndpoints.ServerResponse> CreateServerAsync(
        AuthenticatedSession session, string xsrf, string name,
        string host, int port, CancellationToken ct)
    {
        using var req = MutationRequest(
            HttpMethod.Post,
            "/api/server",
            xsrf,
            new ServerEndpoints.CreateServerRequest(name,
                "postgres",
                host,
                port,
                "appdb",
                "reader_blue",
                "reader_blue",
                "writer_blue",
                "writer_blue")
        );
        var resp = await session.Client.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<ServerEndpoints.ServerResponse>(ct);
        return body!;
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
    public async Task CreateServer_HappyPath_HasPasswordTrueNeverReturnsPlaintext()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf) = await AuthorizedSessionAsync(ct);
        using var _ = session;

        var name = UniqueName();
        using var req = MutationRequest(
            HttpMethod.Post,
            "/api/server",
            xsrf,
            new ServerEndpoints.CreateServerRequest(name,
                "postgres",
                "localhost",
                5432,
                "appdb",
                "reader_blue",
                "s3cr3t"
            )
        );
        var resp = await session.Client.SendAsync(req, ct);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<ServerEndpoints.ServerResponse>(ct);
        Assert.NotNull(body);
        Assert.Equal(name, body.Name);
        Assert.False(body.HasWriteCredential);

        // Verify no plaintext in the JSON response
        var raw = await resp.Content.ReadAsStringAsync(ct);
        Assert.DoesNotContain("s3cr3t", raw);
    }

    [Fact]
    public async Task CreateServer_MismatchedWriteCredentials_Returns400()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf) = await AuthorizedSessionAsync(ct);
        using var _ = session;

        using var req = MutationRequest(
            HttpMethod.Post,
            "/api/server",
            xsrf,
            new ServerEndpoints.CreateServerRequest(
                UniqueName(),
                "postgres",
                "localhost",
                5432,
                "appdb",
                "reader",
                "pass",
                "writer"
            )
        );
        var resp = await session.Client.SendAsync(req, ct);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task CreateServer_DuplicateName_Returns409()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf) = await AuthorizedSessionAsync(ct);
        using var _ = session;

        var name = UniqueName();
        var body = new ServerEndpoints.CreateServerRequest(
            name,
            "postgres",
            "localhost",
            5432,
            "appdb",
            "r",
            "p"
        );

        using var req1 = MutationRequest(HttpMethod.Post, "/api/server", xsrf, body);
        var resp1 = await session.Client.SendAsync(req1, ct);
        resp1.EnsureSuccessStatusCode();

        using var req2 = MutationRequest(HttpMethod.Post, "/api/server", xsrf, body);
        var resp2 = await session.Client.SendAsync(req2, ct);
        Assert.Equal(HttpStatusCode.Conflict, resp2.StatusCode);
    }

    // ── update ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateServer_NullReadPassword_PreservesExistingCiphertext()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf) = await AuthorizedSessionAsync(ct);
        using var _ = session;

        var connStr = await factory.InitialisedApp.GetConnectionStringAsync("blue-appdb", ct)
                      ?? throw new InvalidOperationException("blue-appdb not found");
        var pg = new NpgsqlConnectionStringBuilder(connStr);

        var created = await CreateServerAsync(session, xsrf, UniqueName(), pg.Host!, pg.Port, ct);

        // Update name only — no new password
        using var req = MutationRequest(
            HttpMethod.Put,
            $"/api/server/{created.Id}",
            xsrf,
            new ServerEndpoints.UpdateServerRequest(
                created.Name + "-renamed",
                created.Host,
                created.Port,
                created.Database
            )
        );
        var resp = await session.Client.SendAsync(req, ct);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<ServerEndpoints.ServerResponse>(ct);
        Assert.True(body!.HasWriteCredential);
    }

    [Fact]
    public async Task UpdateServer_ClearsWriteCredential()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf) = await AuthorizedSessionAsync(ct);
        using var _ = session;

        var connStr = await factory.InitialisedApp.GetConnectionStringAsync("blue-appdb", ct)
                      ?? throw new InvalidOperationException("blue-appdb not found");
        var pg = new NpgsqlConnectionStringBuilder(connStr);

        var created = await CreateServerAsync(session, xsrf, UniqueName(), pg.Host!, pg.Port, ct);
        Assert.True(created.HasWriteCredential);

        // Clear write credentials by sending empty strings
        using var req = MutationRequest(
            HttpMethod.Put,
            $"/api/server/{created.Id}",
            xsrf,
            new ServerEndpoints.UpdateServerRequest(
                created.Name,
                created.Host,
                created.Port,
                created.Database,
                "new_read_user",
                "new_read_pass",
                string.Empty,
                string.Empty
            )
        );
        var resp = await session.Client.SendAsync(req, ct);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<ServerEndpoints.ServerResponse>(ct);
        Assert.False(body!.HasWriteCredential);
    }

    // ── delete ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteServer_RemovesFromList()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf) = await AuthorizedSessionAsync(ct);
        using var _ = session;

        var connStr = await factory.InitialisedApp.GetConnectionStringAsync("blue-appdb", ct)
                      ?? throw new InvalidOperationException("blue-appdb not found");
        var pg = new NpgsqlConnectionStringBuilder(connStr);

        var created = await CreateServerAsync(session, xsrf, UniqueName(), pg.Host!, pg.Port, ct);

        using var req = MutationRequest(HttpMethod.Delete, $"/api/server/{created.Id}", xsrf);
        var resp = await session.Client.SendAsync(req, ct);
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);

        var list = await session.Client.GetFromJsonAsync<ServerEndpoints.ListServersResponse>("/api/server", ct);
        Assert.DoesNotContain(list!.Servers, s => s.Id == created.Id);
    }

    // ── test connection ───────────────────────────────────────────────────────

    [Fact]
    public async Task TestConnection_Read_Succeeds_AgainstBlue()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf) = await AuthorizedSessionAsync(ct);
        using var _ = session;

        var connStr = await factory.InitialisedApp.GetConnectionStringAsync("blue-appdb", ct)
                      ?? throw new InvalidOperationException("blue-appdb not found");
        var pg = new NpgsqlConnectionStringBuilder(connStr);

        var created = await CreateServerAsync(session, xsrf, UniqueName(), pg.Host!, pg.Port, ct);

        using var req = MutationRequest(HttpMethod.Post, $"/api/server/{created.Id}/test", xsrf);
        var resp = await session.Client.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        var body = await resp.Content.ReadFromJsonAsync<ServerEndpoints.TestConnectionResponse>(ct);
        Assert.True(body!.Read.Ok, body.Read.Error);
        Assert.True(body.Write?.Ok, body.Write?.Error);
    }

    [Fact]
    public async Task TestConnection_Write_IsNull_ForReadOnlyServer()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf) = await AuthorizedSessionAsync(ct);
        using var _ = session;

        var name = UniqueName();
        using var createReq = MutationRequest(
            HttpMethod.Post,
            "/api/server",
            xsrf,
            new ServerEndpoints.CreateServerRequest(
                name,
                "postgres",
                "localhost",
                5432,
                "appdb",
                "reader_blue",
                "reader_blue"
            )
        );
        var createResp = await session.Client.SendAsync(createReq, ct);
        createResp.EnsureSuccessStatusCode();
        var created = await createResp.Content.ReadFromJsonAsync<ServerEndpoints.ServerResponse>(ct);

        using var req = MutationRequest(HttpMethod.Post, $"/api/server/{created!.Id}/test", xsrf);
        var resp = await session.Client.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        var body = await resp.Content.ReadFromJsonAsync<ServerEndpoints.TestConnectionResponse>(ct);
        Assert.Null(body!.Write);
    }

    [Fact]
    public async Task TestConnection_BadHost_ReturnsOkFalse()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf) = await AuthorizedSessionAsync(ct);
        using var _ = session;

        using var createReq = MutationRequest(
            HttpMethod.Post,
            "/api/server",
            xsrf,
            new ServerEndpoints.CreateServerRequest(
                UniqueName(),
                "postgres",
                "no-such-host-xyz.invalid",
                5432,
                "appdb",
                "r",
                "p"
            )
        );
        var createResp = await session.Client.SendAsync(createReq, ct);
        createResp.EnsureSuccessStatusCode();
        var created = await createResp.Content.ReadFromJsonAsync<ServerEndpoints.ServerResponse>(ct);

        using var req = MutationRequest(HttpMethod.Post, $"/api/server/{created!.Id}/test", xsrf);
        var resp = await session.Client.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        var body = await resp.Content.ReadFromJsonAsync<ServerEndpoints.TestConnectionResponse>(ct);
        Assert.False(body!.Read.Ok);
        Assert.NotNull(body.Read.Error);
    }

    // ── response types ────────────────────────────────────────────────────────

    private sealed record ListUserBody(UserRow[] Users);

    private sealed record UserRow(string Id, string Email);
}