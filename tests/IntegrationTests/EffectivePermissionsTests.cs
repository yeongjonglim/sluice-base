using System.Net;
using System.Net.Http.Json;
using IntegrationTests.Supports;
using SluiceBase.Core.Permissions;

namespace IntegrationTests;

public class EffectivePermissionsTests(SluiceBaseStackFactory factory)
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

    // Sign in as alice and grant her permission:manage (the single admin permission).
    private async Task<(AuthenticatedSession Session, string Xsrf)> AliceAdminAsync(CancellationToken ct)
    {
        var session = await LoginHelper.SignInAsync("alice", "dev", ct);
        var xsrf = await session.FetchXsrfTokenAsync(ct);

        var users = await session.Client.GetFromJsonAsync<ListUserBody>("/api/admin/user", ct);
        var alice = Assert.Single(users!.Users, u => u.Email == "alice@example.com");

        using var grantReq = MutationRequest(HttpMethod.Post,
            $"/api/admin/user/{alice.Id}/permission", xsrf,
            new { permission = Permissions.PermissionManage });
        (await session.Client.SendAsync(grantReq, ct)).EnsureSuccessStatusCode();

        // Re-login to pick up the new permission.
        session.Dispose();
        session = await LoginHelper.SignInAsync("alice", "dev", ct);
        xsrf = await session.FetchXsrfTokenAsync(ct);
        return (session, xsrf);
    }

    // Ensure bob exists (first /api/me materializes the user) and return his id.
    private async Task<string> EnsureBobAsync(AuthenticatedSession admin, CancellationToken ct)
    {
        using var bob = await LoginHelper.SignInAsync("bob", "dev", ct);
        await bob.Client.GetAsync("/api/me", ct);

        var users = await admin.Client.GetFromJsonAsync<ListUserBody>("/api/admin/user", ct);
        var bobUser = Assert.Single(users!.Users, u => u.Email == "bob@example.com");
        return bobUser.Id;
    }

    [Fact]
    public async Task NonAdmin_Returns403()
    {
        var ct = TestContext.Current.CancellationToken;
        using var bob = await LoginHelper.SignInAsync("bob", "dev", ct);
        var resp = await bob.Client.GetAsync($"/api/admin/user/{Guid.NewGuid()}/effective", ct);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task NonexistentUser_Returns404()
    {
        var ct = TestContext.Current.CancellationToken;
        var (alice, _) = await AliceAdminAsync(ct);
        using var _disposable = alice;

        var resp = await alice.Client.GetAsync($"/api/admin/user/{Guid.NewGuid()}/effective", ct);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task DirectGrant_ReturnsDirectSource()
    {
        var ct = TestContext.Current.CancellationToken;
        var (alice, xsrf) = await AliceAdminAsync(ct);
        using var _disposable = alice;

        var bobId = await EnsureBobAsync(alice, ct);

        using var grant = MutationRequest(HttpMethod.Post,
            $"/api/admin/user/{bobId}/permission", xsrf,
            new { permission = Permissions.ServerManage });
        (await alice.Client.SendAsync(grant, ct)).EnsureSuccessStatusCode();

        var body = await alice.Client.GetFromJsonAsync<EffectiveBody>(
            $"/api/admin/user/{bobId}/effective", ct);

        var item = Assert.Single(body!.Global, g => g.Permission == Permissions.ServerManage);
        Assert.Contains(item.Sources, s => s.Direct && s.Group is null);
    }

    [Fact]
    public async Task GroupGrant_ReturnsGroupSource()
    {
        var ct = TestContext.Current.CancellationToken;
        var (alice, xsrf) = await AliceAdminAsync(ct);
        using var _disposable = alice;

        var bobId = await EnsureBobAsync(alice, ct);
        var groupName = $"eng-{Guid.NewGuid():N}"[..20];
        var groupId = await GroupTestHelper.CreateGroupAsync(alice, groupName, xsrf, ct);
        await GroupTestHelper.AddMemberAsync(alice, groupId, bobId, xsrf, ct);
        await GroupTestHelper.GrantGroupPermissionAsync(alice, groupId, Permissions.ServerManage, xsrf, ct);

        var body = await alice.Client.GetFromJsonAsync<EffectiveBody>(
            $"/api/admin/user/{bobId}/effective", ct);

        var item = Assert.Single(body!.Global, g => g.Permission == Permissions.ServerManage);
        Assert.Contains(item.Sources, s => !s.Direct && s.Group != null && s.Group.Name == groupName);

        await GroupTestHelper.DeleteGroupAsync(alice, groupId, xsrf, ct);
    }

    [Fact]
    public async Task DirectAndGroup_ReturnsBothSources()
    {
        var ct = TestContext.Current.CancellationToken;
        var (alice, xsrf) = await AliceAdminAsync(ct);
        using var _disposable = alice;

        var bobId = await EnsureBobAsync(alice, ct);

        using var grant = MutationRequest(HttpMethod.Post,
            $"/api/admin/user/{bobId}/permission", xsrf,
            new { permission = Permissions.ServerManage });
        (await alice.Client.SendAsync(grant, ct)).EnsureSuccessStatusCode();

        var groupName = $"eng-{Guid.NewGuid():N}"[..20];
        var groupId = await GroupTestHelper.CreateGroupAsync(alice, groupName, xsrf, ct);
        await GroupTestHelper.AddMemberAsync(alice, groupId, bobId, xsrf, ct);
        await GroupTestHelper.GrantGroupPermissionAsync(alice, groupId, Permissions.ServerManage, xsrf, ct);

        var body = await alice.Client.GetFromJsonAsync<EffectiveBody>(
            $"/api/admin/user/{bobId}/effective", ct);

        var item = Assert.Single(body!.Global, g => g.Permission == Permissions.ServerManage);
        Assert.Contains(item.Sources, s => s.Direct && s.Group is null);
        Assert.Contains(item.Sources, s => !s.Direct && s.Group != null && s.Group.Name == groupName);

        await GroupTestHelper.DeleteGroupAsync(alice, groupId, xsrf, ct);
    }

    [Fact]
    public async Task IncludesMemberships()
    {
        var ct = TestContext.Current.CancellationToken;
        var (alice, xsrf) = await AliceAdminAsync(ct);
        using var _disposable = alice;

        var bobId = await EnsureBobAsync(alice, ct);
        var groupName = $"mem-{Guid.NewGuid():N}"[..20];
        var groupId = await GroupTestHelper.CreateGroupAsync(alice, groupName, xsrf, ct);
        await GroupTestHelper.AddMemberAsync(alice, groupId, bobId, xsrf, ct);

        var body = await alice.Client.GetFromJsonAsync<EffectiveBody>(
            $"/api/admin/user/{bobId}/effective", ct);

        Assert.Contains(body!.Memberships, m => m.GroupId == groupId && m.GroupName == groupName);

        await GroupTestHelper.DeleteGroupAsync(alice, groupId, xsrf, ct);
    }

    private sealed record GroupInfoBody(string Id, string Name);
    private sealed record SourceBody(bool Direct, GroupInfoBody? Group);
    private sealed record GlobalItemBody(string Permission, SourceBody[] Sources);
    private sealed record MembershipBody(string GroupId, string GroupName);
    private sealed record EffectiveBody(
        GlobalItemBody[] Global,
        object[] DatabaseRoles,
        object[] ColumnBypasses,
        MembershipBody[] Memberships);

    private sealed record UserRow(string Id, string Email);
    private sealed record ListUserBody(UserRow[] Users);
}
