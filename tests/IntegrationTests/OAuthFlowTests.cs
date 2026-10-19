using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;
using Aspire.Hosting.Testing;
using IntegrationTests.Supports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SluiceBase.Api.Data;
using SluiceBase.Api.Mcp;

namespace IntegrationTests;

/// <summary>
/// Integration tests for /mcp/oauth/authorize and /mcp/oauth/token.
///
/// Clients are either the configured <c>sluicebase-dev</c> client from the dev AppHost or CIMD
/// documents served by <see cref="CimdStubServer"/>. The signed-in tests use
/// <see cref="KeycloakLoginHelper"/> and need the full Aspire stack with Keycloak.
/// </summary>
public sealed class OAuthFlowTests(SluiceBaseStackFactory factory)
{
    private const string ConfiguredClientId = "sluicebase-dev";
    private const string LoopbackRedirectUri = "http://127.0.0.1:43123/callback";
    private const string WebRedirectUri = "https://client.example/cb";
    private const string ExpectedIssuer = "https://localhost:5443";
    private const string Verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";

    private KeycloakLoginHelper LoginHelper => new(factory.InitialisedApp);

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>Creates a plain (non-authenticated, non-redirect-following) HTTP client pointed at the api.</summary>
    private HttpClient ApiClient() => factory.InitialisedApp.CreateHttpClient("api", "https");

    private static string AuthorizeUrl(
        string clientId, string redirectUri, string state, string? challenge = null, string responseType = "code")
    {
        var url = $"/mcp/oauth/authorize" +
                  $"?client_id={Uri.EscapeDataString(clientId)}" +
                  $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
                  $"&response_type={responseType}" +
                  $"&code_challenge_method=S256" +
                  $"&state={Uri.EscapeDataString(state)}";
        return challenge is null ? url : url + $"&code_challenge={Uri.EscapeDataString(challenge)}";
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

    private static async Task<string> ErrorAsync(HttpResponseMessage response, CancellationToken ct) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("error").GetString()!;

    private static FormUrlEncodedContent CodeExchange(string clientId, string code, string redirectUri) =>
        new(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = clientId,
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["code_verifier"] = Verifier,
        });

    // ── Shape errors (no login needed) ────────────────────────────────────────

    [Fact]
    public async Task Authorize_WithMissingCodeChallenge_Returns400()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = ApiClient();

        var response = await client.GetAsync(AuthorizeUrl(ConfiguredClientId, LoopbackRedirectUri, "somestate"), ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Authorize_WithWrongResponseType_Returns400()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = ApiClient();

        var response = await client.GetAsync(
            AuthorizeUrl(ConfiguredClientId, LoopbackRedirectUri, "somestate", S256Challenge(Verifier), responseType: "token"), ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Authorize_SignedOut_UnknownClient_RedirectsToLogin()
    {
        var ct = TestContext.Current.CancellationToken;
        // Through the gateway: Keycloak only accepts the gateway's OIDC callback as a redirect_uri.
        using var client = factory.InitialisedApp.CreateHttpClient("gateway", "https");
        factory.CimdStub.Clear();

        var response = await client.GetAsync(
            AuthorizeUrl("https://localhost:47443/signed-out.json", LoopbackRedirectUri, "s", S256Challenge(Verifier)), ct);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/realms/sluicebase/protocol/openid-connect/auth", response.Headers.Location!.ToString());
        Assert.DoesNotContain("/signed-out.json", factory.CimdStub.RequestedPaths);
    }

    // ── Token endpoint negatives (no login needed) ────────────────────────────

    [Fact]
    public async Task Token_WithBogusCode_Returns400WithInvalidGrant()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = ApiClient();

        var response = await client.PostAsync(
            "/mcp/oauth/token", CodeExchange(ConfiguredClientId, "this-is-not-a-real-code", LoopbackRedirectUri), ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_grant", await ErrorAsync(response, ct));
    }

    [Fact]
    public async Task Token_WithBogusRefreshToken_Returns400WithInvalidGrant()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = ApiClient();

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = ConfiguredClientId,
            ["refresh_token"] = "not-a-real-refresh-token",
        });

        var response = await client.PostAsync("/mcp/oauth/token", form, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_grant", await ErrorAsync(response, ct));
    }

    [Fact]
    public async Task Token_WithUnsupportedGrantType_Returns400WithInvalidGrant()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = ApiClient();

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = ConfiguredClientId,
        });

        var response = await client.PostAsync("/mcp/oauth/token", form, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_grant", await ErrorAsync(response, ct));
    }

    [Fact]
    public async Task Token_WithBogusCode_DoesNotFetchUnknownCimdUrl()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = ApiClient();
        factory.CimdStub.Clear();

        var response = await client.PostAsync(
            "/mcp/oauth/token", CodeExchange("https://localhost:47443/never.json", "bogus-code", LoopbackRedirectUri), ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_grant", await ErrorAsync(response, ct));
        Assert.DoesNotContain("/never.json", factory.CimdStub.RequestedPaths);
    }

    [Fact]
    public async Task Token_WithUnknownClient_Returns401InvalidClient()
    {
        var ct = TestContext.Current.CancellationToken;
        // The token endpoint only resolves the client once a live grant exists, so seed a code
        // bound to a client id that neither config nor CIMD can resolve.
        using var session = await LoginHelper.SignInAsync("alice", "dev", ct);

        var metadata = await factory.InitialisedApp.GetConnectionStringAsync("metadata-db", ct);
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(metadata)
            .UseSnakeCaseNamingConvention()
            .Options);
        var user = await db.Users.AsNoTracking().FirstAsync(ct);
        var service = new McpTokenService(db, TimeProvider.System, Options.Create(new McpOptions()));
        var code = await service.IssueAuthCodeAsync(
            "does-not-exist", user.Id, LoopbackRedirectUri, S256Challenge(Verifier), ct);

        using var client = ApiClient();
        var response = await client.PostAsync(
            "/mcp/oauth/token", CodeExchange("does-not-exist", code, LoopbackRedirectUri), ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("invalid_client", await ErrorAsync(response, ct));
    }

    // ── Signed-in authorize (CI-gated: requires Keycloak + running Aspire stack) ──

    [Fact]
    public async Task Authorize_SignedIn_UnknownClient_Returns400()
    {
        var ct = TestContext.Current.CancellationToken;
        using var session = await LoginHelper.SignInAsync("alice", "dev", ct);

        var response = await session.Client.GetAsync(
            AuthorizeUrl("does-not-exist", LoopbackRedirectUri, "s", S256Challenge(Verifier)), ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task Authorize_WithUnknownClientId_Returns400()
    {
        var ct = TestContext.Current.CancellationToken;
        using var session = await LoginHelper.SignInAsync("alice", "dev", ct);

        var response = await session.Client.GetAsync(
            AuthorizeUrl("https://localhost:47443/missing.json", LoopbackRedirectUri, "s", S256Challenge(Verifier)), ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("invalid_client_or_redirect_uri", await response.Content.ReadAsStringAsync(ct));
    }

    [Theory]
    [InlineData(ConfiguredClientId, "http://127.0.0.1:43123/other")]
    [InlineData(CimdStubServer.LoopbackClientId, "http://127.0.0.1:43123/other")]
    [InlineData(CimdStubServer.WebClientId, "https://attacker.example/cb")]
    public async Task Authorize_WithUnregisteredRedirectUri_Returns400(string clientId, string redirectUri)
    {
        var ct = TestContext.Current.CancellationToken;
        using var session = await LoginHelper.SignInAsync("alice", "dev", ct);

        var response = await session.Client.GetAsync(AuthorizeUrl(clientId, redirectUri, "s", S256Challenge(Verifier)), ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task Authorize_CimdMismatch_Returns400()
    {
        var ct = TestContext.Current.CancellationToken;
        using var session = await LoginHelper.SignInAsync("alice", "dev", ct);

        var response = await session.Client.GetAsync(
            AuthorizeUrl(CimdStubServer.MismatchClientId, LoopbackRedirectUri, "s", S256Challenge(Verifier)), ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task Authorize_CimdLoopback_IssuesCodeWithoutConsent_WithIss()
    {
        var ct = TestContext.Current.CancellationToken;
        using var session = await LoginHelper.SignInAsync("alice", "dev", ct);

        var response = await session.Client.GetAsync(
            AuthorizeUrl(CimdStubServer.LoopbackClientId, LoopbackRedirectUri, "loop-state", S256Challenge(Verifier)), ct);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!;
        Assert.StartsWith(LoopbackRedirectUri + "?", location.ToString());
        var qs = HttpUtility.ParseQueryString(location.Query);
        Assert.False(string.IsNullOrEmpty(qs["code"]));
        Assert.Equal("loop-state", qs["state"]);
        Assert.Equal(ExpectedIssuer, qs["iss"]);
    }

    [Fact]
    public async Task Authorize_ConfiguredClient_IssuesCodeWithoutConsent()
    {
        var ct = TestContext.Current.CancellationToken;
        using var session = await LoginHelper.SignInAsync("alice", "dev", ct);

        var response = await session.Client.GetAsync(
            AuthorizeUrl(ConfiguredClientId, LoopbackRedirectUri, "cfg-state", S256Challenge(Verifier)), ct);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var qs = HttpUtility.ParseQueryString(response.Headers.Location!.Query);
        Assert.False(string.IsNullOrEmpty(qs["code"]));
        Assert.Equal("cfg-state", qs["state"]);
        Assert.Equal(ExpectedIssuer, qs["iss"]);
    }

    [Fact]
    public async Task Authorize_CimdWeb_ShowsConsentWithVerifiedHost()
    {
        var ct = TestContext.Current.CancellationToken;
        using var session = await LoginHelper.SignInAsync("alice", "dev", ct);

        var response = await session.Client.GetAsync(
            AuthorizeUrl(CimdStubServer.WebClientId, WebRedirectUri, "s", S256Challenge(Verifier)), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Contains("frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
        var html = await response.Content.ReadAsStringAsync(ct);
        Assert.Contains("<strong>localhost</strong>", html);
        Assert.Contains("Example Web Tool", html);
    }

    [Fact]
    public async Task Authorize_PostWithoutAntiforgeryToken_Returns400()
    {
        var ct = TestContext.Current.CancellationToken;
        using var session = await LoginHelper.SignInAsync("alice", "dev", ct);
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = CimdStubServer.WebClientId,
            ["redirect_uri"] = WebRedirectUri,
            ["response_type"] = "code",
            ["code_challenge"] = S256Challenge(Verifier),
            ["code_challenge_method"] = "S256",
            ["state"] = "s",
            ["decision"] = "allow",
        });

        var response = await session.Client.PostAsync("/mcp/oauth/authorize", form, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Authorize_PostWithoutAntiforgeryToken_DoesNotFetchCimd()
    {
        var ct = TestContext.Current.CancellationToken;
        using var session = await LoginHelper.SignInAsync("alice", "dev", ct);
        factory.CimdStub.Clear();
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = "https://localhost:47443/forged-post.json",
            ["redirect_uri"] = WebRedirectUri,
            ["response_type"] = "code",
            ["code_challenge"] = S256Challenge(Verifier),
            ["code_challenge_method"] = "S256",
            ["state"] = "s",
            ["decision"] = "allow",
        });

        var response = await session.Client.PostAsync("/mcp/oauth/authorize", form, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("invalid_csrf_token", await response.Content.ReadAsStringAsync(ct));
        Assert.DoesNotContain("/forged-post.json", factory.CimdStub.RequestedPaths);
    }

    [Fact]
    public async Task Authorize_CimdWeb_Allow_RedirectsWithCodeAndIss()
    {
        var ct = TestContext.Current.CancellationToken;
        using var session = await LoginHelper.SignInAsync("alice", "dev", ct);
        var consent = await session.Client.GetAsync(
            AuthorizeUrl(CimdStubServer.WebClientId, WebRedirectUri, "allow-state", S256Challenge(Verifier)), ct);
        var fields = McpConsentForm.Fields(await consent.Content.ReadAsStringAsync(ct), "allow");

        var response = await session.Client.PostAsync("/mcp/oauth/authorize", new FormUrlEncodedContent(fields), ct);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith(WebRedirectUri + "?", response.Headers.Location!.ToString());
        var qs = HttpUtility.ParseQueryString(response.Headers.Location!.Query);
        Assert.False(string.IsNullOrEmpty(qs["code"]));
        Assert.Equal("allow-state", qs["state"]);
        Assert.Equal(ExpectedIssuer, qs["iss"]);
    }

    [Fact]
    public async Task Authorize_Deny_RedirectsWithAccessDeniedAndIss()
    {
        var ct = TestContext.Current.CancellationToken;
        using var session = await LoginHelper.SignInAsync("alice", "dev", ct);
        var consent = await session.Client.GetAsync(
            AuthorizeUrl(CimdStubServer.WebClientId, WebRedirectUri, "deny-state", S256Challenge(Verifier)), ct);
        var fields = McpConsentForm.Fields(await consent.Content.ReadAsStringAsync(ct), "deny");

        var response = await session.Client.PostAsync("/mcp/oauth/authorize", new FormUrlEncodedContent(fields), ct);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var qs = HttpUtility.ParseQueryString(response.Headers.Location!.Query);
        Assert.Equal("access_denied", qs["error"]);
        Assert.Equal("deny-state", qs["state"]);
        Assert.Equal(ExpectedIssuer, qs["iss"]);
        Assert.Null(qs["code"]);
    }

    [Fact]
    public async Task FullFlow_CimdLoopback_CodeExchangeAndRefresh_ReturnsTokens()
    {
        var ct = TestContext.Current.CancellationToken;

        // The CIMD document registers a portless loopback URI; native clients bind an ephemeral
        // port per sign-in (RFC 8252 §7.3).
        const string requestedRedirectUri = "http://127.0.0.1:51234/callback";
        const string state = "test-state-abc123";

        using var session = await LoginHelper.SignInAsync("alice", "dev", ct);

        // Step 1: authorize → redirect straight to redirect_uri?code=...&state=...&iss=...
        var authorizeResponse = await session.Client.GetAsync(
            AuthorizeUrl(CimdStubServer.LoopbackClientId, requestedRedirectUri, state, S256Challenge(Verifier)), ct);
        Assert.Equal(HttpStatusCode.Redirect, authorizeResponse.StatusCode);
        var location = authorizeResponse.Headers.Location!;
        Assert.StartsWith(requestedRedirectUri, location.ToString());

        var qs = HttpUtility.ParseQueryString(location.Query);
        var code = qs["code"];
        Assert.False(string.IsNullOrEmpty(code), "Expected 'code' in redirect query string");
        Assert.Equal(state, qs["state"]);

        // Step 2: Exchange code for tokens via POST /mcp/oauth/token (on the api directly).
        using var tokenClient = ApiClient();
        var tokenResponse = await tokenClient.PostAsync(
            "/mcp/oauth/token", CodeExchange(CimdStubServer.LoopbackClientId, code!, requestedRedirectUri), ct);
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

        // Step 3: Use the refresh token to get a new token pair.
        var refreshForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = CimdStubServer.LoopbackClientId,
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
