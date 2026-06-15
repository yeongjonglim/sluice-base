using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aspire.Hosting.Testing;
using IntegrationTests.Supports;
using Npgsql;

namespace IntegrationTests;

public sealed class OAuthMetadataTests(SluiceBaseStackFactory factory)
{
    // ── Metadata discovery ────────────────────────────────────────────────────

    [Fact]
    public async Task OAuthProtectedResource_ReturnsOk_WithResourceAndAuthorizationServers()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = factory.InitialisedApp.CreateHttpClient("api", "https");

        var response = await client.GetAsync("/.well-known/oauth-protected-resource", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.True(body.TryGetProperty("resource", out var resource));
        Assert.False(string.IsNullOrWhiteSpace(resource.GetString()));

        Assert.True(body.TryGetProperty("authorization_servers", out var authServers));
        Assert.True(authServers.GetArrayLength() > 0);

        var baseUrl = resource.GetString()!;
        var firstServer = authServers.EnumerateArray().First().GetString();
        Assert.Equal(baseUrl, firstServer);
    }

    [Fact]
    public async Task OAuthAuthorizationServer_ReturnsOk_WithRequiredFields()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = factory.InitialisedApp.CreateHttpClient("api", "https");

        var response = await client.GetAsync("/.well-known/oauth-authorization-server", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);

        Assert.True(body.TryGetProperty("authorization_endpoint", out var authEndpoint));
        Assert.False(string.IsNullOrWhiteSpace(authEndpoint.GetString()));

        Assert.True(body.TryGetProperty("token_endpoint", out var tokenEndpoint));
        Assert.False(string.IsNullOrWhiteSpace(tokenEndpoint.GetString()));

        Assert.True(body.TryGetProperty("registration_endpoint", out var registrationEndpoint));
        Assert.False(string.IsNullOrWhiteSpace(registrationEndpoint.GetString()));

        Assert.True(body.TryGetProperty("code_challenge_methods_supported", out var pkce));
        var methods = pkce.EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.Equal(["S256"], methods);
    }

    // ── Dynamic Client Registration ───────────────────────────────────────────

    [Fact]
    public async Task Register_WithValidRedirectUri_Returns201AndClientId()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = factory.InitialisedApp.CreateHttpClient("api", "https");

        var body = new
        {
            client_name = "Test MCP Client",
            redirect_uris = new[] { "https://example.com/callback" },
        };

        var response = await client.PostAsJsonAsync("/mcp/oauth/register", body, ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>(ct);

        Assert.True(result.TryGetProperty("client_id", out var clientId));
        Assert.False(string.IsNullOrWhiteSpace(clientId.GetString()));

        Assert.True(result.TryGetProperty("client_name", out var clientName));
        Assert.Equal("Test MCP Client", clientName.GetString());

        Assert.True(result.TryGetProperty("redirect_uris", out var redirectUris));
        var uris = redirectUris.EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains("https://example.com/callback", uris);

        // Verify the row exists in the metadata DB
        var metaConnStr = await factory.InitialisedApp.GetConnectionStringAsync("metadata-db", ct);
        await using var conn = new NpgsqlConnection(metaConnStr);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM mcp_oauth_client WHERE client_id = @clientId", conn);
        cmd.Parameters.AddWithValue("@clientId", clientId.GetString()!);
        var count = (long)(await cmd.ExecuteScalarAsync(ct))!;
        Assert.Equal(1L, count);
    }

    [Fact]
    public async Task Register_WithEmptyRedirectUris_Returns400()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = factory.InitialisedApp.CreateHttpClient("api", "https");

        var body = new
        {
            client_name = "Bad Client",
            redirect_uris = Array.Empty<string>(),
        };

        var response = await client.PostAsJsonAsync("/mcp/oauth/register", body, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.True(result.TryGetProperty("error", out var error));
        Assert.Equal("invalid_redirect_uri", error.GetString());
    }
}
