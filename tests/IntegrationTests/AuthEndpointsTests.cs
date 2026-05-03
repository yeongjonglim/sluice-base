using System.Net;
using Aspire.Hosting.Testing;
using IntegrationTests.Supports;

namespace IntegrationTests;

[Collection("Aspire")]
public class AuthEndpointsTests(SluiceBaseStackFactory factory)
{
    [Fact]
    public async Task Me_Anonymous_Returns401()
    {
        using var client = factory.InitialisedApp.CreateHttpClient("api", "https");

        var response = await client.GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_Redirects_ToKeycloak()
    {
        using var client = factory.InitialisedApp.CreateHttpClient("api", "https");

        var response = await client.GetAsync("/login");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.StartsWith(
            factory.InitialisedApp.GetEndpoint("keycloak", "https") + "realms/sluicebase/protocol/openid-connect/auth",
            response.Headers.Location!.ToString());
    }
}