using System.Net;
using Aspire.Hosting.Testing;
using IntegrationTests.Supports;

namespace IntegrationTests;

[Collection("Aspire")]
public sealed class HealthEndpointsTests(SluiceBaseStackFactory factory)
{
    [Fact]
    public async Task Health_Anonymous_ReturnsOk()
    {
        var client = factory.InitialisedApp.CreateHttpClient("api", "https");

        var response = await client.GetAsync("/api/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Health_Authed_Anonymous_Returns401()
    {
        using var client = factory.InitialisedApp.CreateHttpClient("api", "https");

        var response = await client.GetAsync("/api/health/authed", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}