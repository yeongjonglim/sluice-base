using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;
using Aspire.Hosting.Testing;
using IntegrationTests.Supports;

namespace IntegrationTests;

/// <summary>
/// Integration tests for /mcp/oauth/authorize and /mcp/oauth/token.
///
/// The full-flow tests (authorize → Keycloak login → code → token → refresh) are
/// CI-gated: they require the Aspire stack with a running Keycloak container and a
/// registered test user. They will NOT run locally unless the full stack is healthy.
///
/// The negative tests (unregistered redirect_uri, bogus code) do NOT require login and
/// are the primary compile-time + CI gate for these endpoints.
/// </summary>
public sealed class OAuthFlowTests(SluiceBaseStackFactory factory)
{
    private const string LoopbackRedirectUri = "http://127.0.0.1:43123/callback";

    private KeycloakLoginHelper LoginHelper => new(factory.InitialisedApp);

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>Creates a plain (non-authenticated, non-redirect-following) HTTP client pointed at the api.</summary>
    private HttpClient ApiClient() => factory.InitialisedApp.CreateHttpClient("api", "https");

    /// <summary>Registers a new MCP OAuth client and returns the assigned client_id.</summary>
    private static async Task<string> RegisterClientAsync(HttpClient client, string redirectUri, CancellationToken ct)
    {
        var body = new
        {
            client_name = "OAuthFlowTest Client",
            redirect_uris = new[] { redirectUri },
        };
        var response = await client.PostAsJsonAsync("/mcp/oauth/register", body, ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return result.GetProperty("client_id").GetString()!;
    }

    private static Task<HttpResponseMessage> PostRegistrationAsync(HttpClient client, string[] redirectUris, CancellationToken ct) =>
        client.PostAsJsonAsync("/mcp/oauth/register", new { client_name = "OAuthFlowTest Client", redirect_uris = redirectUris }, ct);

    private static string AuthorizeUrl(string clientId, string redirectUri, string challenge, string state) =>
        $"/mcp/oauth/authorize" +
        $"?client_id={Uri.EscapeDataString(clientId)}" +
        $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
        $"&response_type=code" +
        $"&code_challenge={Uri.EscapeDataString(challenge)}" +
        $"&code_challenge_method=S256" +
        $"&state={Uri.EscapeDataString(state)}";

    /// <summary>Computes the PKCE S256 code challenge for a given verifier.</summary>
    private static string S256Challenge(string verifier)
    {
        var bytes = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    // ── Negative tests (no login needed) ──────────────────────────────────────

    [Fact]
    public async Task Authorize_WithUnregisteredRedirectUri_Returns400()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = ApiClient();

        var clientId = await RegisterClientAsync(client, LoopbackRedirectUri, ct);
        var verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        var challenge = S256Challenge(verifier);

        var url = $"/mcp/oauth/authorize" +
                  $"?client_id={Uri.EscapeDataString(clientId)}" +
                  $"&redirect_uri={Uri.EscapeDataString("http://127.0.0.1:43123/other")}" +
                  $"&response_type=code" +
                  $"&code_challenge={Uri.EscapeDataString(challenge)}" +
                  $"&code_challenge_method=S256" +
                  $"&state=somestate";

        var response = await client.GetAsync(url, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Authorize_WithUnknownClientId_Returns400()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = ApiClient();

        var verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        var challenge = S256Challenge(verifier);

        var url = $"/mcp/oauth/authorize" +
                  $"?client_id=does-not-exist" +
                  $"&redirect_uri={Uri.EscapeDataString("https://example.com/callback")}" +
                  $"&response_type=code" +
                  $"&code_challenge={Uri.EscapeDataString(challenge)}" +
                  $"&code_challenge_method=S256" +
                  $"&state=somestate";

        var response = await client.GetAsync(url, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Authorize_WithMissingCodeChallenge_Returns400()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = ApiClient();

        var clientId = await RegisterClientAsync(client, LoopbackRedirectUri, ct);

        var url = $"/mcp/oauth/authorize" +
                  $"?client_id={Uri.EscapeDataString(clientId)}" +
                  $"&redirect_uri={Uri.EscapeDataString(LoopbackRedirectUri)}" +
                  $"&response_type=code" +
                  $"&code_challenge_method=S256" +
                  $"&state=somestate";
        // Deliberately omitted: code_challenge

        var response = await client.GetAsync(url, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Authorize_WithWrongResponseType_Returns400()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = ApiClient();

        var clientId = await RegisterClientAsync(client, LoopbackRedirectUri, ct);
        var verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        var challenge = S256Challenge(verifier);

        var url = $"/mcp/oauth/authorize" +
                  $"?client_id={Uri.EscapeDataString(clientId)}" +
                  $"&redirect_uri={Uri.EscapeDataString(LoopbackRedirectUri)}" +
                  $"&response_type=token" + // invalid — must be "code"
                  $"&code_challenge={Uri.EscapeDataString(challenge)}" +
                  $"&code_challenge_method=S256" +
                  $"&state=somestate";

        var response = await client.GetAsync(url, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Token_WithBogusCode_Returns400WithInvalidGrant()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = ApiClient();

        var clientId = await RegisterClientAsync(client, LoopbackRedirectUri, ct);

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = clientId,
            ["code"] = "this-is-not-a-real-code",
            ["redirect_uri"] = LoopbackRedirectUri,
            ["code_verifier"] = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk",
        });

        var response = await client.PostAsync("/mcp/oauth/token", form, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.True(body.TryGetProperty("error", out var error));
        Assert.Equal("invalid_grant", error.GetString());
    }

    [Fact]
    public async Task Token_WithBogusRefreshToken_Returns400WithInvalidGrant()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = ApiClient();

        var clientId = await RegisterClientAsync(client, LoopbackRedirectUri, ct);

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = clientId,
            ["refresh_token"] = "not-a-real-refresh-token",
        });

        var response = await client.PostAsync("/mcp/oauth/token", form, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.True(body.TryGetProperty("error", out var error));
        Assert.Equal("invalid_grant", error.GetString());
    }

    [Fact]
    public async Task Token_WithUnsupportedGrantType_Returns400WithInvalidGrant()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = ApiClient();

        var clientId = await RegisterClientAsync(client, LoopbackRedirectUri, ct);

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
        });

        var response = await client.PostAsync("/mcp/oauth/token", form, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.True(body.TryGetProperty("error", out var error));
        Assert.Equal("invalid_grant", error.GetString());
    }

    [Theory]
    [InlineData("https://attacker.example/cb")]
    [InlineData("javascript:alert(1)")]
    [InlineData("myapp://callback")]
    [InlineData("https://vscode.dev/redirect")]
    public async Task Register_WithRedirectUriThatCanReachAThirdParty_Returns400(string redirectUri)
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = ApiClient();

        var response = await PostRegistrationAsync(client, [redirectUri], ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("invalid_redirect_uri", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Register_WithMixedRedirectUris_KeepsOnlyAllowedOnes()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = ApiClient();

        // What VS Code sends: relay URIs alongside its loopback callbacks.
        var response = await PostRegistrationAsync(client,
            ["https://vscode.dev/redirect", "https://insiders.vscode.dev/redirect", "http://127.0.0.1/", "http://127.0.0.1:33418/"], ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var registered = body.GetProperty("redirect_uris").EnumerateArray().Select(u => u.GetString()).ToArray();
        Assert.Equal(["http://127.0.0.1/", "http://127.0.0.1:33418/"], registered);
    }

    [Fact]
    public async Task Token_WithUnknownClient_Returns401InvalidClient()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = ApiClient();

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = "does-not-exist",
            ["refresh_token"] = "not-a-real-refresh-token",
        });

        var response = await client.PostAsync("/mcp/oauth/token", form, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("invalid_client", body.GetProperty("error").GetString());
    }

    // ── Full OAuth flow (CI-gated: requires Keycloak + running Aspire stack) ──
    //
    // The full dance: register → authorize (consent page) → approve → capture code from redirect →
    // POST /token with code_verifier → assert tokens → POST /token with refresh_token → assert new tokens.
    //
    // Authentication reuses KeycloakLoginHelper.SignInAsync (the proven login path used by
    // every other authenticated test), which returns a session whose Client carries the
    // sb.auth cookie and uses AllowAutoRedirect=false. Because the session is already
    // authenticated, GET /mcp/oauth/authorize renders the consent page straight away; its
    // antiforgery cookie lands in the same cookie container for the approval POST.

    [Fact]
    public async Task Authorize_WhenSignedIn_ShowsConsentWithoutIssuingCode()
    {
        var ct = TestContext.Current.CancellationToken;
        string clientId;
        using (var setupClient = ApiClient())
        {
            clientId = await RegisterClientAsync(setupClient, LoopbackRedirectUri, ct);
        }

        using var session = await LoginHelper.SignInAsync("alice", "dev", ct);
        var response = await session.Client.GetAsync(
            AuthorizeUrl(clientId, LoopbackRedirectUri, S256Challenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"), "s"), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Contains("frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
        var html = await response.Content.ReadAsStringAsync(ct);
        Assert.Contains("127.0.0.1:43123", html);
    }

    [Fact]
    public async Task Authorize_PostWithoutAntiforgeryToken_Returns400()
    {
        var ct = TestContext.Current.CancellationToken;
        string clientId;
        using (var setupClient = ApiClient())
        {
            clientId = await RegisterClientAsync(setupClient, LoopbackRedirectUri, ct);
        }

        using var session = await LoginHelper.SignInAsync("alice", "dev", ct);
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["redirect_uri"] = LoopbackRedirectUri,
            ["response_type"] = "code",
            ["code_challenge"] = S256Challenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"),
            ["code_challenge_method"] = "S256",
            ["state"] = "s",
            ["decision"] = "allow",
        });

        var response = await session.Client.PostAsync("/mcp/oauth/authorize", form, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Authorize_Deny_RedirectsWithAccessDenied()
    {
        var ct = TestContext.Current.CancellationToken;
        string clientId;
        using (var setupClient = ApiClient())
        {
            clientId = await RegisterClientAsync(setupClient, LoopbackRedirectUri, ct);
        }

        using var session = await LoginHelper.SignInAsync("alice", "dev", ct);
        var consent = await session.Client.GetAsync(
            AuthorizeUrl(clientId, LoopbackRedirectUri, S256Challenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"), "deny-state"), ct);
        var fields = McpConsentForm.Fields(await consent.Content.ReadAsStringAsync(ct), "deny");

        var response = await session.Client.PostAsync("/mcp/oauth/authorize", new FormUrlEncodedContent(fields), ct);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var qs = HttpUtility.ParseQueryString(response.Headers.Location!.Query);
        Assert.Equal("access_denied", qs["error"]);
        Assert.Equal("deny-state", qs["state"]);
        Assert.Null(qs["code"]);
    }

    [Fact]
    public async Task FullFlow_ConsentApproveCodeExchange_ReturnsTokens()
    {
        var ct = TestContext.Current.CancellationToken;

        // Native clients bind an ephemeral port per sign-in, so the authorize request uses a
        // different loopback port from the one registered (RFC 8252 §7.3).
        var requestedRedirectUri = "http://127.0.0.1:51234/callback";
        const string verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        var challenge = S256Challenge(verifier);
        const string state = "test-state-abc123";

        string clientId;
        using (var setupClient = ApiClient())
        {
            clientId = await RegisterClientAsync(setupClient, LoopbackRedirectUri, ct);
        }

        using var session = await LoginHelper.SignInAsync("alice", "dev", ct);

        // Step 1: the consent page.
        var consentResponse = await session.Client.GetAsync(AuthorizeUrl(clientId, requestedRedirectUri, challenge, state), ct);
        Assert.Equal(HttpStatusCode.OK, consentResponse.StatusCode);
        var fields = McpConsentForm.Fields(await consentResponse.Content.ReadAsStringAsync(ct), "allow");

        // Step 2: approve → redirect to redirect_uri?code=...&state=...
        var authorizeResponse = await session.Client.PostAsync("/mcp/oauth/authorize", new FormUrlEncodedContent(fields), ct);
        Assert.Equal(HttpStatusCode.Redirect, authorizeResponse.StatusCode);
        var location = authorizeResponse.Headers.Location!;
        Assert.NotNull(location);
        Assert.StartsWith(requestedRedirectUri, location.ToString());

        var qs = HttpUtility.ParseQueryString(location.Query);
        var code = qs["code"];
        var returnedState = qs["state"];
        Assert.False(string.IsNullOrEmpty(code), "Expected 'code' in redirect query string");
        Assert.Equal(state, returnedState);

        // Step 3: Exchange code for tokens via POST /mcp/oauth/token (on the api directly).
        using var tokenClient = ApiClient();

        var tokenForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = clientId,
            ["code"] = code!,
            ["redirect_uri"] = requestedRedirectUri,
            ["code_verifier"] = verifier,
        });
        var tokenResponse = await tokenClient.PostAsync("/mcp/oauth/token", tokenForm, ct);
        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);

        var tokenBody = await tokenResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.True(tokenBody.TryGetProperty("access_token", out var accessToken));
        Assert.False(string.IsNullOrWhiteSpace(accessToken.GetString()));
        Assert.True(tokenBody.TryGetProperty("refresh_token", out var refreshToken));
        Assert.False(string.IsNullOrWhiteSpace(refreshToken.GetString()));
        Assert.True(tokenBody.TryGetProperty("token_type", out var tokenType));
        Assert.Equal("Bearer", tokenType.GetString());
        Assert.True(tokenBody.TryGetProperty("expires_in", out var expiresIn));
        Assert.True(expiresIn.GetInt32() > 0);

        // Step 4: Use the refresh token to get a new token pair.
        var refreshForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = clientId,
            ["refresh_token"] = refreshToken.GetString()!,
        });
        var refreshResponse = await tokenClient.PostAsync("/mcp/oauth/token", refreshForm, ct);
        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);

        var refreshBody = await refreshResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.True(refreshBody.TryGetProperty("access_token", out var newAccessToken));
        Assert.False(string.IsNullOrWhiteSpace(newAccessToken.GetString()));
        Assert.True(refreshBody.TryGetProperty("refresh_token", out var newRefreshToken));
        Assert.False(string.IsNullOrWhiteSpace(newRefreshToken.GetString()));

        // New tokens must differ from the initial ones (rotation).
        Assert.NotEqual(accessToken.GetString(), newAccessToken.GetString());
        Assert.NotEqual(refreshToken.GetString(), newRefreshToken.GetString());
    }
}
