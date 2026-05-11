using System.Net;
using System.Net.Http.Json;
using Aspire.Hosting.Testing;
using IntegrationTests.Supports;
using SluiceBase.Core.Permissions;

namespace IntegrationTests;

public class MeEndpointTests(SluiceBaseStackFactory factory)
{
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
        Assert.False(string.IsNullOrEmpty(body.Sub));
        Assert.Equal("alice@example.com", body.Email);
        Assert.Contains(Permissions.PermissionManage, body.Permissions);
    }

    [Fact]
    public async Task Me_Bob_ReturnsEmptyPermissions()
    {
        var helper = new KeycloakLoginHelper(factory.InitialisedApp);
        using var session = await helper.SignInAsync(
            "bob",
            "dev",
            TestContext.Current.CancellationToken);

        var response = await session.Client.GetAsync(
            "/api/me",
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<MeBody>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.Equal("bob@example.com", body.Email);
        Assert.Empty(body.Permissions);
    }

    private sealed record MeBody(
        string Id,
        string Sub,
        string Email,
        string? Name,
        string[] Permissions);
}