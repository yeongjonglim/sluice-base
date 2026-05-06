using System.Net;
using System.Net.Http.Json;
using Aspire.Hosting.Testing;
using IntegrationTests.Supports;
using SluiceBase.Core.Permissions;

namespace IntegrationTests;

public class AdminPermissionTests(SluiceBaseStackFactory factory)
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private KeycloakLoginHelper LoginHelper => new(factory.InitialisedApp);

    private static async Task<string> GetXsrfAsync(
        AuthenticatedSession session, CancellationToken ct) =>
        await session.FetchXsrfTokenAsync(ct);

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

    // ── anonymous / unauthorized ──────────────────────────────────────────────

    [Fact]
    public async Task ListUsers_Anonymous_Returns401()
    {
        using var client = factory.InitialisedApp.CreateHttpClient("api", "https");

        var response = await client.GetAsync(
            "/api/admin/user", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ListUsers_Bob_Returns403()
    {
        using var session = await LoginHelper.SignInAsync(
            "bob", "dev", TestContext.Current.CancellationToken);

        var response = await session.Client.GetAsync(
            "/api/admin/user", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── list users ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListUsers_Alice_ReturnsAtLeastAliceRow()
    {
        using var session = await LoginHelper.SignInAsync(
            "alice", "dev", TestContext.Current.CancellationToken);

        var response = await session.Client.GetAsync(
            "/api/admin/user", TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ListBody>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.Contains(body.Users, u => u.Email == "alice@example.com");
    }

    // ── grant permission ──────────────────────────────────────────────────────

    [Fact]
    public async Task GrantPermission_HappyPath_Returns201AndPersists()
    {
        var ct = TestContext.Current.CancellationToken;

        // Bob logs in to ensure a user row exists
        using var bobSession = await LoginHelper.SignInAsync("bob", "dev", ct);
        await bobSession.Client.GetAsync("/api/me", ct);

        // Alice grants bob query:execute
        using var aliceSession = await LoginHelper.SignInAsync("alice", "dev", ct);
        var xsrf = await GetXsrfAsync(aliceSession, ct);

        var users = await aliceSession.Client.GetFromJsonAsync<ListBody>(
            "/api/admin/user", ct);
        var bob = Assert.Single(users!.Users, u => u.Email == "bob@example.com");

        using var grantReq = MutationRequest(
            HttpMethod.Post,
            $"/api/admin/user/{bob.Id}/permission",
            xsrf,
            new { permission = Permissions.QueryExecute });
        var grantResp = await aliceSession.Client.SendAsync(grantReq, ct);

        Assert.Equal(HttpStatusCode.Created, grantResp.StatusCode);

        // Verify bob now holds the permission
        using var bobSession2 = await LoginHelper.SignInAsync("bob", "dev", ct);
        var meResp = await bobSession2.Client.GetFromJsonAsync<MeBody>(
            "/api/me", ct);
        Assert.Contains(Permissions.QueryExecute, meResp!.Permissions);
    }

    [Fact]
    public async Task GrantPermission_Idempotent_Returns200OnDuplicate()
    {
        var ct = TestContext.Current.CancellationToken;
        using var aliceSession = await LoginHelper.SignInAsync("alice", "dev", ct);
        var xsrf = await GetXsrfAsync(aliceSession, ct);

        var users = await aliceSession.Client.GetFromJsonAsync<ListBody>(
            "/api/admin/user", ct);
        var alice = Assert.Single(users!.Users, u => u.Email == "alice@example.com");

        // Grant alice server:manage twice
        using var req1 = MutationRequest(
            HttpMethod.Post,
            $"/api/admin/user/{alice.Id}/permission",
            xsrf,
            new { permission = Permissions.ServerManage });
        var resp1 = await aliceSession.Client.SendAsync(req1, ct);
        Assert.True(resp1.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK);

        using var req2 = MutationRequest(
            HttpMethod.Post,
            $"/api/admin/user/{alice.Id}/permission",
            xsrf,
            new { permission = Permissions.ServerManage });
        var resp2 = await aliceSession.Client.SendAsync(req2, ct);
        Assert.Equal(HttpStatusCode.OK, resp2.StatusCode);
    }

    [Fact]
    public async Task GrantPermission_UnknownPermission_Returns400()
    {
        var ct = TestContext.Current.CancellationToken;
        using var aliceSession = await LoginHelper.SignInAsync("alice", "dev", ct);
        var xsrf = await GetXsrfAsync(aliceSession, ct);

        var users = await aliceSession.Client.GetFromJsonAsync<ListBody>(
            "/api/admin/user", ct);
        var alice = Assert.Single(users!.Users, u => u.Email == "alice@example.com");

        using var req = MutationRequest(
            HttpMethod.Post,
            $"/api/admin/user/{alice.Id}/permission",
            xsrf,
            new { permission = "not:real" });
        var response = await aliceSession.Client.SendAsync(req, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── revoke permission ─────────────────────────────────────────────────────

    [Fact]
    public async Task RevokePermission_HappyPath_Returns204AndRemovesGrant()
    {
        var ct = TestContext.Current.CancellationToken;
        using var aliceSession = await LoginHelper.SignInAsync("alice", "dev", ct);
        var xsrf = await GetXsrfAsync(aliceSession, ct);

        var users = await aliceSession.Client.GetFromJsonAsync<ListBody>(
            "/api/admin/user", ct);
        var alice = Assert.Single(users!.Users, u => u.Email == "alice@example.com");

        // First grant update:submit
        using var grantReq = MutationRequest(
            HttpMethod.Post,
            $"/api/admin/user/{alice.Id}/permission",
            xsrf,
            new { permission = Permissions.UpdateSubmit });
        await aliceSession.Client.SendAsync(grantReq, ct);

        // Now revoke it
        using var revokeReq = MutationRequest(
            HttpMethod.Delete,
            $"/api/admin/user/{alice.Id}/permission/{Permissions.UpdateSubmit}",
            xsrf);
        var revokeResp = await aliceSession.Client.SendAsync(revokeReq, ct);
        Assert.Equal(HttpStatusCode.NoContent, revokeResp.StatusCode);

        // Verify removed
        using var aliceSession2 = await LoginHelper.SignInAsync("alice", "dev", ct);
        var me = await aliceSession2.Client.GetFromJsonAsync<MeBody>(
            "/api/me", ct);
        Assert.DoesNotContain(Permissions.UpdateSubmit, me!.Permissions);
    }

    [Fact]
    public async Task RevokePermission_Idempotent_Returns204WhenMissing()
    {
        var ct = TestContext.Current.CancellationToken;
        using var aliceSession = await LoginHelper.SignInAsync("alice", "dev", ct);
        var xsrf = await GetXsrfAsync(aliceSession, ct);

        var users = await aliceSession.Client.GetFromJsonAsync<ListBody>(
            "/api/admin/user", ct);
        var alice = Assert.Single(users!.Users, u => u.Email == "alice@example.com");

        // Revoke a permission alice never had
        using var req = MutationRequest(
            HttpMethod.Delete,
            $"/api/admin/user/{alice.Id}/permission/{Permissions.UpdateApprove}",
            xsrf);
        var response = await aliceSession.Client.SendAsync(req, ct);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // ── bootstrap recovery ────────────────────────────────────────────────────

    [Fact]
    public async Task SelfRevoke_AliceRevokesOwnPermissionManage_BootstrapRestoresOnNextLogin()
    {
        var ct = TestContext.Current.CancellationToken;
        using var aliceSession = await LoginHelper.SignInAsync("alice", "dev", ct);
        var xsrf = await GetXsrfAsync(aliceSession, ct);

        var users = await aliceSession.Client.GetFromJsonAsync<ListBody>(
            "/api/admin/user", ct);
        var alice = Assert.Single(users!.Users, u => u.Email == "alice@example.com");

        // Revoke alice's own permission:manage
        using var revokeReq = MutationRequest(
            HttpMethod.Delete,
            $"/api/admin/user/{alice.Id}/permission/{Permissions.PermissionManage}",
            xsrf);
        var revokeResp = await aliceSession.Client.SendAsync(revokeReq, ct);
        Assert.Equal(HttpStatusCode.NoContent, revokeResp.StatusCode);

        // Next request on the same session should see no permission:manage
        var meAfterRevoke = await aliceSession.Client.GetFromJsonAsync<MeBody>(
            "/api/me", ct);
        Assert.DoesNotContain(Permissions.PermissionManage, meAfterRevoke!.Permissions);

        // New login: bootstrap re-grants permission:manage
        using var aliceSession2 = await LoginHelper.SignInAsync("alice", "dev", ct);
        var meAfterLogin = await aliceSession2.Client.GetFromJsonAsync<MeBody>(
            "/api/me", ct);
        Assert.Contains(Permissions.PermissionManage, meAfterLogin!.Permissions);
    }

    // ── response record types ─────────────────────────────────────────────────

    private sealed record MeBody(
        string Id, string Sub, string Email, string? Name, string[] Permissions);
    private sealed record UserRow(string Id, string Email, string[] Permissions);
    private sealed record ListBody(UserRow[] Users);
}