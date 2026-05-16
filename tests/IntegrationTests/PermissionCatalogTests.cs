using System.Net;
using System.Net.Http.Json;
using Aspire.Hosting.Testing;
using IntegrationTests.Supports;
using SluiceBase.Core.Permissions;

namespace IntegrationTests;

public class PermissionCatalogTests(SluiceBaseStackFactory factory)
{
    [Fact]
    public async Task PermissionCatalog_Anonymous_Returns401()
    {
        using var client = factory.InitialisedApp.CreateHttpClient("api", "https");

        var response = await client.GetAsync(
            "/api/permission/catalog", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PermissionCatalog_Authenticated_ReturnsGlobalPermissions()
    {
        var helper = new KeycloakLoginHelper(factory.InitialisedApp);
        using var session = await helper.SignInAsync(
            "alice", "dev", TestContext.Current.CancellationToken);

        var response = await session.Client.GetAsync(
            "/api/permission/catalog", TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<CatalogBody>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.Equal(2, body.Permissions.Length);
        Assert.Contains(Permissions.PermissionManage, body.Permissions);
        Assert.Contains(Permissions.ServerManage, body.Permissions);
    }

    private sealed record CatalogBody(string[] Permissions);
}