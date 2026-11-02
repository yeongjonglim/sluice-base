using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;
using Aspire.Hosting.Testing;
using IntegrationTests.Supports;

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

        Assert.False(body.TryGetProperty("registration_endpoint", out _));

        Assert.True(body.TryGetProperty("code_challenge_methods_supported", out var pkce));
        var methods = pkce.EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.Equal(["S256"], methods);

        Assert.True(body.GetProperty("authorization_response_iss_parameter_supported").GetBoolean());
        Assert.True(body.GetProperty("client_id_metadata_document_supported").GetBoolean());
    }

    [Fact]
    public async Task Issuer_IsConfiguredPublicBaseUrl_InBothMetadataDocuments()
    {
        var ct = TestContext.Current.CancellationToken;
        // Hit the API directly: the issuer must still be the AppHost's Mcp__PublicBaseUrl, not the API's own host.
        using var client = factory.InitialisedApp.CreateHttpClient("api", "https");

        var resource = await client.GetFromJsonAsync<JsonElement>("/.well-known/oauth-protected-resource", ct);
        var server = await client.GetFromJsonAsync<JsonElement>("/.well-known/oauth-authorization-server", ct);

        Assert.Equal("https://localhost:5443", server.GetProperty("issuer").GetString());
        Assert.Equal("https://localhost:5443", resource.GetProperty("resource").GetString());
        Assert.Equal("https://localhost:5443", resource.GetProperty("authorization_servers").EnumerateArray().First().GetString());
    }

    [Fact]
    public async Task Issuer_IsIdenticalAcrossMetadataAndIss()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = factory.InitialisedApp.CreateHttpClient("api", "https");

        var resource = await client.GetFromJsonAsync<JsonElement>("/.well-known/oauth-protected-resource", ct);
        var server = await client.GetFromJsonAsync<JsonElement>("/.well-known/oauth-authorization-server", ct);

        var issuer = server.GetProperty("issuer").GetString()!;
        var firstAuthServer = resource.GetProperty("authorization_servers").EnumerateArray().First().GetString()!;
        Assert.Equal(issuer, firstAuthServer);
        Assert.False(issuer.EndsWith('/'));
        Assert.False(firstAuthServer.EndsWith('/'));

        using var session = await new KeycloakLoginHelper(factory.InitialisedApp).SignInAsync("alice", "dev", ct);
        var challenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk")))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var authorize = await session.Client.GetAsync(
            $"/mcp/oauth/authorize?client_id={Uri.EscapeDataString(CimdStubServer.LoopbackClientId)}" +
            $"&redirect_uri={Uri.EscapeDataString("http://127.0.0.1:43123/callback")}&response_type=code" +
            $"&code_challenge={challenge}&code_challenge_method=S256&state=iss", ct);
        Assert.Equal(HttpStatusCode.Redirect, authorize.StatusCode);
        var iss = HttpUtility.ParseQueryString(authorize.Headers.Location!.Query)["iss"];
        Assert.Equal(issuer, iss);
    }

    [Fact]
    public async Task Register_Returns404()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = factory.InitialisedApp.CreateHttpClient("api", "https");

        var response = await client.PostAsJsonAsync("/mcp/oauth/register", new { client_name = "x" }, ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
