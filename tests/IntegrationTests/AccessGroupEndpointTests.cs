using System.Net;
using System.Net.Http.Json;
using IntegrationTests.Supports;
using SluiceBase.Core.Permissions;

namespace IntegrationTests;

public class AccessGroupEndpointTests(SluiceBaseStackFactory factory)
{
    private KeycloakLoginHelper LoginHelper => new(factory.InitialisedApp);

    private static HttpRequestMessage Mutation(HttpMethod method, string url, string xsrf, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Add("X-XSRF-TOKEN", xsrf);
        if (body is not null)
        {
            req.Content = JsonContent.Create(body);
        }
        return req;
    }

    [Fact]
    public async Task CreateListAndDeleteGroup_RoundTrips()
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = await LoginHelper.SignInAsync("alice", "dev", ct); // alice is bootstrap admin
        var xsrf = await admin.FetchXsrfTokenAsync(ct);

        var name = $"grp-{Guid.NewGuid():N}"[..16];
        var create = Mutation(HttpMethod.Post, "/api/admin/group", xsrf, new { name, description = "desc" });
        var createResp = await admin.Client.SendAsync(create, ct);
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);

        var list = await admin.Client.GetFromJsonAsync<GroupListBody>("/api/admin/group", ct);
        var created = Assert.Single(list!.Groups, g => g.Name == name);

        var del = Mutation(HttpMethod.Delete, $"/api/admin/group/{created.Id}", xsrf);
        (await admin.Client.SendAsync(del, ct)).EnsureSuccessStatusCode();

        var afterList = await admin.Client.GetFromJsonAsync<GroupListBody>("/api/admin/group", ct);
        Assert.DoesNotContain(afterList!.Groups, g => g.Name == name);
    }

    [Fact]
    public async Task GrantGlobalPermission_RejectsNonGlobal()
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = await LoginHelper.SignInAsync("alice", "dev", ct);
        var xsrf = await admin.FetchXsrfTokenAsync(ct);

        var name = $"grp-{Guid.NewGuid():N}"[..16];
        (await admin.Client.SendAsync(Mutation(HttpMethod.Post, "/api/admin/group", xsrf, new { name }), ct))
            .EnsureSuccessStatusCode();
        var list = await admin.Client.GetFromJsonAsync<GroupListBody>("/api/admin/group", ct);
        var group = Assert.Single(list!.Groups, g => g.Name == name);

        // query:execute is scopeable, not global → 400
        var bad = Mutation(HttpMethod.Post, $"/api/admin/group/{group.Id}/permission/{Permissions.QueryExecute}", xsrf);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Client.SendAsync(bad, ct)).StatusCode);

        // server:manage is global → 201
        var ok = Mutation(HttpMethod.Post, $"/api/admin/group/{group.Id}/permission/{Permissions.ServerManage}", xsrf);
        Assert.Equal(HttpStatusCode.Created, (await admin.Client.SendAsync(ok, ct)).StatusCode);
    }

    private sealed record GroupListBody(IReadOnlyList<GroupSummaryBody> Groups);
    private sealed record GroupSummaryBody(string Id, string Name, string? Description, int MemberCount);
}
