using System.Net;
using System.Net.Http.Json;
using Aspire.Hosting.Testing;
using IntegrationTests.Supports;
using SluiceBase.Core.Permissions;

namespace IntegrationTests;

public class MeEndpointTests(SluiceBaseStackFactory factory)
{
    private KeycloakLoginHelper LoginHelper => new(factory.InitialisedApp);

    [Fact]
    public async Task Me_Anonymous_Returns401()
    {
        using var client = factory.InitialisedApp.CreateHttpClient("api", "https");

        var response = await client.GetAsync("/api/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_AliceBootstrapAdmin_ReturnsPermissionManage()
    {
        var helper = new KeycloakLoginHelper(factory.InitialisedApp);
        using var session = await helper.SignInAsync(
            "alice",
            "dev",
            TestContext.Current.CancellationToken);

        var response = await session.Client.GetAsync(
            "/api/me",
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<MeBody>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.Equal("alice@example.com", body.Email);
        Assert.Contains(Permissions.PermissionManage, body.Permissions);
    }

    [Fact]
    public async Task Me_Bob_ReturnsEmptyPermissions()
    {
        var ct = TestContext.Current.CancellationToken;

        using var initialBobSession = await LoginHelper.SignInAsync("bob", "dev", ct);
        await initialBobSession.Client.GetAsync("/api/me", ct);

        using var adminSession = await LoginHelper.SignInAsync("alice", "dev", ct);
        var xsrf = await adminSession.FetchXsrfTokenAsync(ct);
        await PermissionTestHelper.RevokeAllPermissionsAsync(adminSession, "bob@example.com", xsrf, ct);
        await PermissionTestHelper.RevokeAllDatabaseRolesAsync(adminSession, "bob@example.com", xsrf, ct);

        using var session = await LoginHelper.SignInAsync(
            "bob",
            "dev",
            ct);

        var response = await session.Client.GetAsync(
            "/api/me",
            ct);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<MeBody>(
            ct);
        Assert.NotNull(body);
        Assert.Equal("bob@example.com", body.Email);
        Assert.Empty(body.Permissions);
    }

    private sealed record MeBody(
        string Id,
        string Email,
        string? Name,
        string[] Permissions);
}
