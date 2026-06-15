using System.Net;
using System.Net.Http.Json;
using Aspire.Hosting.Testing;
using IntegrationTests.Supports;
using Npgsql;
using SluiceBase.Api.Endpoints;
using SluiceBase.Api.Services;
using SluiceBase.Core.Permissions;

namespace IntegrationTests;

/// <summary>
/// Verifies that <see cref="ICatalogService"/> produces the correct filtered view for
/// non-admin users. These tests call the catalog HTTP endpoint (which delegates to the
/// service) so that behavior is validated end-to-end without bypassing the DI wiring.
/// </summary>
public class CatalogServiceTests(SluiceBaseStackFactory factory)
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
    /// Seeds a server + database as alice (server-admin) and returns identifiers needed by tests.
    /// </summary>
    private async Task<(AuthenticatedSession AdminSession, string AdminXsrf, string ServerId, string DatabaseId)>
        AdminSessionWithServerAndDatabaseAsync(CancellationToken ct)
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

        var serverName = $"svc-{Guid.NewGuid():N}"[..24];
        using var sReq = MutationRequest(HttpMethod.Post, "/api/server", xsrf,
            new ServerEndpoints.CreateServerRequest(serverName, "postgres", blueBuilder.Host!, blueBuilder.Port));
        var sResp = await session.Client.SendAsync(sReq, ct);
        sResp.EnsureSuccessStatusCode();
        var server = (await sResp.Content.ReadFromJsonAsync<ServerBody>(ct))!;

        using var cReq = MutationRequest(HttpMethod.Post, $"/api/server/{server.Id}/credential", xsrf,
            new { label = "read", username = blueBuilder.Username, password = blueBuilder.Password });
        var cResp = await session.Client.SendAsync(cReq, ct);
        cResp.EnsureSuccessStatusCode();
        var cred = (await cResp.Content.ReadFromJsonAsync<CredentialBody>(ct))!;

        using var dbReq = MutationRequest(HttpMethod.Post, $"/api/server/{server.Id}/database", xsrf,
            new { displayName = "svc-db", databaseName = blueBuilder.Database ?? "postgres", readCredentialId = cred.Id });
        var dbResp = await session.Client.SendAsync(dbReq, ct);
        dbResp.EnsureSuccessStatusCode();
        var db = (await dbResp.Content.ReadFromJsonAsync<DatabaseBody>(ct))!;

        return (session, xsrf, server.Id, db.Id);
    }

    [Fact]
    public async Task ListAccessible_NonAdminWithQueryExecuteOnOneDatabase_ReturnsExactlyThatDatabaseUnderItsServer()
    {
        var ct = TestContext.Current.CancellationToken;
        var (adminSession, adminXsrf, serverId, databaseId) = await AdminSessionWithServerAndDatabaseAsync(ct);
        using var admin = adminSession;

        // Ensure bob exists
        using var bobSession = await LoginHelper.SignInAsync("bob", "dev", ct);
        await bobSession.Client.GetAsync("/api/me", ct);

        // Revoke all of bob's existing roles so we start from a clean slate
        await PermissionTestHelper.RevokeAllDatabaseRolesAsync(admin, "bob@example.com", adminXsrf, ct);

        var users = await admin.Client.GetFromJsonAsync<ListUserBody>("/api/admin/user", ct);
        var bob = users!.Users.Single(u => u.Email == "bob@example.com");

        // Grant query:execute on exactly the one database we created
        await DatabaseRoleTestHelper.AssignByDatabaseAsync(admin, bob.Id, Permissions.QueryExecute, databaseId, adminXsrf, ct);

        // ICatalogService is invoked by the endpoint — validate its output via the HTTP response
        var resp = await bobSession.Client.GetAsync("/api/catalog/server", ct);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<CatalogEndpoints.CatalogServersResponse>(ct);
        Assert.NotNull(body);

        // Bob should see exactly one server
        var server = Assert.Single(body.Servers);
        Assert.Equal(serverId, server.Id.ToString());

        // That server should contain exactly the one database
        var database = Assert.Single(server.Databases);
        Assert.Equal(databaseId, database.Id.ToString());
    }

    private sealed record ListUserBody(UserRow[] Users);
    private sealed record UserRow(string Id, string Email);
    private sealed record ServerBody(string Id, string Name);
    private sealed record CredentialBody(string Id, string Label);
    private sealed record DatabaseBody(string Id, string DisplayName);
}
