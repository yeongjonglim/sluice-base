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

        var response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}