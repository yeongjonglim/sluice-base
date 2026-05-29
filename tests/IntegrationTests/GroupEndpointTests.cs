using System.Net;
using System.Net.Http.Json;
using Aspire.Hosting.Testing;
using IntegrationTests.Supports;
using SluiceBase.Core.Permissions;

namespace IntegrationTests;

public class GroupEndpointTests(SluiceBaseStackFactory factory)
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

    private async Task<(AuthenticatedSession Session, string Xsrf)>
        AliceWithGroupManageAsync(CancellationToken ct)
    {
        var session = await LoginHelper.SignInAsync("alice", "dev", ct);
        var xsrf = await session.FetchXsrfTokenAsync(ct);

        var users = await session.Client.GetFromJsonAsync<ListUserBody>("/api/admin/user", ct);
        var alice = Assert.Single(users!.Users, u => u.Email == "alice@example.com");

        using var grantReq = MutationRequest(HttpMethod.Post,
            $"/api/admin/user/{alice.Id}/permission", xsrf,
            new { permission = Permissions.GroupManage });
        (await session.Client.SendAsync(grantReq, ct)).EnsureSuccessStatusCode();

        // Re-login to pick up new permission
        session.Dispose();
        session = await LoginHelper.SignInAsync("alice", "dev", ct);
        xsrf = await session.FetchXsrfTokenAsync(ct);

        return (session, xsrf);
    }

    // ── anonymous / unauthorized ─────────────────────────────────────────────

    [Fact]
    public async Task ListGroups_Anonymous_Returns401()
    {
        using var client = factory.InitialisedApp.CreateHttpClient("api", "https");
        var resp = await client.GetAsync("/api/admin/group", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task ListGroups_Bob_Returns403()
    {
        using var session = await LoginHelper.SignInAsync("bob", "dev", TestContext.Current.CancellationToken);
        var resp = await session.Client.GetAsync("/api/admin/group", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    // ── CRUD ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateGroup_HappyPath_Returns201()
    {
        var ct = TestContext.Current.CancellationToken;
        var (alice, xsrf) = await AliceWithGroupManageAsync(ct);
        using var _ = alice;

        var groupName = $"test-{Guid.NewGuid():N}"[..20];
        using var req = MutationRequest(HttpMethod.Post, "/api/admin/group", xsrf,
            new { name = groupName, description = "Integration test group" });
        var resp = await alice.Client.SendAsync(req, ct);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<GroupBody>(ct);
        Assert.Equal(groupName, body!.Name);

        await GroupTestHelper.DeleteGroupAsync(alice, body.Id, xsrf, ct);
    }

    [Fact]
    public async Task CreateGroup_DuplicateName_Returns400()
    {
        var ct = TestContext.Current.CancellationToken;
        var (alice, xsrf) = await AliceWithGroupManageAsync(ct);
        using var _ = alice;

        var groupName = $"dup-{Guid.NewGuid():N}"[..20];
        var groupId = await GroupTestHelper.CreateGroupAsync(alice, groupName, xsrf, ct);

        using var req = MutationRequest(HttpMethod.Post, "/api/admin/group", xsrf,
            new { name = groupName });
        var resp = await alice.Client.SendAsync(req, ct);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);

        await GroupTestHelper.DeleteGroupAsync(alice, groupId, xsrf, ct);
    }

    [Fact]
    public async Task DeleteGroup_HappyPath_Returns204()
    {
        var ct = TestContext.Current.CancellationToken;
        var (alice, xsrf) = await AliceWithGroupManageAsync(ct);
        using var _ = alice;

        var groupId = await GroupTestHelper.CreateGroupAsync(alice, $"del-{Guid.NewGuid():N}"[..20], xsrf, ct);

        using var req = MutationRequest(HttpMethod.Delete, $"/api/admin/group/{groupId}", xsrf);
        var resp = await alice.Client.SendAsync(req, ct);
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
    }

    // ── membership ───────────────────────────────────────────────────────────

    [Fact]
    public async Task AddMember_HappyPath_Returns201AndAppearsInList()
    {
        var ct = TestContext.Current.CancellationToken;
        var (alice, xsrf) = await AliceWithGroupManageAsync(ct);
        using var _ = alice;

        using var bob = await LoginHelper.SignInAsync("bob", "dev", ct);
        await bob.Client.GetAsync("/api/me", ct);

        var users = await alice.Client.GetFromJsonAsync<ListUserBody>("/api/admin/user", ct);
        var bobUser = Assert.Single(users!.Users, u => u.Email == "bob@example.com");

        var groupId = await GroupTestHelper.CreateGroupAsync(alice, $"mem-{Guid.NewGuid():N}"[..20], xsrf, ct);

        await GroupTestHelper.AddMemberAsync(alice, groupId, bobUser.Id, xsrf, ct);

        var members = await alice.Client.GetFromJsonAsync<MemberListBody>(
            $"/api/admin/group/{groupId}/member", ct);
        Assert.Contains(members!.Members, m => m.UserId == bobUser.Id);

        await GroupTestHelper.DeleteGroupAsync(alice, groupId, xsrf, ct);
    }

    // ── group permission grants flow through to /api/me ──────────────────────

    [Fact]
    public async Task GroupPermission_FlowsThroughToMe()
    {
        var ct = TestContext.Current.CancellationToken;
        var (alice, xsrf) = await AliceWithGroupManageAsync(ct);
        using var _ = alice;

        using var bob = await LoginHelper.SignInAsync("bob", "dev", ct);
        await bob.Client.GetAsync("/api/me", ct);

        var users = await alice.Client.GetFromJsonAsync<ListUserBody>("/api/admin/user", ct);
        var bobUser = Assert.Single(users!.Users, u => u.Email == "bob@example.com");

        var groupId = await GroupTestHelper.CreateGroupAsync(alice, $"perm-{Guid.NewGuid():N}"[..20], xsrf, ct);
        await GroupTestHelper.AddMemberAsync(alice, groupId, bobUser.Id, xsrf, ct);
        await GroupTestHelper.GrantGroupPermissionAsync(alice, groupId, Permissions.ServerManage, xsrf, ct);

        // Bob should now have server:manage via group
        using var bob2 = await LoginHelper.SignInAsync("bob", "dev", ct);
        var me = await bob2.Client.GetFromJsonAsync<MeBody>("/api/me", ct);
        Assert.Contains(Permissions.ServerManage, me!.Permissions);

        await GroupTestHelper.DeleteGroupAsync(alice, groupId, xsrf, ct);
    }

    // ── response records ─────────────────────────────────────────────────────

    private sealed record MeBody(string Id, string Email, string? Name, string[] Permissions);
    private sealed record GroupBody(string Id, string Name);
    private sealed record GroupListBody(GroupBody[] Groups);
    private sealed record MemberItem(string UserId, string? UserEmail);
    private sealed record MemberListBody(MemberItem[] Members);
    private sealed record UserRow(string Id, string Email);
    private sealed record ListUserBody(UserRow[] Users);
}
