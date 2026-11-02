# MCP OAuth CIMD Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** MCP clients identify themselves only through a Client ID Metadata Document (CIMD) or as an operator-configured client. DCR is removed. Every authorization response carries an RFC 9207 `iss` derived from an operator-configured public base URL. Consent is shown only when a code would go to a non-loopback callback of a CIMD client.

**Architecture:** One `IMcpClientResolver` turns a `client_id` into a `ResolvedClient`, first from `Mcp:Clients` config and then by an SSRF-guarded, cached CIMD fetch. Authorize (GET/POST) and token use it instead of the `McpOAuthClient` table, which is dropped. `McpIssuer` is the single source of the base URL for metadata, `iss` and the bearer challenge.

**Tech Stack:** .NET 10, ASP.NET Core minimal APIs, EF Core (Npgsql), Aspire 13.6 AppHost + `Aspire.Hosting.Testing`, xunit.v3 on MTP, React/TS (Vitest, ESLint).

**Spec:** `docs/superpowers/specs/2026-10-18-mcp-oauth-cimd-design.md`

## Global Constraints

- Branch `feat/mcp-oauth-consent-redirect-policy`. Commit messages are a single subject line with no body.
- Minimal comments: only for a non-obvious "why". Preserve existing comments unless they become false.
- Never hand-edit EF migrations. Generate them with `dotnet ef migrations add`.
- Suppress experimental-API warnings with an inline `#pragma warning disable`, never with `<NoWarn>`.
- TypeScript: `Array<T>`, never `T[]`. Run `npm run lint` in `src/frontend` for any frontend change.
- Public clients only: `token_endpoint_auth_methods_supported` stays `["none"]`.
- `PublicBaseUrl` is an origin only (scheme, host, optional port), canonicalised without a trailing `/`.
- CIMD fetch limits:
  - timeout 5 s,
  - maximum body 64 KiB,
  - no redirects, no proxy, at most 4 concurrent fetches,
  - positive TTL is `max-age` clamped to [5 min, 24 h], default 1 h,
  - negative TTL 1 min,
  - stale-on-error up to 7 days past TTL.
- `Mcp:Cimd:AllowLoopbackForTesting=true` outside the `Development` environment must throw at startup.
- Integration tests need the Aspire stack. Locally, `dotnet build` and `dotnet test --project tests/SluiceBase.Api.Tests` are the gate. Run the integration tests if the stack is healthy, otherwise rely on CI.

## Review Focus

1. **`PublicBaseUrl` written with a trailing slash or uppercase host** (`https://Sluice.Example.com/`). The metadata `issuer`, protected-resource `authorization_servers[0]` and every `iss` must be the identical string. Test in Task 1: `Issuer_IsIdenticalAcrossMetadataAndIss`.
2. **A portless loopback redirect in a CIMD document** (`http://127.0.0.1/callback/abc`, as Codex and Claude Code publish) against a request with an ephemeral port and the same path. It must match, while a different path or query must not. Test in Task 2.
3. **Disguised private targets:**
   - IPv4-mapped IPv6 (`::ffff:10.0.0.1`, `::ffff:169.254.169.254`),
   - `0.0.0.0`, `[::]`,
   - CGNAT `100.64.1.1`,
   - a hostname resolving to both a public and a private address.
   All must be refused. Test in Task 5.
4. **A `client_id` that differs from the document's `client_id` only by a trailing slash, letter case or a default `:443`.** Must be rejected, since the match is ordinal. Test in Task 4.
5. **A chunked response with no `Content-Length` that streams past 64 KiB.** It must be aborted while being read, and treated as an invalid document, not a temporary failure. Test in Task 5.

---

### Task 1: Public base URL, issuer, and metadata flags

**Files:**
- Modify: `src/SluiceBase.Api/Mcp/McpOptions.cs`
- Create: `src/SluiceBase.Api/Mcp/McpIssuer.cs`
- Modify: `src/SluiceBase.Api/Mcp/Oauth/OAuthMetadataEndpoints.cs`
- Modify: `src/SluiceBase.Api/Mcp/McpBearerAuthenticationHandler.cs`
- Modify: `src/SluiceBase.Api/Program.cs` (startup warning when unset)
- Test: `tests/SluiceBase.Api.Tests/McpOptionsTests.cs`, `tests/IntegrationTests/OAuthMetadataTests.cs`

**Interfaces:**
- Produces:
  - `McpOptions.PublicBaseUrl : string?`
  - `string? McpOptions.GetValidatedPublicBaseUrl(ILogger? logger = null)`, which returns a canonical origin or `null`
  - `static string McpIssuer.BaseUrl(HttpContext ctx, McpOptions options)`

- [ ] **Step 1: Write failing unit tests** in `McpOptionsTests`:
  - `GetValidatedPublicBaseUrl_Canonicalises`. Theory: `"https://sluice.example.com/"` → `"https://sluice.example.com"`; `"https://Sluice.Example.com"` → `"https://sluice.example.com"`; `"http://localhost:5443"` → `"http://localhost:5443"`; `"https://sluice.example.com:443"` → `"https://sluice.example.com"`.
  - `GetValidatedPublicBaseUrl_RejectsNonOrigins`. Theory: `"https://x.example/sluice"`, `"https://x.example?a=1"`, `"https://x.example#f"`, `"ftp://x.example"`, `"x.example"`, `"https://user@x.example"` → `null`.
  - `GetValidatedPublicBaseUrl_NullOrEmpty_ReturnsNull`.
- [ ] **Step 2: Run** `dotnet test --project tests/SluiceBase.Api.Tests --filter "FullyQualifiedName~McpOptionsTests"`. Expected: FAIL to compile.
- [ ] **Step 3: Implement.**
  - Build the canonical value from `Uri.GetLeftPart(UriPartial.Authority)`, which lowercases the host and drops the default port.
  - Log a warning through a new `[LoggerMessage]` in `McpLoggerMessage` when the value is non-empty but invalid.
  - `McpIssuer.BaseUrl` returns the validated value, or falls back to `$"{ctx.Request.Scheme}://{ctx.Request.Host}"`.
  - Use it in both metadata endpoints and in `HandleChallengeAsync`.
  - Add `["authorization_response_iss_parameter_supported"] = true` and `["client_id_metadata_document_supported"] = true` to the authorization-server metadata. Keep `registration_endpoint` for now; Task 9 removes it.
  - In `Program.cs`, when MCP is enabled and `GetValidatedPublicBaseUrl()` is null, log one warning: "Mcp:PublicBaseUrl is not set; the OAuth issuer is derived from request headers. Set it when running behind a reverse proxy."
- [ ] **Step 4: Run the unit tests.** Expected: PASS.
- [ ] **Step 5: Extend `OAuthMetadataTests.OAuthAuthorizationServer_ReturnsOk_WithRequiredFields`** to assert both new flags are `true`. Add `Issuer_IsIdenticalAcrossMetadataAndIss`, which for now asserts `issuer == authorization_servers[0]` and that neither ends with `/`. Task 8 extends it with `iss`.
- [ ] **Step 6: `dotnet build`** with 0 errors. Run the integration tests if the stack is healthy.
- [ ] **Step 7: Commit** `Add Mcp:PublicBaseUrl as the single OAuth issuer source`

### Task 2: Narrow the redirect-URI policy to CIMD documents

**Files:**
- Modify: `src/SluiceBase.Api/Mcp/Oauth/McpRedirectUriPolicy.cs`
- Test: `tests/SluiceBase.Api.Tests/McpRedirectUriPolicyTests.cs`

**Interfaces:**
- Produces:
  - `static bool McpRedirectUriPolicy.IsAllowedForCimd(string redirectUri)`
  - `static bool McpRedirectUriPolicy.IsLoopback(string redirectUri)` (now public)
  - `static bool McpRedirectUriPolicy.Matches(string registered, string requested)` (unchanged)
- Retires (deleted in Task 9): `IsAllowed(string, IReadOnlyCollection<string>)`, `IsConforming`, `NativeAppCallbacks`. Their callers (registration, token, remediation) still exist until Tasks 8–9, so keep the old members unchanged alongside the new ones for now; Task 9 deletes them.

- [ ] **Step 1: Rewrite the tests:**
  - `IsAllowedForCimd_Accepts`: `http://127.0.0.1:8080/cb`, `http://localhost/cb`, `http://[::1]:9/cb`, `https://claude.ai/api/mcp/auth_callback`, `https://chatgpt.com/connector_platform_oauth_redirect`, `cursor://anysphere.cursor-mcp/oauth/callback`.
  - `IsAllowedForCimd_Rejects`: `http://example.com/cb`, `http://10.0.0.5/cb`, `https://u:p@claude.ai/cb`, `https://claude.ai/cb#x`, `http://127.0.0.1\@evil.com/`, `https://claude.ai/c b`, `""`, `"not a uri"`. Keep the existing parser-confusion cases.
  - `Matches_PortlessRegisteredLoopback_MatchesEphemeralPort`: `("http://127.0.0.1/callback/abc", "http://127.0.0.1:53111/callback/abc")` → true.
  - `Matches_PortlessRegisteredLoopback_DifferentPathOrQuery_False`: `("http://127.0.0.1/callback/abc", "http://127.0.0.1:53111/callback/xyz")` and `("http://127.0.0.1/cb?a=1", "http://127.0.0.1:5/cb?a=2")` → false.
  - `Matches_Https_RequiresExact`: `("https://claude.ai/cb", "https://claude.ai/cb/")` → false.
- [ ] **Step 2: Run** with filter `McpRedirectUriPolicyTests`. Expected: FAIL.
- [ ] **Step 3: Implement `IsAllowedForCimd`.**
  - Apply the shared character/userinfo/fragment rejection to every scheme.
  - Then accept: `http` + loopback, `https` with a non-empty host, or any other scheme that isn't `http`, `https`, `javascript`, `data`, `file` or `vbscript` (a private-use scheme).
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit** `Narrow MCP redirect policy to CIMD-listed callbacks`

### Task 3: Operator-configured clients and the resolver contract

**Files:**
- Modify: `src/SluiceBase.Api/Mcp/McpOptions.cs`
- Create: `src/SluiceBase.Api/Mcp/Clients/ResolvedClient.cs`, `IMcpClientResolver.cs`, `McpClientResolver.cs`
- Test: `tests/SluiceBase.Api.Tests/McpClientResolverTests.cs`

**Interfaces:**
- Produces:
  - `sealed class McpConfiguredClient { string ClientId; string Name; List<string> RedirectUris }`
  - `McpOptions.Clients : List<McpConfiguredClient>`, defaulting to `[]`
  - `IReadOnlyList<McpConfiguredClient> McpOptions.GetValidatedClients(ILogger? logger = null)`, which skips entries with an empty id, no URIs, a non-absolute URI or a duplicate id, with one warning each
  - `enum ClientKind { Configured, Cimd }`
  - `sealed record ResolvedClient(string ClientId, string DisplayName, string? VerifiedHost, IReadOnlyList<string> RedirectUris, ClientKind Kind)`
  - `abstract record ClientResolution`, with these cases:
    - `Found(ResolvedClient Client)`
    - `NotFound`
    - `TemporarilyUnavailable`
  - `interface IMcpClientResolver { Task<ClientResolution> ResolveAsync(string clientId, CancellationToken ct); }`
  - `McpClientResolver(IOptions<McpOptions>, ICimdClientSource, ILogger<McpClientResolver>)`. `ICimdClientSource` is defined in Task 6. For this task, add the interface signature `Task<ClientResolution> ResolveAsync(Uri clientIdUrl, CancellationToken ct)` and a test fake.

- [ ] **Step 1: Write the tests:**
  - `Resolve_ConfiguredId_ReturnsConfigured`: Kind `Configured`, `VerifiedHost` null, and `ICimdClientSource` not called.
  - `Resolve_ConfiguredHttpsId_WinsOverCimd`.
  - `Resolve_HttpsId_DelegatesToCimdSource`.
  - `Resolve_UnknownNonHttpsId_NotFound` with theory data `"0b8e…guid"`, `"http://x.example/c.json"`, `""`.
  - `GetValidatedClients_SkipsInvalidAndDuplicate`.
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.** Use an exact, ordinal match on `ClientId`. Only `Uri.TryCreate(..., Absolute)` with scheme `https` goes to the CIMD source; the URL shape is validated in Task 4. Register `IMcpClientResolver` as a singleton in `Program.cs` inside the MCP-enabled block.
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit** `Add MCP client resolver with operator-configured clients`

### Task 4: CIMD URL and document validation (pure)

**Files:**
- Create: `src/SluiceBase.Api/Mcp/Clients/CimdDocument.cs`
- Test: `tests/SluiceBase.Api.Tests/CimdDocumentTests.cs`

**Interfaces:**
- Produces:
  - `static bool CimdDocument.IsAcceptableClientIdUrl(string clientId, out Uri url)`
  - `static ResolvedClient? CimdDocument.Validate(Uri clientIdUrl, JsonElement document)`, which returns null when the document is invalid

- [ ] **Step 1: Write the tests.**
  - `IsAcceptableClientIdUrl` accepts `https://claude.ai/oauth/claude-code-client-metadata`, `https://chatgpt.com/oauth/client.json` and `https://x.example:8443/c.json`.
  - `IsAcceptableClientIdUrl` rejects:
    - `https://x.example`, `https://x.example/`,
    - `https://u@x.example/c`, `https://x.example/c#f`,
    - `https://x.example/a/../c`, `https://x.example/./c`,
    - `http://x.example/c`.
  - `Validate_HappyPath`:
    - `client_id` equals the URL, `client_name` is "ChatGPT", and `redirect_uris` is `["https://chatgpt.com/connector_platform_oauth_redirect", "http://evil.example/cb"]`,
    - the result has `DisplayName` "ChatGPT", `VerifiedHost` "chatgpt.com", only the https URI kept, and Kind `Cimd`.
  - `Validate_ClientIdMismatch_Null`. Theory against the URL `https://chatgpt.com/oauth/client.json`:
    - document id `https://chatgpt.com/oauth/client.json/`,
    - `https://ChatGPT.com/oauth/client.json`,
    - `https://chatgpt.com:443/oauth/client.json`.
  - `Validate_NoUsableRedirects_Null`, `Validate_SecretPresent_Null` (`client_secret`), `Validate_AuthMethodNotNone_Null` (`token_endpoint_auth_method: "private_key_jwt"` with no `none` in the plural list).
  - `Validate_AuthMethodsPluralIncludesNone_Ok`.
  - `Validate_NoClientName_DisplaysHost`.
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.** Compare against the raw request string with `string.Equals(..., Ordinal)`, not against a normalised `Uri`. Filter `redirect_uris` with `McpRedirectUriPolicy.IsAllowedForCimd`.
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit** `Validate CIMD client ids and metadata documents`

### Task 5: SSRF-guarded CIMD fetcher

**Files:**
- Create: `src/SluiceBase.Api/Mcp/Clients/PublicAddress.cs`, `CimdFetcher.cs`
- Modify: `src/SluiceBase.Api/Mcp/McpOptions.cs` → add `Cimd` sub-options, `src/SluiceBase.Api/Program.cs`
- Test: `tests/SluiceBase.Api.Tests/PublicAddressTests.cs`, `tests/SluiceBase.Api.Tests/CimdFetcherTests.cs`

**Interfaces:**
- Produces:
  - `static bool PublicAddress.IsPublic(IPAddress address)`
  - `McpOptions.Cimd : McpCimdOptions { bool AllowLoopbackForTesting }`
  - `abstract record FetchResult`, with these cases:
    - `Ok(JsonElement Document, TimeSpan? MaxAge)`
    - `Invalid`
    - `Transient`
  - `interface ICimdFetcher { Task<FetchResult> FetchAsync(Uri url, CancellationToken ct); }`
  - `sealed class CimdFetcher(IHttpClientFactory, IOptions<McpOptions>)`, which uses the named client `"cimd"`
  - `static SocketsHttpHandler CimdFetcher.CreateHandler(bool allowLoopback)`

- [ ] **Step 1: Write `PublicAddressTests`:**
  - `IsPublic_False` for `127.0.0.1`, `::1`, `10.1.2.3`, `172.16.0.1`, `192.168.1.1`, `169.254.169.254`, `100.64.1.1`, `0.0.0.0`, `::`, `fe80::1`, `fc00::1`, `fd12::1`, `224.0.0.1`, `255.255.255.255`, `::ffff:10.0.0.1`, `::ffff:169.254.169.254`, `ff02::1`.
  - `IsPublic_True` for `1.1.1.1`, `160.79.104.10`, `2606:4700:4700::1111`.
- [ ] **Step 2: Write `CimdFetcherTests`** against a `HttpMessageHandler` stub (pass the stub in through an internal constructor overload):
  - `Fetch_200Json_Ok_ReadsMaxAge`: `Cache-Control: max-age=600` → `MaxAge` 10 min.
  - `Fetch_3xx_Invalid`, `Fetch_404_Invalid`, `Fetch_NonJsonContentType_Invalid`, `Fetch_MalformedJson_Invalid`.
  - `Fetch_5xx_Transient`, `Fetch_Timeout_Transient` (stub delays past a 5 s timeout; use an injectable timeout to keep the test fast), `Fetch_HttpRequestException_Transient`.
  - `Fetch_ChunkedBodyOver64KiB_Invalid`: no `Content-Length`, 70 KiB streamed.
  - `Fetch_FifthConcurrent_Transient`: four in flight, blocked on a `TaskCompletionSource`; the fifth returns `Transient` immediately.
  - `ConnectGuard_RefusesPrivateResolution`: call the extracted `ConnectCallback` logic, given a resolver delegate returning `[1.1.1.1, 10.0.0.1]` → throws/refuses. Refuse if **any** resolved address is non-public, and connect only to a vetted address.
- [ ] **Step 3: Run both.** Expected: FAIL.
- [ ] **Step 4: Implement.**
  - The handler has `AllowAutoRedirect = false`, `UseProxy = false`, `AutomaticDecompression = All`, and a `ConnectCallback` that runs `Dns.GetHostAddressesAsync`, applies `PublicAddress.IsPublic` (or also allows loopback when `allowLoopback`), and opens a `Socket` to the first vetted address.
  - When `allowLoopback` is set, `SslOptions.RemoteCertificateValidationCallback` accepts any certificate only if the connected remote endpoint is loopback, and otherwise uses default validation.
  - The body is read through a bounded stream of 64 KiB.
  - A static `SemaphoreSlim(4)` gates fetches with `WaitAsync(0)`.
  - In `Program.cs`:
    - register `AddHttpClient("cimd")` with `ConfigurePrimaryHttpMessageHandler(() => CimdFetcher.CreateHandler(...))` and a 5 s timeout,
    - register `ICimdFetcher` as a singleton,
    - throw `InvalidOperationException("Mcp:Cimd:AllowLoopbackForTesting is only permitted in the Development environment.")` when the flag is set and `!builder.Environment.IsDevelopment()`.
- [ ] **Step 5: Run.** Expected: PASS.
- [ ] **Step 6: Commit** `Add SSRF-guarded CIMD document fetcher`

### Task 6: CIMD cache and resolver wiring

**Files:**
- Create: `src/SluiceBase.Api/Mcp/Clients/CimdClientSource.cs`
- Modify: `tests/SluiceBase.Api.Tests/SluiceBase.Api.Tests.csproj` (add `Microsoft.Extensions.TimeProvider.Testing`, latest 10.x)
- Test: `tests/SluiceBase.Api.Tests/CimdClientSourceTests.cs`

**Interfaces:**
- Consumes: `ICimdFetcher`, `CimdDocument.Validate`, `CimdDocument.IsAcceptableClientIdUrl` (Tasks 4–5), and `ICimdClientSource` (Task 3).
- Produces: `sealed class CimdClientSource(ICimdFetcher, TimeProvider) : ICimdClientSource`, registered as a singleton.

- [ ] **Step 1: Write the tests** with `FakeTimeProvider` and a counting fake fetcher:
  - `Resolve_CachesPositive_ForMaxAgeClamped`: `max-age=60` → treated as 5 min, so there's no refetch at 4 min and a refetch at 6 min.
  - `Resolve_MaxAgeAbove24h_ClampedTo24h`, `Resolve_NoMaxAge_Defaults1h`.
  - `Resolve_InvalidDocument_NotFound_CachedFor1Min`.
  - `Resolve_TransientWithLastGood_ServesStale`: after the TTL expires, the fetcher returns `Transient` and the result is still `Found`.
  - `Resolve_TransientBeyond7DaysStale_TemporarilyUnavailable`.
  - `Resolve_TransientNoLastGood_TemporarilyUnavailable_NotNegativeCached`: the next call fetches again.
  - `Resolve_UnacceptableUrl_NotFound_NoFetch`.
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement** a `ConcurrentDictionary<string, Entry>` keyed by the exact `client_id` string. `Entry` holds the last good `ResolvedClient`, `FreshUntil`, `StaleUntil` and `NegativeUntil`. Don't use `IMemoryCache`; it doesn't take a `TimeProvider`, and the tests need a controllable clock.
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit** `Cache CIMD clients with stale-on-error`

### Task 7: Integration-test infrastructure (configured client, public URL, CIMD stub)

**Files:**
- Modify: `src/AppHost/Program.cs`
- Create: `tests/IntegrationTests/Supports/CimdStubServer.cs`
- Modify: `tests/IntegrationTests/Supports/SluiceBaseStackFactory.cs`

**Interfaces:**
- Produces:
  - Dev API environment:
    - `Mcp__PublicBaseUrl=https://localhost:5443`,
    - `Mcp__Cimd__AllowLoopbackForTesting=true`,
    - `Mcp__Clients__0__ClientId=sluicebase-dev`, `Name=SluiceBase dev client`, `RedirectUris__0=http://127.0.0.1:43123/callback`.
  - `CimdStubServer` constants:
    - `Port = 47443`,
    - `LoopbackClientId = "https://localhost:47443/loopback.json"`, with redirect `http://127.0.0.1/callback`, portless,
    - `WebClientId = "https://localhost:47443/web.json"`, with redirect `https://client.example/cb` and `client_name` "Example Web Tool",
    - `MismatchClientId = "https://localhost:47443/mismatch.json"`, whose document `client_id` has a trailing `/`.
  - `SluiceBaseStackFactory.CimdStub : CimdStubServer`, started before the Aspire app.

- [ ] **Step 1: Implement `CimdStubServer`.** It's an in-process `WebApplication` on Kestrel at `https://localhost:47443`, using the dev certificate and serving the three JSON documents with `Cache-Control: max-age=300`. The API runs as a host process, so loopback reaches it.
- [ ] **Step 2: Add the AppHost environment variables.** Add one short comment explaining that the test-only CIMD flag is Development-only and enforced at API startup.
- [ ] **Step 3: Start the stub in `SluiceBaseStackFactory.InitializeAsync`** before `BuildAsync`, and dispose it in `DisposeAsync`.
- [ ] **Step 4: `dotnet build`.** Expected: 0 errors. Start the AppHost (`aspire start`) and confirm the API is healthy and `curl -k https://localhost:5443/.well-known/oauth-authorization-server` shows `"issuer":"https://localhost:5443"`.
- [ ] **Step 5: Commit** `Add CIMD stub and dev MCP client config for integration tests`

### Task 8: Rewire authorize/token: resolver, conditional consent, `iss`

**Files:**
- Modify: `src/SluiceBase.Api/Mcp/Oauth/OAuthAuthorizeEndpoints.cs`, `OAuthTokenEndpoints.cs`, `McpConsentPage.cs`
- Modify: `src/SluiceBase.Api/Mcp/IMcpTokenService.cs`
- Test: `tests/IntegrationTests/OAuthFlowTests.cs`, `tests/IntegrationTests/McpToolsTests.cs`, `tests/IntegrationTests/OAuthMetadataTests.cs`, `tests/SluiceBase.Api.Tests/McpConsentPageTests.cs`

**Interfaces:**
- Consumes: `IMcpClientResolver`, `ResolvedClient`, `ClientResolution` (Task 3), `McpRedirectUriPolicy.Matches`/`IsLoopback` (Task 2), `McpIssuer.BaseUrl` (Task 1), `CimdStubServer` ids (Task 7).
- Produces:
  - `Task<bool> IMcpTokenService.HasLiveGrantAsync(string grantType, string clientId, string secret, CancellationToken ct)`, which is true when an unconsumed, unexpired code (`authorization_code`) or an unrevoked, unexpired refresh token (`refresh_token`) with that hash exists for `clientId`
  - `static bool OAuthAuthorizeEndpoints.RequiresConsent(ResolvedClient client, string redirectUri)`, which is `client.Kind == ClientKind.Cimd && !McpRedirectUriPolicy.IsLoopback(redirectUri)`
  - `McpConsentPage.Render(HttpContext, AuthorizeRequest, ResolvedClient, string? userLabel, AntiforgeryTokenSet, BrandingOptions)`

- [ ] **Step 1: Rewrite the `OAuthFlowTests` helpers and negative tests.** Delete `RegisterClientAsync`/`PostRegistrationAsync` and the `Register_*` tests.
  - Use `sluicebase-dev` (configured) and the `CimdStubServer` ids.
  - The shape errors (`Authorize_WithMissingCodeChallenge_Returns400`, `Authorize_WithWrongResponseType_Returns400`) still return 400 before login.
  - New: `Authorize_SignedOut_UnknownClient_RedirectsToLogin`, which asserts a 302 to the OIDC authority and that the stub saw no request, and `Authorize_SignedIn_UnknownClient_Returns400`.
  - Adapt `Authorize_WithUnregisteredRedirectUri_Returns400`, `Authorize_WithUnknownClientId_Returns400` and `Token_WithUnknownClient_Returns401InvalidClient` to the configured or stub ids.
- [ ] **Step 2: Add the CIMD flow tests** (signed in via `LoginHelper`):
  - `Authorize_CimdLoopback_IssuesCodeWithoutConsent_WithIss`:
    - uses `LoopbackClientId` and `http://127.0.0.1:43123/callback`,
    - expects a 302 to the callback with `code`, `state` and `iss == "https://localhost:5443"`.
  - `Authorize_ConfiguredClient_IssuesCodeWithoutConsent`.
  - `Authorize_CimdWeb_ShowsConsentWithVerifiedHost`: expects a 200 HTML page containing `localhost` (the verified host) and "Example Web Tool", and no `Location` header.
  - `Authorize_CimdWeb_Allow_RedirectsWithCodeAndIss`, and `Authorize_Deny_RedirectsWithAccessDeniedAndIss`.
  - `Authorize_CimdMismatch_Returns400`.
  - `FullFlow_CimdLoopback_CodeExchangeAndRefresh_ReturnsTokens`: authorize, then token, then refresh, all with `client_id = LoopbackClientId`.
  - `Token_WithBogusCode_DoesNotFetchUnknownCimdUrl`: posts `client_id=https://localhost:47443/never.json` with a bogus code, expects 400 `invalid_grant`, and asserts the stub's request log has no `/never.json`.
  - Extend `Issuer_IsIdenticalAcrossMetadataAndIss` to compare a real `iss` from the loopback flow.
- [ ] **Step 3: Update `McpToolsTests`** token acquisition to use the configured `sluicebase-dev` client, and `McpConsentPageTests` to pass a `ResolvedClient` and assert:
  - the verified host is the headline,
  - `client_name` is secondary,
  - the old "can't verify it" line is absent.
- [ ] **Step 4: Implement GET authorize.**
  1. Shape check, returning a 400 `invalid_request` as now.
  2. If not signed in, `ChallengeBackTo`.
  3. `ResolveAsync`: `NotFound` → 400 `invalid_client_or_redirect_uri`; `TemporarilyUnavailable` → 400 `temporarily_unavailable`.
  4. Check `redirect_uri` with `Matches` against `client.RedirectUris`, and on failure return the same 400.
  5. If `RequiresConsent`, render the page. Otherwise issue the code and redirect.

  POST authorize: shape check, sign-in, resolve, match, antiforgery, then decision. Approving a request that didn't require consent is allowed (builder's call, taken as the simpler option).
- [ ] **Step 5: Add `iss` in `RedirectToClient`** for every parameter set: `("iss", McpIssuer.BaseUrl(ctx, options))`.
- [ ] **Step 6: Implement the token endpoint.**
  1. Read the form.
  2. If the grant isn't `authorization_code` or `refresh_token`, return 400 `invalid_grant` as now.
  3. If `!HasLiveGrantAsync(...)`, return 400 `invalid_grant`, without resolving anything.
  4. Resolve: `NotFound` → 401 `invalid_client`; `TemporarilyUnavailable` → 503 `{"error":"temporarily_unavailable"}`.
  5. Then the existing redeem/refresh.
- [ ] **Step 7: `dotnet build` and unit tests.** Expected: PASS. Run the integration tests if the stack is healthy (`dotnet test --project tests/IntegrationTests --filter "OAuthFlowTests|OAuthMetadataTests|McpToolsTests"`). Otherwise, note in the report that they weren't run locally.
- [ ] **Step 8: Commit** `Resolve MCP clients via CIMD or config with conditional consent and iss`

### Task 9: Remove DCR and the client table

**Files:**
- Delete:
  - `src/SluiceBase.Api/Mcp/Oauth/OAuthRegistrationEndpoints.cs`, `McpOAuthClientRemediation.cs`,
  - `src/SluiceBase.Core/Mcp/McpOAuthClient.cs`, `McpOAuthClientId.cs`,
  - `src/SluiceBase.Api/Data/Configurations/McpOAuthClientConfiguration.cs`,
  - `tests/IntegrationTests/McpOAuthClientRemediationTests.cs`.
- Modify:
  - `src/SluiceBase.Api/Data/AppDbContext.cs`, `Program.cs` (the registration mapping and the remediation call),
  - `McpOptions.cs` (remove `AllowedRedirectUris`), `McpRedirectUriPolicy.cs` (remove any shims),
  - `OAuthMetadataEndpoints.cs` (remove `registration_endpoint`),
  - `tests/IntegrationTests/OAuthMetadataTests.cs`, `OpenApiTests.cs` if it enumerates paths.
- Generate: an EF migration `RemoveMcpOAuthClient`, `src/SluiceBase.Api/openapi.json`, `src/frontend/src/api/schema.ts`.

- [ ] **Step 1: Update `OAuthMetadataTests`.**
  - Delete `Register_WithValidRedirectUri_Returns201AndClientId` and `Register_WithEmptyRedirectUris_Returns400`.
  - Assert that the authorization-server metadata has no `registration_endpoint` property.
  - Add `Register_Returns404`: `POST /mcp/oauth/register` → 404.
- [ ] **Step 2: Delete the files and references.** Then `dotnet build`, expecting 0 errors (`grep -rn "McpOAuthClient\b\|AllowedRedirectUris\|/mcp/oauth/register" src tests` returns nothing outside `Migrations/`).
- [ ] **Step 3: Generate the migration:** `dotnet ef migrations add RemoveMcpOAuthClient --project src/SluiceBase.Api`. Expected: the migration drops `mcp_oauth_client`, and nothing else changes.
- [ ] **Step 4: Regenerate `openapi.json`** (the build writes it) and `schema.ts` using the frontend's existing generation script (see `src/frontend/package.json`). Expected: the `/mcp/oauth/register` path disappears from both.
- [ ] **Step 5: `dotnet build`, unit tests, and integration tests** if the stack is healthy.
- [ ] **Step 6: Commit** `Remove MCP dynamic client registration`

### Task 10: Frontend connect modal

**Files:**
- Modify: `src/frontend/src/components/mcp/mcpClients.ts`, `src/frontend/src/components/mcp/__tests__/ConnectMcpModal.test.tsx`

- [ ] **Step 1: Verify CIMD support** for VS Code, GitHub Copilot (in VS Code) and MCP Inspector from their official docs and changelogs. Record the findings, with links, in the task report.
- [ ] **Step 2: Update the modal tests.**
  - Expect the tab set Claude Code, VS Code, GitHub Copilot, Codex, without Cursor, minus any client Step 1 found to be DCR-only.
  - Assert that no tab labelled "Cursor" renders.
- [ ] **Step 3: Run** `npm test -- ConnectMcpModal` in `src/frontend`. Expected: FAIL.
- [ ] **Step 4: Remove the Cursor entry** (and any DCR-only client) from `mcpClients.ts`.
- [ ] **Step 5: Run** `npm test -- ConnectMcpModal` and `npm run lint`. Expected: both pass.
- [ ] **Step 6: If MCP Inspector is DCR-only,** document in Task 11 that it should use the configured `sluicebase-dev` client. AppHost already ships it.
- [ ] **Step 7: Commit** `Drop MCP connect tabs for clients without CIMD`

### Task 11: README

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Config table.**
  - Remove the `Mcp__AllowedRedirectUris` row.
  - Add `Mcp__PublicBaseUrl` ("Public origin of this server, e.g. `https://sluice.example.com`. Strongly recommended behind a reverse proxy; becomes the OAuth issuer").
  - Add `Mcp__Clients__N__ClientId` / `__Name` / `__RedirectUris__M` ("Pre-registered MCP clients for tools without CIMD support").
- [ ] **Step 2: Rewrite "Sign-in and consent"** to cover:
  - clients identify via CIMD or operator config, and there's no self-registration,
  - consent is shown only for non-loopback callbacks of CIMD clients,
  - the supported tools: Claude Code/Desktop/claude.ai, ChatGPT, Codex, plus whatever Task 10 verified,
  - Cursor needs a configured client and Cursor itself doesn't yet support CIMD,
  - existing connections must reconnect once after upgrading,
  - CIMD needs direct outbound HTTPS; in proxy-only networks, only configured clients work.
- [ ] **Step 3: Fix "Testing the MCP server locally"** for MCP Inspector per Task 10's findings, and the integration-test filter line (remove `McpOAuthClientRemediationTests` if it's listed).
- [ ] **Step 4: Commit** `Document CIMD-based MCP sign-in`
