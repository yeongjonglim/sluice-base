using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aspire.Hosting.Testing;
using IntegrationTests.Supports;

namespace IntegrationTests;

public class AuthEndpointTests(SluiceBaseStackFactory factory)
{
    [Fact]
    public async Task Me_Anonymous_Returns401()
    {
        using var client = factory.InitialisedApp.CreateHttpClient("api", "https");

        var response = await client.GetAsync("/api/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_Redirects_ToKeycloak()
    {
        using var client = factory.InitialisedApp.CreateHttpClient("gateway", "https");

        var response = await client.GetAsync("/login", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.StartsWith(
            factory.InitialisedApp.GetEndpoint("keycloak", "http") + "realms/sluicebase/protocol/openid-connect/auth",
            response.Headers.Location!.ToString());
    }

    private const string RevokeUnknownPermissionPath =
        "/api/admin/user/00000000-0000-0000-0000-000000000000/permission/server:manage";

    [Fact]
    public async Task ApiMutation_WithoutAntiforgeryToken_Returns400()
    {
        var ct = TestContext.Current.CancellationToken;
        using var session = await new KeycloakLoginHelper(factory.InitialisedApp).SignInAsync("alice", "dev", ct);

        // The session cookie alone is what a cross-site form or fetch would carry.
        var response = await session.Client.DeleteAsync(RevokeUnknownPermissionPath, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("antiforgery", body.GetProperty("type").GetString());
    }

    [Fact]
    public async Task ApiMutation_WithAntiforgeryToken_IsNotRejectedByTheCheck()
    {
        var ct = TestContext.Current.CancellationToken;
        using var session = await new KeycloakLoginHelper(factory.InitialisedApp).SignInAsync("alice", "dev", ct);
        var xsrf = await session.FetchXsrfTokenAsync(ct);

        using var request = new HttpRequestMessage(HttpMethod.Delete, RevokeUnknownPermissionPath);
        request.Headers.Add("X-XSRF-TOKEN", xsrf);
        var response = await session.Client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}
