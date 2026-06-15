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

        var clientId = await RegisterClientAsync(client, "https://registered.example.com/callback", ct);
        var verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        var challenge = S256Challenge(verifier);

        var url = $"/mcp/oauth/authorize" +
                  $"?client_id={Uri.EscapeDataString(clientId)}" +
                  $"&redirect_uri={Uri.EscapeDataString("https://UNREGISTERED.example.com/callback")}" +
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

        var clientId = await RegisterClientAsync(client, "https://example.com/callback", ct);

        var url = $"/mcp/oauth/authorize" +
                  $"?client_id={Uri.EscapeDataString(clientId)}" +
                  $"&redirect_uri={Uri.EscapeDataString("https://example.com/callback")}" +
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

        var clientId = await RegisterClientAsync(client, "https://example.com/callback", ct);
        var verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        var challenge = S256Challenge(verifier);

        var url = $"/mcp/oauth/authorize" +
                  $"?client_id={Uri.EscapeDataString(clientId)}" +
                  $"&redirect_uri={Uri.EscapeDataString("https://example.com/callback")}" +
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

        var clientId = await RegisterClientAsync(client, "https://example.com/callback", ct);

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = clientId,
            ["code"] = "this-is-not-a-real-code",
            ["redirect_uri"] = "https://example.com/callback",
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

        var clientId = await RegisterClientAsync(client, "https://example.com/callback", ct);

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

        var clientId = await RegisterClientAsync(client, "https://example.com/callback", ct);

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

    // ── Full OAuth flow (CI-gated: requires Keycloak + running Aspire stack) ──
    //
    // The full dance: register → authorize (following OIDC redirect through Keycloak)
    // → capture code from redirect → POST /token with code_verifier → assert tokens
    // → POST /token with refresh_token → assert new tokens.
    //
    // This flow works by:
    //   1. Using a CookieContainer-equipped HttpClient (AllowAutoRedirect=true) so it
    //      follows the OIDC challenge through to Keycloak's login page automatically.
    //   2. Submitting Keycloak credentials via the kc-form-login form (same approach
    //      used by KeycloakLoginHelper.SignInAsync).
    //   3. Following the final redirect back to the registered redirect_uri.
    //      Because our redirect_uri is a synthetic https://localhost/mcp-test/callback,
    //      the final redirect request will fail with a connection error — we catch it,
    //      read Location from the last response (or from the exception's RequestUri)
    //      to extract the `code` and `state` query parameters.
    //
    // If Keycloak is not reachable or the stack is not fully started the test will fail
    // with a meaningful HTTP or socket error that CI will surface.

    [Fact]
    public async Task FullFlow_AuthorizeCodeExchange_ReturnsTokens()
    {
        var ct = TestContext.Current.CancellationToken;

        // Redirect URI must be registered. We use a localhost path we control so we
        // can capture the code from the Location header when the redirect fires.
        var redirectUri = "https://localhost/mcp-test/callback";
        const string verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        var challenge = S256Challenge(verifier);
        const string state = "test-state-abc123";

        // Use a plain client to register the OAuth client (no redirect needed).
        string clientId;
        using (var setupClient = ApiClient())
        {
            clientId = await RegisterClientAsync(setupClient, redirectUri, ct);
        }

        var gatewayBase = factory.InitialisedApp.GetEndpoint("gateway", "https");

        // Build a cookie-container client that follows redirects automatically.
        // AllowAutoRedirect=true so we can follow through the OIDC challenge and
        // Keycloak's own redirects in one shot.
        var cookieJar = new CookieContainer();
        using var loginHandler = new HttpClientHandler
        {
            CookieContainer = cookieJar,
            AllowAutoRedirect = true,
            UseCookies = true,
            ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        };

        // We need to stop BEFORE following the final redirect to our redirect_uri
        // (which is a synthetic URL that doesn't actually exist). We'll do a
        // partial follow: let AllowAutoRedirect handle the OIDC/Keycloak chain but
        // catch the final redirect by switching to AllowAutoRedirect=false at the end.
        //
        // Strategy: make the first call with AllowAutoRedirect=true up through Keycloak
        // submission. The final redirect from Keycloak back to /mcp/oauth/authorize
        // (with the OIDC code) will be followed, and the authorize endpoint will then
        // redirect to our redirect_uri. We switch AllowAutoRedirect=false for that
        // last hop so we can read the Location header.

        using var followClient = new HttpClient(loginHandler) { BaseAddress = gatewayBase };

        var authorizeUrl = $"/mcp/oauth/authorize" +
                           $"?client_id={Uri.EscapeDataString(clientId)}" +
                           $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
                           $"&response_type=code" +
                           $"&code_challenge={Uri.EscapeDataString(challenge)}" +
                           $"&code_challenge_method=S256" +
                           $"&state={Uri.EscapeDataString(state)}";

        // Step 1: GET /mcp/oauth/authorize — will redirect to Keycloak login
        var loginPageResponse = await followClient.GetAsync(authorizeUrl, ct);
        loginPageResponse.EnsureSuccessStatusCode();

        // Step 2: Parse Keycloak login form action and submit credentials
        var html = await loginPageResponse.Content.ReadAsStringAsync(ct);
        var kcFormMatch = System.Text.RegularExpressions.Regex.Match(
            html,
            """<form[^>]+id="kc-form-login"[^>]+action="(?<action>[^"]+)""",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        Assert.True(kcFormMatch.Success, "Could not find Keycloak login form — is the stack healthy?");

        var loginActionUrl = HttpUtility.HtmlDecode(kcFormMatch.Groups["action"].Value);
        var loginForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = "alice",
            ["password"] = "dev",
            ["credentialId"] = string.Empty,
        });
        var afterLogin = await followClient.PostAsync(loginActionUrl, loginForm, ct);

        // Step 3: Keycloak may post a self-submitting form (SAMLResponse-style redirect).
        // Follow any auto-post forms the same way KeycloakLoginHelper does.
        await FollowAutoPostFormsAsync(followClient, afterLogin, ct);

        // At this point the cookie jar should contain the session cookie. Now perform
        // the authorize request again — this time with AllowAutoRedirect=false so we
        // can capture the final redirect to our redirect_uri.
        using var captureHandler = new HttpClientHandler
        {
            CookieContainer = cookieJar,
            AllowAutoRedirect = false,
            UseCookies = true,
            ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        };
        using var captureClient = new HttpClient(captureHandler) { BaseAddress = gatewayBase };

        var authorizeResponse = await captureClient.GetAsync(authorizeUrl, ct);

        // The endpoint should redirect us to our redirect_uri with ?code=...&state=...
        Assert.Equal(HttpStatusCode.Redirect, authorizeResponse.StatusCode);
        var location = authorizeResponse.Headers.Location!;
        Assert.NotNull(location);
        Assert.StartsWith(redirectUri, location.ToString());

        var qs = HttpUtility.ParseQueryString(location.Query);
        var code = qs["code"];
        var returnedState = qs["state"];
        Assert.False(string.IsNullOrEmpty(code), "Expected 'code' in redirect query string");
        Assert.Equal(state, returnedState);

        // Step 4: Exchange code for tokens via POST /mcp/oauth/token (on the api directly).
        using var tokenClient = ApiClient();

        var tokenForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = clientId,
            ["code"] = code!,
            ["redirect_uri"] = redirectUri,
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

        // Step 5: Use the refresh token to get a new token pair.
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

    // ── Helpers for following Keycloak's auto-post forms ──────────────────────

    private static async Task FollowAutoPostFormsAsync(
        HttpClient client, HttpResponseMessage response, CancellationToken ct)
    {
        var html = await response.Content.ReadAsStringAsync(ct);

        if (!html.Contains("document.forms[0].submit()", StringComparison.OrdinalIgnoreCase)
            && !html.Contains("Onload", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var actionMatch = System.Text.RegularExpressions.Regex.Match(
            html, """<form[^>]+action="(?<action>[^"]+)""",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!actionMatch.Success)
        {
            return;
        }

        var actionUrl = HttpUtility.HtmlDecode(actionMatch.Groups["action"].Value);

        var fields = System.Text.RegularExpressions.Regex
            .Matches(html, """<input[^>]+name="(?<name>[^"]+)"[^>]+value="(?<value>[^"]*)" """,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            .ToDictionary(
                m => m.Groups["name"].Value,
                m => HttpUtility.HtmlDecode(m.Groups["value"].Value));

        await client.PostAsync(actionUrl, new FormUrlEncodedContent(fields), ct);
    }
}
