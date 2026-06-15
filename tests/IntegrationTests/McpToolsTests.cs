using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using IntegrationTests.Supports;
using ModelContextProtocol.Client;
using Npgsql;
using SluiceBase.Api.Endpoints;
using SluiceBase.Core.Permissions;

namespace IntegrationTests;

/// <summary>
/// Integration tests for the MCP server endpoint mounted at /mcp.
///
/// Test 1 (BearerGate) verifies the McpBearer authorization policy: any request
/// without a valid Authorization header must receive 401 with WWW-Authenticate
/// containing the resource_metadata URI. This is runnable without the full Aspire
/// stack to the extent the api service is healthy.
///
/// Test 2 (list_databases) exercises the full tool invocation via the MCP protocol.
/// It requires the full Aspire stack (Keycloak + api + gateway) and is CI-gated.
/// Because driving the full MCP streamable-HTTP handshake requires the ModelContextProtocol.Client
/// package (not yet referenced in this test project) and multiple round-trips, this test
/// mints a valid bearer token and asserts it is accepted (200 / MCP response) rather than
/// doing a full tool-call assertion via the MCP client library. A TODO marks the upgrade path.
///
/// Test 3 (run_query) drives a real tools/call over the MCP client and asserts that:
///   - all three tools are listed (list_databases, get_schema, run_query)
///   - run_query returns rows for a SELECT against the seeded database
///   - a query_log row with source = 'Mcp' is persisted in the metadata DB
/// </summary>
public sealed class McpToolsTests(SluiceBaseStackFactory factory)
{
    private KeycloakLoginHelper LoginHelper => new(factory.InitialisedApp);

    // ── Helpers ─────────────────────────────────────────────────────────────────

    /// <summary>Creates a plain (non-authenticated, non-redirect-following) HTTP client pointed at the api.</summary>
    private HttpClient ApiClient() => factory.InitialisedApp.CreateHttpClient("api", "https");

    /// <summary>Registers a new MCP OAuth client via /mcp/oauth/register and returns the client_id.</summary>
    private static async Task<string> RegisterOAuthClientAsync(HttpClient client, string redirectUri, CancellationToken ct)
    {
        var body = new { client_name = "McpToolsTest", redirect_uris = new[] { redirectUri } };
        var resp = await client.PostAsJsonAsync("/mcp/oauth/register", body, ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(ct);
        return json.GetProperty("client_id").GetString()!;
    }

    /// <summary>Mints a valid MCP access token for the given Keycloak user via the full OAuth flow.</summary>
    private async Task<string> MintAccessTokenAsync(string username, string password, CancellationToken ct)
    {
        // Use a synthetic redirect URI on localhost — the authorize endpoint will redirect
        // there with ?code=…; we catch it with AllowAutoRedirect=false.
        const string redirectUri = "https://localhost/mcp-test-tools/callback";
        const string codeVerifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        var codeChallenge = S256Challenge(codeVerifier);

        string clientId;
        using (var setupClient = ApiClient())
        {
            clientId = await RegisterOAuthClientAsync(setupClient, redirectUri, ct);
        }

        var gatewayBase = factory.InitialisedApp.GetEndpoint("gateway", "https");
        var cookieJar = new CookieContainer();

        // Step 1: drive through Keycloak login so we get a session cookie
        using var loginHandler = new HttpClientHandler
        {
            CookieContainer = cookieJar,
            AllowAutoRedirect = true,
            UseCookies = true,
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        };
        using var followClient = new HttpClient(loginHandler) { BaseAddress = gatewayBase };

        var authorizeUrl = $"/mcp/oauth/authorize" +
                           $"?client_id={Uri.EscapeDataString(clientId)}" +
                           $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
                           $"&response_type=code" +
                           $"&code_challenge={Uri.EscapeDataString(codeChallenge)}" +
                           $"&code_challenge_method=S256" +
                           $"&state=mcptools-test";

        var loginPageResp = await followClient.GetAsync(authorizeUrl, ct);
        loginPageResp.EnsureSuccessStatusCode();

        var html = await loginPageResp.Content.ReadAsStringAsync(ct);
        var kcMatch = System.Text.RegularExpressions.Regex.Match(
            html,
            """<form[^>]+id="kc-form-login"[^>]+action="(?<action>[^"]+)""",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        Assert.True(kcMatch.Success, "Could not find Keycloak login form — is the Aspire stack healthy?");

        var loginActionUrl = HttpUtility.HtmlDecode(kcMatch.Groups["action"].Value);
        var loginForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = username,
            ["password"] = password,
            ["credentialId"] = string.Empty,
        });
        var afterLogin = await followClient.PostAsync(loginActionUrl, loginForm, ct);
        await FollowAutoPostFormsAsync(followClient, afterLogin, ct);

        // Step 2: re-issue authorize request with AllowAutoRedirect=false to capture the code
        using var captureHandler = new HttpClientHandler
        {
            CookieContainer = cookieJar,
            AllowAutoRedirect = false,
            UseCookies = true,
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        };
        using var captureClient = new HttpClient(captureHandler) { BaseAddress = gatewayBase };

        var authResp = await captureClient.GetAsync(authorizeUrl, ct);
        Assert.Equal(HttpStatusCode.Redirect, authResp.StatusCode);

        var location = authResp.Headers.Location!;
        var qs = HttpUtility.ParseQueryString(location.Query);
        var code = qs["code"];
        Assert.False(string.IsNullOrEmpty(code), "Expected authorization code in redirect");

        // Step 3: exchange code for tokens
        using var tokenClient = ApiClient();
        var tokenForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = clientId,
            ["code"] = code!,
            ["redirect_uri"] = redirectUri,
            ["code_verifier"] = codeVerifier,
        });
        var tokenResp = await tokenClient.PostAsync("/mcp/oauth/token", tokenForm, ct);
        Assert.Equal(HttpStatusCode.OK, tokenResp.StatusCode);

        var tokenJson = await tokenResp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(ct);
        return tokenJson.GetProperty("access_token").GetString()!;
    }

    private static string S256Challenge(string verifier)
    {
        var bytes = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static async Task<(AuthenticatedSession Session, string Xsrf, string DatabaseId)>
        AdminSessionWithDatabaseAsync(KeycloakLoginHelper loginHelper, DistributedApplication app, CancellationToken ct)
    {
        var session = await loginHelper.SignInAsync("alice", "dev", ct);
        var xsrf = await session.FetchXsrfTokenAsync(ct);

        var users = await session.Client.GetFromJsonAsync<ListUserBody>("/api/admin/user", ct);
        var alice = users!.Users.Single(u => u.Email == "alice@example.com");

        using var grantServer = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/user/{alice.Id}/permission");
        grantServer.Headers.Add("X-XSRF-TOKEN", xsrf);
        grantServer.Content = JsonContent.Create(new { permission = Permissions.ServerManage });
        (await session.Client.SendAsync(grantServer, ct)).EnsureSuccessStatusCode();

        var blueConnStr = await app.GetConnectionStringAsync("blue-appdb", ct);
        var blueBuilder = new NpgsqlConnectionStringBuilder(blueConnStr!);

        var serverName = $"mcp-{Guid.NewGuid():N}"[..24];
        using var sReq = new HttpRequestMessage(HttpMethod.Post, "/api/server");
        sReq.Headers.Add("X-XSRF-TOKEN", xsrf);
        sReq.Content = JsonContent.Create(
            new ServerEndpoints.CreateServerRequest(serverName, "postgres", blueBuilder.Host!, blueBuilder.Port));
        var sResp = await session.Client.SendAsync(sReq, ct);
        sResp.EnsureSuccessStatusCode();
        var server = (await sResp.Content.ReadFromJsonAsync<ServerBody>(ct))!;

        using var cReq = new HttpRequestMessage(HttpMethod.Post, $"/api/server/{server.Id}/credential");
        cReq.Headers.Add("X-XSRF-TOKEN", xsrf);
        cReq.Content = JsonContent.Create(
            new { label = "read", username = blueBuilder.Username, password = blueBuilder.Password });
        var cResp = await session.Client.SendAsync(cReq, ct);
        cResp.EnsureSuccessStatusCode();
        var cred = (await cResp.Content.ReadFromJsonAsync<CredentialBody>(ct))!;

        using var dbReq = new HttpRequestMessage(HttpMethod.Post, $"/api/server/{server.Id}/database");
        dbReq.Headers.Add("X-XSRF-TOKEN", xsrf);
        dbReq.Content = JsonContent.Create(new
        {
            displayName = "mcp-db",
            databaseName = blueBuilder.Database ?? "postgres",
            readCredentialId = cred.Id,
        });
        var dbResp = await session.Client.SendAsync(dbReq, ct);
        dbResp.EnsureSuccessStatusCode();
        var db = (await dbResp.Content.ReadFromJsonAsync<DatabaseBody>(ct))!;

        return (session, xsrf, db.Id);
    }

    // ── Test 1: Bearer gate ──────────────────────────────────────────────────────

    /// <summary>
    /// Verifies the McpBearer authorization policy: a POST to /mcp without an
    /// Authorization header must return 401 and a WWW-Authenticate header that
    /// contains <c>resource_metadata</c>. This covers the deferred check from the
    /// bearer gate added in the prior task.
    /// </summary>
    [Fact]
    public async Task Mcp_WithoutAuthorization_Returns401WithResourceMetadata()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = ApiClient();

        // POST is the MCP streamable-HTTP transport method; GET also works here.
        var resp = await client.PostAsync("/mcp", null, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        Assert.True(
            resp.Headers.TryGetValues("WWW-Authenticate", out var values),
            "Expected WWW-Authenticate header on 401");
        var wwwAuth = string.Join(", ", values!);
        Assert.Contains("resource_metadata", wwwAuth, StringComparison.OrdinalIgnoreCase);
    }

    // ── Test 2: list_databases via MCP bearer ──────────────────────────────────

    /// <summary>
    /// End-to-end smoke test for the list_databases tool.
    ///
    /// This test:
    ///   1. Creates an admin session, registers a server + database, grants bob query:execute.
    ///   2. Mints an MCP access token for bob via the full OAuth flow.
    ///   3. Sends a POST /mcp request with Authorization: Bearer &lt;token&gt; and asserts
    ///      the response is NOT 401/403 (i.e., the bearer handler authenticates the request).
    ///
    /// A full MCP tool-call assertion (sending initialize + tools/call JSON-RPC) requires the
    /// ModelContextProtocol.Client package which is not yet referenced in the test project.
    /// The bearer-acceptance assertion here confirms end-to-end wiring (DI, auth policy, route).
    /// Full tool invocation is exercised by CI via the existing McpTokenServiceTests +
    /// OAuthFlowTests together with the bearer-acceptance check here.
    ///
    /// TODO: add ModelContextProtocol.Client to the test project and drive the full
    ///       tools/list + tools/call sequence once the MCP client API stabilizes.
    /// </summary>
    [Fact]
    public async Task ListDatabases_WithValidBearerToken_IsAuthenticated()
    {
        var ct = TestContext.Current.CancellationToken;
        var (adminSession, adminXsrf, databaseId) = await AdminSessionWithDatabaseAsync(LoginHelper, factory.InitialisedApp, ct);
        using var admin = adminSession;

        // Ensure bob's user row exists
        using var bobSession = await LoginHelper.SignInAsync("bob", "dev", ct);
        await bobSession.Client.GetAsync("/api/me", ct);

        var users = await admin.Client.GetFromJsonAsync<ListUserBody>("/api/admin/user", ct);
        var bob = users!.Users.Single(u => u.Email == "bob@example.com");

        // Grant bob query:execute on the database so list_databases will return something
        await DatabaseRoleTestHelper.AssignByDatabaseAsync(admin, bob.Id, Permissions.QueryExecute, databaseId, adminXsrf, ct);

        // Mint an MCP access token for bob via the full OAuth flow
        var accessToken = await MintAccessTokenAsync("bob", "dev", ct);
        Assert.False(string.IsNullOrEmpty(accessToken), "Expected a non-empty access token");

        // POST to /mcp with Authorization: Bearer <token>
        // The MCP streamable-HTTP transport uses POST with Content-Type: application/json.
        // We send a minimal MCP initialize request. The important assertion is that the
        // server does NOT return 401 or 403 — the McpBearer handler authenticated the request.
        using var client = ApiClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, "/mcp");
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        req.Content = new StringContent(
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"test","version":"0"}}}""",
            Encoding.UTF8,
            "application/json");

        var resp = await client.SendAsync(req, ct);

        // Must NOT be 401 (unauthenticated) or 403 (forbidden)
        Assert.NotEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    // ── Test 3: run_query via MCP client round-trip ──────────────────────────────

    /// <summary>
    /// Full MCP client round-trip test for run_query.
    ///
    /// This test:
    ///   1. Creates an admin session, seeds a server + database, grants bob query:execute.
    ///   2. Mints an MCP access token for bob.
    ///   3. Connects the ModelContextProtocol.Client via HttpClientTransport (streamable HTTP)
    ///      with the bearer token on the underlying HttpClient.
    ///   4. Calls tools/list and asserts list_databases, get_schema, and run_query are present.
    ///   5. Calls run_query with a SELECT and asserts the result contains content (rows).
    ///   6. Queries the metadata DB and asserts a query_log row with source = 'Mcp' was persisted.
    /// </summary>
    [Fact]
    public async Task RunQuery_ViaMcpClient_ReturnsRowsAndLogsSourceMcp()
    {
        var ct = TestContext.Current.CancellationToken;
        var (adminSession, adminXsrf, databaseId) = await AdminSessionWithDatabaseAsync(LoginHelper, factory.InitialisedApp, ct);
        using var admin = adminSession;

        // Ensure bob's user row exists
        using var bobSession = await LoginHelper.SignInAsync("bob", "dev", ct);
        await bobSession.Client.GetAsync("/api/me", ct);

        var users = await admin.Client.GetFromJsonAsync<ListUserBody>("/api/admin/user", ct);
        var bob = users!.Users.Single(u => u.Email == "bob@example.com");

        // Grant bob query:execute on the database
        await DatabaseRoleTestHelper.AssignByDatabaseAsync(admin, bob.Id, Permissions.QueryExecute, databaseId, adminXsrf, ct);

        // Mint a bearer token for bob
        var accessToken = await MintAccessTokenAsync("bob", "dev", ct);

        // Build an HttpClient with the Authorization header preset and bypassing TLS validation
        // (Aspire dev certs are self-signed in test environments).
        var apiEndpoint = factory.InitialisedApp.GetEndpoint("api", "https");
        var mcpEndpoint = new Uri(apiEndpoint, "/mcp");

        var innerHandler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        };
        var httpClient = new HttpClient(innerHandler)
        {
            BaseAddress = mcpEndpoint,
        };
        httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        var transportOptions = new HttpClientTransportOptions
        {
            Endpoint = mcpEndpoint,
            TransportMode = HttpTransportMode.StreamableHttp,
        };

        await using var transport = new HttpClientTransport(transportOptions, httpClient, loggerFactory: null, ownsHttpClient: true);
        await using var mcpClient = await McpClient.CreateAsync(transport, cancellationToken: ct);

        // 4. Assert all three tools are discoverable
        var tools = await mcpClient.ListToolsAsync(cancellationToken: ct);
        var toolNames = tools.Select(t => t.Name).ToArray();
        Assert.Contains("list_databases", toolNames);
        Assert.Contains("get_schema", toolNames);
        Assert.Contains("run_query", toolNames);

        // 5. Call run_query and assert the response has content (not an error)
        // Use a comment containing a unique token so we can locate the log row.
        var uniqueMarker = $"mcp-test-{Guid.NewGuid():N}";
        var sql = $"SELECT 1 AS value -- {uniqueMarker}";

        var callResult = await mcpClient.CallToolAsync(
            "run_query",
            new Dictionary<string, object?>
            {
                ["databaseId"] = databaseId,
                ["sql"] = sql,
            },
            cancellationToken: ct);

        Assert.False(callResult.IsError, $"run_query returned an error: {string.Join("; ", callResult.Content.Select(c => c is ModelContextProtocol.Protocol.TextContentBlock t ? t.Text : string.Empty))}");
        Assert.NotEmpty(callResult.Content);

        // 6. Verify a query_log row with source = 'Mcp' was persisted
        var metaConnStr = await factory.InitialisedApp.GetConnectionStringAsync("metadata-db", ct);
        await using var conn = new NpgsqlConnection(metaConnStr);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT source FROM query_log WHERE query_text LIKE @marker LIMIT 1", conn);
        cmd.Parameters.AddWithValue("@marker", $"%{uniqueMarker}%");
        var sourceValue = await cmd.ExecuteScalarAsync(ct);

        Assert.NotNull(sourceValue);
        Assert.Equal("Mcp", sourceValue!.ToString());
    }

    // ── Follow-redirect helpers (same as OAuthFlowTests) ────────────────────────

    private static async Task FollowAutoPostFormsAsync(HttpClient client, HttpResponseMessage response, CancellationToken ct)
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

    // ── Private record types ─────────────────────────────────────────────────────

    private sealed record ListUserBody(UserRow[] Users);
    private sealed record UserRow(string Id, string Email);
    private sealed record ServerBody(string Id, string Name);
    private sealed record CredentialBody(string Id, string Label);
    private sealed record DatabaseBody(string Id, string DisplayName);
}
