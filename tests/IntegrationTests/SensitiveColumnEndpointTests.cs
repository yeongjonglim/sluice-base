using System.Net;
using System.Net.Http.Json;
using Aspire.Hosting.Testing;
using IntegrationTests.Supports;
using Npgsql;
using SluiceBase.Core.Permissions;

namespace IntegrationTests;

public class SensitiveColumnEndpointTests(SluiceBaseStackFactory factory)
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

    private async Task<(AuthenticatedSession Session, string Xsrf, string DatabaseId, string AliceId)>
        AliceWithDatabaseAsync(CancellationToken ct)
    {
        var session = await LoginHelper.SignInAsync("alice", "dev", ct);
        var xsrf = await session.FetchXsrfTokenAsync(ct);

        var users = await session.Client.GetFromJsonAsync<ListUserBody>("/api/admin/user", ct);
        var alice = Assert.Single(users!.Users, u => u.Email == "alice@example.com");

        using var grantServer = MutationRequest(HttpMethod.Post,
            $"/api/admin/user/{alice.Id}/permission", xsrf,
            new { permission = Permissions.ServerManage });
        (await session.Client.SendAsync(grantServer, ct)).EnsureSuccessStatusCode();

        var blueConnStr = await factory.InitialisedApp.GetConnectionStringAsync("blue-appdb", ct);
        var blueBuilder = new NpgsqlConnectionStringBuilder(blueConnStr!);

        var serverName = $"sc-{Guid.NewGuid():N}"[..20];
        using var sReq = MutationRequest(HttpMethod.Post, "/api/server", xsrf,
            new { name = serverName, kind = "postgres", host = blueBuilder.Host, port = blueBuilder.Port });
        var sResp = await session.Client.SendAsync(sReq, ct);
        sResp.EnsureSuccessStatusCode();
        var server = (await sResp.Content.ReadFromJsonAsync<ServerBody>(ct))!;

        using var cReq = MutationRequest(HttpMethod.Post, $"/api/server/{server.Id}/credential", xsrf,
            new { label = "read", username = blueBuilder.Username, password = blueBuilder.Password });
        var cResp = await session.Client.SendAsync(cReq, ct);
        cResp.EnsureSuccessStatusCode();
        var cred = (await cResp.Content.ReadFromJsonAsync<CredentialBody>(ct))!;

        using var dbReq = MutationRequest(HttpMethod.Post, $"/api/server/{server.Id}/database", xsrf,
            new { displayName = "App DB", databaseName = blueBuilder.Database ?? "appdb", readCredentialId = cred.Id });
        var dbResp = await session.Client.SendAsync(dbReq, ct);
        dbResp.EnsureSuccessStatusCode();
        var db = (await dbResp.Content.ReadFromJsonAsync<DatabaseBody>(ct))!;

        return (session, xsrf, db.Id, alice.Id);
    }

    [Fact]
    public async Task ListSensitiveColumns_Anonymous_Returns401()
    {
        using var client = factory.InitialisedApp.CreateHttpClient("api", "https");
        var resp = await client.GetAsync(
            $"/api/admin/database/{Guid.NewGuid()}/sensitive-column",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task ListSensitiveColumns_Bob_Returns403()
    {
        using var session = await LoginHelper.SignInAsync("bob", "dev", TestContext.Current.CancellationToken);
        var resp = await session.Client.GetAsync(
            $"/api/admin/database/{Guid.NewGuid()}/sensitive-column",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task MarkAndList_RoundTrip()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf, databaseId, _) = await AliceWithDatabaseAsync(ct);
        using var _ = session;

        using var markReq = MutationRequest(HttpMethod.Post,
            $"/api/admin/database/{databaseId}/sensitive-column", xsrf,
            new { schemaName = "public", tableName = "user", columnName = "email" });
        var markResp = await session.Client.SendAsync(markReq, ct);
        Assert.Equal(HttpStatusCode.Created, markResp.StatusCode);

        var list = await session.Client.GetFromJsonAsync<SensitiveColumnTestHelper.SensitiveColumnListBody>(
            $"/api/admin/database/{databaseId}/sensitive-column", ct);
        Assert.Single(list!.Columns, c =>
            c.SchemaName == "public" && c.TableName == "user" && c.ColumnName == "email");
    }

    [Fact]
    public async Task Mark_Duplicate_Returns200()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf, databaseId, _) = await AliceWithDatabaseAsync(ct);
        using var _ = session;

        for (var i = 0; i < 2; i++)
        {
            using var req = MutationRequest(HttpMethod.Post,
                $"/api/admin/database/{databaseId}/sensitive-column", xsrf,
                new { schemaName = "public", tableName = "server_database", columnName = "database_name" });
            var resp = await session.Client.SendAsync(req, ct);
            Assert.True(resp.IsSuccessStatusCode);
        }
    }

    [Fact]
    public async Task Unmark_RemovesColumn()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf, databaseId, _) = await AliceWithDatabaseAsync(ct);
        using var _ = session;

        var columnId = await SensitiveColumnTestHelper.MarkColumnAsync(
            session, databaseId, "public", "user", "name", xsrf, ct);

        using var deleteReq = MutationRequest(
            HttpMethod.Delete, $"/api/admin/database/{databaseId}/sensitive-column/{columnId}", xsrf);
        (await session.Client.SendAsync(deleteReq, ct)).EnsureSuccessStatusCode();

        var list = await session.Client.GetFromJsonAsync<SensitiveColumnTestHelper.SensitiveColumnListBody>(
            $"/api/admin/database/{databaseId}/sensitive-column", ct);
        Assert.DoesNotContain(list!.Columns, c => c.Id == columnId);
    }

    [Fact]
    public async Task GrantAndRevokeBypass_RoundTrip()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf, databaseId, aliceId) = await AliceWithDatabaseAsync(ct);
        using var _ = session;

        var columnId = await SensitiveColumnTestHelper.MarkColumnAsync(
            session, databaseId, "public", "user", "email", xsrf, ct);

        await SensitiveColumnTestHelper.GrantBypassAsync(session, databaseId, columnId, aliceId, xsrf, ct);

        var list = await session.Client.GetFromJsonAsync<FullSensitiveColumnListBody>(
            $"/api/admin/database/{databaseId}/sensitive-column", ct);
        var col = Assert.Single(list!.Columns, c => c.Id == columnId);
        Assert.Single(col.Bypasses, b => b.UserId == aliceId);

        using var revokeReq = MutationRequest(
            HttpMethod.Delete,
            $"/api/admin/database/{databaseId}/sensitive-column/{columnId}/bypass/{aliceId}", xsrf);
        (await session.Client.SendAsync(revokeReq, ct)).EnsureSuccessStatusCode();

        var listAfter = await session.Client.GetFromJsonAsync<FullSensitiveColumnListBody>(
            $"/api/admin/database/{databaseId}/sensitive-column", ct);
        var colAfter = Assert.Single(listAfter!.Columns, c => c.Id == columnId);
        Assert.Empty(colAfter.Bypasses);
    }

    private sealed record ListUserBody(UserRow[] Users);
    private sealed record UserRow(string Id, string Email);
    private sealed record ServerBody(string Id);
    private sealed record CredentialBody(string Id);
    private sealed record DatabaseBody(string Id);
    private sealed record FullSensitiveColumnListBody(FullSensitiveColumnRow[] Columns);
    private sealed record FullSensitiveColumnRow(string Id, string SchemaName, string TableName, string ColumnName, BypassRow[] Bypasses);
    private sealed record BypassRow(string UserId);
}
