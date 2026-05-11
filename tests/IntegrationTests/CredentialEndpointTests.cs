using System.Net;
using System.Net.Http.Json;
using IntegrationTests.Supports;
using SluiceBase.Api.Endpoints;
using SluiceBase.Core.Permissions;

namespace IntegrationTests;

public class CredentialEndpointTests(SluiceBaseStackFactory factory)
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

    private static async Task<ServerEndpoints.ServerResponse> CreateServerAsync(
        AuthenticatedSession session, string xsrf, CancellationToken ct)
    {
        using var req = MutationRequest(HttpMethod.Post,
            "/api/server",
            xsrf,
            new ServerEndpoints.CreateServerRequest(UniqueName(), "postgres", "localhost", 5432));
        var resp = await session.Client.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<ServerEndpoints.ServerResponse>(ct))!;
    }

    private static async Task<CredentialEndpoints.CredentialResponse> AddCredentialAsync(
        AuthenticatedSession session, string xsrf, Guid serverId, string label, CancellationToken ct)
    {
        using var req = MutationRequest(HttpMethod.Post,
            $"/api/server/{serverId}/credential",
            xsrf,
            new CredentialEndpoints.AddCredentialRequest(label, "user_" + label, "pass_" + label));
        var resp = await session.Client.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<CredentialEndpoints.CredentialResponse>(ct))!;
    }

    [Fact]
    public async Task AddCredential_HappyPath_NeverReturnsPassword()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf) = await AuthorizedSessionAsync(ct);
        using var _ = session;
        var server = await CreateServerAsync(session, xsrf, ct);

        using var req = MutationRequest(HttpMethod.Post,
            $"/api/server/{server.Id}/credential",
            xsrf,
            new CredentialEndpoints.AddCredentialRequest("My cred", "alice", "s3cr3t"));
        var resp = await session.Client.SendAsync(req, ct);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

        var raw = await resp.Content.ReadAsStringAsync(ct);
        Assert.DoesNotContain("s3cr3t", raw);
    }

    [Fact]
    public async Task UpdateCredential_ChangesLabel()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf) = await AuthorizedSessionAsync(ct);
        using var _ = session;
        var server = await CreateServerAsync(session, xsrf, ct);
        var cred = await AddCredentialAsync(session, xsrf, server.Id.Value, "original", ct);

        using var req = MutationRequest(HttpMethod.Put,
            $"/api/server/{server.Id}/credential/{cred.Id}",
            xsrf,
            new CredentialEndpoints.UpdateCredentialRequest("updated", "user_updated"));
        var resp = await session.Client.SendAsync(req, ct);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<CredentialEndpoints.CredentialResponse>(ct);
        Assert.Equal("updated", body!.Label);
    }

    [Fact]
    public async Task DeleteCredential_ReferencedByActiveDatabase_Returns409()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf) = await AuthorizedSessionAsync(ct);
        using var _ = session;
        var server = await CreateServerAsync(session, xsrf, ct);
        var cred = await AddCredentialAsync(session, xsrf, server.Id.Value, "read", ct);

        // Create a database that references this credential
        using var dbReq = MutationRequest(HttpMethod.Post,
            $"/api/server/{server.Id}/database",
            xsrf,
            new DatabaseEndpoints.AddDatabaseRequest("App DB", "appdb", cred.Id));
        (await session.Client.SendAsync(dbReq, ct)).EnsureSuccessStatusCode();

        // Now try to delete the credential — should be blocked
        using var delReq = MutationRequest(HttpMethod.Delete, $"/api/server/{server.Id}/credential/{cred.Id}", xsrf);
        Assert.Equal(HttpStatusCode.Conflict, (await session.Client.SendAsync(delReq, ct)).StatusCode);
    }

    [Fact]
    public async Task DeleteCredential_AfterDatabaseSoftDeleted_Succeeds()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf) = await AuthorizedSessionAsync(ct);
        using var _ = session;
        var server = await CreateServerAsync(session, xsrf, ct);
        var cred = await AddCredentialAsync(session, xsrf, server.Id.Value, "read", ct);

        // Create then soft-delete the database
        using var dbReq = MutationRequest(HttpMethod.Post,
            $"/api/server/{server.Id}/database",
            xsrf,
            new DatabaseEndpoints.AddDatabaseRequest("App DB", "appdb", cred.Id));
        var dbResp = await session.Client.SendAsync(dbReq, ct);
        var db = (await dbResp.Content.ReadFromJsonAsync<DatabaseEndpoints.DatabaseResponse>(ct))!;

        using var dbDelReq = MutationRequest(HttpMethod.Delete, $"/api/server/{server.Id}/database/{db.Id}", xsrf);
        (await session.Client.SendAsync(dbDelReq, ct)).EnsureSuccessStatusCode();

        // Now the credential can be soft-deleted
        using var credDelReq = MutationRequest(HttpMethod.Delete, $"/api/server/{server.Id}/credential/{cred.Id}", xsrf);
        Assert.Equal(HttpStatusCode.NoContent, (await session.Client.SendAsync(credDelReq, ct)).StatusCode);
    }

    private sealed record ListUserBody(UserRow[] Users);

    private sealed record UserRow(string Id, string Email);
}