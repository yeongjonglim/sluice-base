using System.Net;
using System.Net.Http.Json;
using Aspire.Hosting.Testing;
using IntegrationTests.Supports;
using Npgsql;
using SluiceBase.Api.Endpoints;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Schemas;

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

    private async Task<(AuthenticatedSession session, string serverId)> AuthorizedSessionWithBlueServerAsync(
        CancellationToken ct)
    {
        var session = await LoginHelper.SignInAsync("alice", "dev", ct);
        var xsrf = await session.FetchXsrfTokenAsync(ct);

        var users = await session.Client.GetFromJsonAsync<ListUsersResponse>("/api/admin/user", ct);
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
        using var createReq = MutationRequest(HttpMethod.Post, "/api/server", xsrf,
            new ServerEndpoints.CreateServerRequest(
                serverName, "postgres",
                blueBuilder.Host!, blueBuilder.Port, "appdb",
                "reader_blue", "reader_blue"));
        var createResp = await session.Client.SendAsync(createReq, ct);
        createResp.EnsureSuccessStatusCode();
        var server = await createResp.Content.ReadFromJsonAsync<ServerEndpoints.ServerResponse>(ct);

        return (session, server!.Id.Value.ToString());
    }

    [Fact]
    public async Task GetSchema_ReturnsTree_ForBlueServer()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, serverId) = await AuthorizedSessionWithBlueServerAsync(ct);
        using var _ = session;

        var resp = await session.Client.GetAsync($"/api/schema/{serverId}", ct);
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
        using var session = await LoginHelper.SignInAsync(
            "bob", "dev", TestContext.Current.CancellationToken);
        var resp = await session.Client.GetAsync(
            $"/api/schema/{Guid.NewGuid()}",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task GetSchema_Returns404_ForUnknownServer()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = await LoginHelper.SignInAsync("alice", "dev", ct);
        using var _ = session;
        var xsrf = await session.FetchXsrfTokenAsync(ct);

        var users = await session.Client.GetFromJsonAsync<ListUsersResponse>("/api/admin/user", ct);
        var alice = users!.Users.Single(u => u.Email == "alice@example.com");
        using var grant = MutationRequest(HttpMethod.Post,
            $"/api/admin/user/{alice.Id}/permission", xsrf,
            new { permission = Permissions.QueryExecute });
        (await session.Client.SendAsync(grant, ct)).EnsureSuccessStatusCode();

        var resp = await session.Client.GetAsync($"/api/schema/{Guid.NewGuid()}", ct);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}