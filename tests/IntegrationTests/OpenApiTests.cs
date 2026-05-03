using System.Net;
using System.Text.Json;
using Aspire.Hosting.Testing;
using IntegrationTests.Supports;

namespace IntegrationTests;

[Collection("Aspire")]
public class OpenApiTests(SluiceBaseStackFactory factory)
{
    [Fact]
    public async Task OpenApi_Document_IsServed()
    {
        using var client = factory.InitialisedApp.CreateHttpClient("api", "https");

        var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(body);

        Assert.Equal("3.1.1", doc.RootElement.GetProperty("openapi").GetString());
        Assert.True(doc.RootElement.TryGetProperty("paths", out _));
    }
}