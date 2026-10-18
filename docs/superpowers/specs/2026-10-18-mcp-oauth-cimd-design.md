# MCP OAuth: CIMD-only client identity, issuer hardening, conditional consent

Date: 2026-10-18
Status: Approved
Issues: closes #209; addresses #213 items 1–3 (item 4, the SDK bump, is a separate PR)
Branch: `feat/mcp-oauth-consent-redirect-policy` (builds on commit `c42da60`)

## Why

SluiceBase embeds its own OAuth 2.1 authorization server for MCP clients
(`src/SluiceBase.Api/Mcp/Oauth/`). Today any client can self-register through
Dynamic Client Registration (DCR, `/mcp/oauth/register`) with any redirect URI, and
`/mcp/oauth/authorize` issues a code straight from a GET. Anyone could register
`https://evil.example/cb`, send a signed-in user a link, and collect a code and
then a token for that user's database access (confused deputy).

Commit `c42da60` on this branch fixed that within the DCR model: a redirect-URI
policy, a consent page, and a startup cleanup of non-conforming stored clients.
This design replaces that DCR-centric approach with the shape the MCP 2026-07-28
spec points to:

- **DCR is deprecated** in favour of Client ID Metadata Documents (CIMD). The
  owner has decided to **remove DCR now** instead of waiting for the ~12-month
  deprecation window. The app is not critical, so moving fast matters more than
  backward compatibility.
- **RFC 9207 `iss`** must be returned on authorization responses. ChatGPT and
  Codex use it to decide which client identity to present, and reject a mismatch.
- **The issuer must be stable and exact.** Deployments sit behind a reverse proxy
  such as nginx, and the base URL is currently derived from `X-Forwarded-*`
  (fragile and spoofable, see #209).

### Target clients (researched 2026-10-18)

| Client | CIMD | Notes |
|---|---|---|
| Claude Code / Desktop / claude.ai | Yes | Claude Code's document lists a **portless** loopback redirect; the real callback adds an ephemeral port |
| ChatGPT | Yes, preferred | Uses the stable id `https://chatgpt.com/oauth/client.json` + redirect `https://chatgpt.com/connector_platform_oauth_redirect` only if the server does `iss`; otherwise callback-specific ids/redirects |
| Codex | Yes | Picks CIMD only if metadata advertises it, `token_endpoint_auth_methods_supported` includes `none`, and the callback is loopback. Callback-specific doc registers `http://127.0.0.1/callback/<id>` without a port. Stable doc `https://chatgpt.com/oauth/codex/client.json` requires `iss` |
| Cursor | **No** (DCR only, no timeline as of 2026-09-22) | Owner accepts that Cursor stops working. It can still be served later via an operator-configured client |

## Goals

1. Clients identify themselves through **CIMD** (an `https://` `client_id`) or as an
   **operator-configured client** (any other `client_id`). Nothing else is accepted.
2. **Remove DCR entirely**: endpoint, metadata field, `McpOAuthClient` entity and table.
3. **`Mcp:PublicBaseUrl`** (#209) is the single source for `issuer`, all advertised
   endpoint URLs, the protected-resource `resource`/`authorization_servers`, and `iss`.
4. **`iss`** on every authorize redirect, success and error, plus
   `authorization_response_iss_parameter_supported: true` in metadata.
5. **Conditional consent.** Show the consent page only when the code goes to a
   remote `https://` callback of a CIMD client. Loopback callbacks and
   operator-configured clients skip it.
6. **SSRF-safe** CIMD fetching.

## Non-goals

- `ModelContextProtocol.AspNetCore` 1.4 → 2.1 bump (#213 item 4). Separate PR.
- Keeping DCR for backward compatibility, or migrating existing DCR clients/tokens.
- Remembered approvals, a "connected apps" list, or any revoke UI.
- A domain allowlist or trust policy for CIMD clients. Any CIMD client is allowed.
- Admin UI or DB storage for operator-configured clients. Config only.
- `private_key_jwt` or any confidential-client auth. Public clients with PKCE only,
  as today.
- Supporting Cursor.

## Anti-goals (would make this a failure even if it "works")

- Any path where a signed-in user's code reaches a **remote** callback without that
  user clicking Allow on the consent page.
- The server fetching an attacker-chosen URL that resolves to a private, loopback,
  link-local or metadata-service address (SSRF).
- `iss` differing from metadata `issuer` by even one character. Clients compare by
  exact string, including a trailing slash.

## Design

### 1. Public base URL (#209)

- New `McpOptions.PublicBaseUrl` (string, optional; env `Mcp__PublicBaseUrl`).
- Add a `GetValidatedPublicBaseUrl(ILogger?)` helper that mirrors the
  `GetValidatedServerName` pattern. It must be an absolute `http`/`https` **origin**:
  scheme, host and optional port, with no path, query or fragment. Strip a trailing
  `/` so the value is canonical. If it is invalid, log a warning and treat it as
  unset. *My call:* sub-path hosting (e.g. `https://corp.example/sluice`) is out of
  scope. It would need path-inserted `.well-known` routes (RFC 8414/9728) and prefix
  handling that nothing else in the app supports.
- One resolver (e.g. `McpIssuer.BaseUrl(HttpContext, McpOptions)`) returns the
  configured value, or falls back to today's `$"{Scheme}://{Host}"`. Every consumer
  uses it: `OAuthMetadataEndpoints` (both documents), the `iss` value, and the
  `WWW-Authenticate` `resource_metadata` URL in `McpBearerAuthenticationHandler`
  (builder: check whether it builds its own absolute URL, and if so switch it over).
- If it is unset at startup and MCP is enabled, log one warning recommending it is
  set behind a reverse proxy. The fallback keeps local dev and existing deployments
  working.

### 2. Issuer identification (RFC 9207, #213 item 1)

- Authorization server metadata adds `authorization_response_iss_parameter_supported: true`.
- Every redirect to the client carries `iss=<base URL>`: success (`code`, `state`,
  `iss`) and errors (`error=access_denied`, `state`, `iss`). Errors returned as a
  400 body (bad client or redirect) are not redirects and are unchanged. Per OAuth,
  we never redirect to an unverified URI.
- Remove `registration_endpoint`. Add `client_id_metadata_document_supported: true`.
  Keep `token_endpoint_auth_methods_supported: ["none"]`, which Codex requires.

### 3. Client resolution

Introduce one abstraction used by authorize (GET and POST) and token:

```
IMcpClientResolver.ResolveAsync(string clientId, CancellationToken) -> ResolvedClient?
ResolvedClient(string ClientId, string DisplayName, IReadOnlyList<string> RedirectUris,
               ClientKind Kind)   // Kind: Configured | Cimd
```

Resolution order:

1. **Operator-configured.** If `clientId` exactly matches an entry in
   `Mcp:Clients`, return it (`Kind = Configured`). This check runs first, so an
   operator can pin even an `https://` id.
2. **CIMD.** If `clientId` is an absolute `https://` URL, fetch and validate the
   document (section 4) and return `Kind = Cimd`, `DisplayName = client_name` or the host.
3. Otherwise return `null`, which callers turn into `invalid_client_or_redirect_uri`
   (authorize) or `invalid_client` (token).

**Configured clients** bind from config:

```
Mcp__Clients__0__ClientId=some-id
Mcp__Clients__0__Name=Some Tool
Mcp__Clients__0__RedirectUris__0=cursor://anysphere.cursor-mcp/oauth/callback
```

At startup, validate: `ClientId` non-empty and unique, at least one redirect URI,
each URI absolute. Log and skip any invalid entry rather than failing startup,
matching the existing "warn and fall back" style. The redirect-URI *policy*
(section 5) does **not** apply to configured clients. The operator vouched for them,
and that is how a web client or custom scheme can be pinned.

`Mcp__AllowedRedirectUris` (added earlier on this branch, never released) is
**removed**. Configured clients replace it.

### 4. CIMD fetching and validation

Per draft-ietf-oauth-client-id-metadata-document and the MCP client-registration spec.

**URL acceptance** (before any network I/O). The `client_id` must be:

- an absolute `https` URL with a non-empty path other than `/`,
- with no userinfo or fragment, and no `.`/`..` path segments,
- with default port only, or an explicit port. *Builder's call;* keep it simple and
  allow explicit ports.

**SSRF protection** (mandatory):

- Use a dedicated named `HttpClient` whose `SocketsHttpHandler.ConnectCallback`
  resolves DNS itself and **refuses to connect** to any address that is not
  publicly routable: loopback, private (RFC 1918, `fc00::/7`), link-local
  (`169.254.0.0/16`, `fe80::/10`, which includes cloud metadata `169.254.169.254`),
  CGNAT `100.64.0.0/10`, unspecified, multicast, broadcast, and IPv4-mapped IPv6
  forms of these. Checking at connect time (not just before the request) defeats
  DNS rebinding.
- No redirects (`AllowAutoRedirect = false`); a 3xx is a failure.
- Timeout of about 5 s, a maximum response size of about 64 KiB (read with a cap and
  abort beyond it), `Accept: application/json`, and only a 200 with a JSON content
  type is accepted.
- No proxy (`UseProxy = false`), so the connect check sees the real target.
  Deployments are assumed to have direct outbound internet access. Supporting an
  egress proxy is out of scope. The README notes that in proxy-only environments
  CIMD fetches fail, so only configured clients work there.
- Abstract the fetch behind an interface (e.g. `ICimdDocumentFetcher`), so the
  integration tests can substitute canned documents without internet access and the
  SSRF logic stays unit-testable.

**Document validation:**

- Valid JSON object whose `client_id` **exactly equals** the requested URL (ordinal).
- `redirect_uris` is a non-empty array of strings. Drop any entry that fails the
  redirect-URI policy (section 5). If none survive, the client is invalid.
- `token_endpoint_auth_method` absent or `none`; or
  `token_endpoint_auth_methods_supported` contains `none`. Otherwise invalid, since we
  only support public clients.
- `client_name` is optional. If absent, display the `client_id` host.
- Reject documents that contain `client_secret` or `client_secret_expires_at`
  (spec: CIMD clients must not have shared secrets).

**Caching:**

- Use `IMemoryCache` keyed by the `client_id` URL. Honour `Cache-Control: max-age`,
  clamped to a range of about 5 min to 24 h; with no header, default to about 1 h.
- Also cache negative results briefly (about 1 min), so a bad id can't make us
  hammer a host.
- **Separate "couldn't fetch" from "fetched but invalid"** (*my call*). The fetcher
  returns one of three outcomes: valid document, invalid document, or temporary
  failure (timeout, connection error, 5xx). On a temporary failure, **serve the last
  good document** if one is held (stale-on-error; keep the last good document for
  up to about 7 days past its TTL). That way a client host's outage during refresh
  doesn't force users to reconnect. With no last-good copy, authorize returns a 400
  `temporarily_unavailable` and token returns a 503 `temporarily_unavailable`,
  never `invalid_client`.
- The token endpoint resolves the client too, so a cache hit there is the normal case.

**Limiting anonymous fetches** (*my call*). Both endpoints are anonymous, so without
care anyone could make SluiceBase fetch arbitrary public URLs:

- **GET authorize:** check the request's shape (`response_type`, PKCE) first, then
  require sign-in, and only then resolve the client and fetch. An unknown or invalid
  client is therefore reported after login rather than before. Update the negative
  tests to match.
- **Token:** look up the code or refresh token by hash first. Resolve the client
  (and possibly fetch) only if a live record exists for that `client_id`, so an
  anonymous caller without a valid code or refresh token can never trigger a fetch.
- **Global cap:** at most about 4 concurrent outbound CIMD fetches. Excess requests
  get the temporary-failure outcome rather than queueing.

**Integration-test hook** (*my call*). The integration tests run the API as a
separate Aspire process, so a DI fake isn't possible. Instead:

- Add a config flag `Mcp:Cimd:AllowLoopbackForTesting`. When true, the connect
  check also permits loopback addresses, and https certificate validation for
  loopback hosts accepts the dev certificate.
- At startup, **throw** if the flag is true and the environment isn't
  `Development`. That makes it impossible to ship a production deployment with
  SSRF protection weakened.
- The AppHost (always Development) sets the flag and runs a tiny stub resource that
  serves canned CIMD documents over https on loopback for the tests.
- The dev AppHost also sets `Mcp__PublicBaseUrl=https://localhost:5443` (the
  gateway, which is correct for local dev anyway) and one configured client used by
  the integration tests.
- If the stub resource proves impractical, the builder may instead move the CIMD
  integration tests to an in-process `WebApplicationFactory` host with a DI fake.
  Either way, production code must not contain a fetch bypass usable outside
  Development.

### 5. Redirect-URI policy (kept from c42da60, narrowed)

`McpRedirectUriPolicy` now governs **CIMD clients only**:

- **Allowed in a CIMD document:** `http` loopback (`localhost`, `127.0.0.1`, `[::1]`;
  the existing parser-confusion rejections stay), any `https` URL, and private-use
  schemes such as `cursor://`. *My call:* an `https` callback is allowed because the
  consent page protects it, and a private-use scheme is delivered locally by the OS.
  Treat a private-use scheme like `https` (consent required), since we can't verify
  which app owns a scheme.
- **Rejected:** non-loopback `http`, userinfo, fragments, backslashes, control or
  whitespace characters.
- **Matching** (`Matches`) is unchanged: exact match, or loopback-to-loopback ignoring
  port. This is what makes Claude Code's and Codex's **portless** registered URIs
  match `http://127.0.0.1:<port>/…`. The registered URI may have no port and the
  requested one has an ephemeral port; the path and query must match exactly.
- The hard-coded `NativeAppCallbacks` set (Cursor) is removed. Cursor is now just a
  potential configured client.

### 6. Authorize flow

GET `/mcp/oauth/authorize`:

1. Validate `response_type=code`, `code_challenge_method=S256`, `code_challenge` present.
2. Resolve the client (section 3). Check that `redirect_uri` matches one of its URIs
   (section 5). On failure, return a 400 body as today (no redirect).
3. If not signed in, redirect to the OIDC challenge and back (unchanged).
4. **Consent decision:**
   - `Kind = Configured` → no consent.
   - `Kind = Cimd` and the redirect is loopback → no consent.
   - otherwise (a CIMD client whose redirect is `https` or a private-use scheme) →
     render the consent page.
5. No consent → issue the code and redirect immediately (with `iss`).

POST `/mcp/oauth/authorize` (consent submit) stays as on this branch: re-validate,
check antiforgery, then deny → `access_denied` (+`iss`), or allow → code (+`iss`).
The POST must re-derive the consent requirement and refuse to act for a request
that didn't need consent. *Builder's call;* simplest is to accept it either way,
since approving is always safe.

**Consent page changes** (`McpConsentPage`):

- Headline client identity is the **verified CIMD host** (e.g. `chatgpt.com`), with
  `client_name` secondary. Drop the "the tool chose this name itself; SluiceBase
  can't verify it" line. Keep a softer note that the name comes from the tool,
  while the domain is verified.
- Keep the destination box, the capabilities list, the "only allow if you just
  started this" warning, CSP and the anti-framing headers.
- The loopback branch of `Describe` becomes unreachable for the page, but keep it
  harmless.

### 7. Token endpoint

- Resolve `client_id` (section 3). If unresolvable, return `401 {"error":"invalid_client"}`.
- `McpTokenService` is unchanged. It already binds codes and refresh tokens to the
  `client_id` string and checks `redirect_uri` equality on redeem.
- A refresh for an old DCR client id (a GUID-like string that is neither configured
  nor `https`) now fails with `invalid_client`, so the user reconnects once.
  Intended; no migration code.

### 8. Removal of DCR

- Delete `OAuthRegistrationEndpoints` and its mapping, `McpOAuthClientRemediation`
  and its `Program.cs` call, `McpOAuthClient`, `McpOAuthClientId`,
  `McpOAuthClientConfiguration`, and `AppDbContext.McpOAuthClients`.
- Add an EF migration that drops the `mcp_oauth_client` table. Generate it with
  `dotnet ef migrations add`; never hand-edit it. Existing `mcp_token` and
  `mcp_auth_code` rows stay, and old ones simply stop resolving (see section 7).
  Already-issued access tokens stay valid until expiry (≤60 min). Accepted.
- Regenerate `openapi.json` and `src/frontend/src/api/schema.ts`, since the register
  endpoint goes away.

### 9. Frontend

- `src/frontend/src/components/mcp/mcpClients.ts`: remove the **Cursor** tab. *My
  call:* it would advertise a client that can no longer connect. Keep Claude Code,
  VS Code, GitHub Copilot and Codex. Update `ConnectMcpModal.test.tsx` to match.
- **Verify that VS Code and GitHub Copilot support CIMD** (VS Code is reported to).
  *My call:* any listed client found to be DCR-only is treated like Cursor and its
  tab is removed. The same goes for MCP Inspector in the README. If Inspector is
  DCR-only, the dev AppHost ships a configured client for it, and the local-testing
  section explains how to pass that client id.
- Adding claude.ai or ChatGPT tabs is not required. *Builder's call.*
- Run `npm run lint` and the vitest suite.

### 10. Docs

README:

- Config table: add `Mcp__PublicBaseUrl` (strongly recommended behind a proxy) and
  `Mcp__Clients__N__*`; remove `Mcp__AllowedRedirectUris`.
- Rewrite "Sign-in and consent":
  - clients identify via CIMD or operator config, with no self-registration,
  - when the consent page shows,
  - which tools work (Claude, ChatGPT, Codex, VS Code/Copilot, MCP Inspector if it
    does CIMD; the builder verifies MCP Inspector),
  - that Cursor needs a configured client and currently doesn't work out of the box,
  - that existing connections must be reconnected once after upgrading.
- Fix the "Testing the MCP server locally" section if MCP Inspector's flow changes.

## Testing

- **Unit** (`tests/SluiceBase.Api.Tests`):
  - redirect policy, narrowed (https and private-scheme now allowed in CIMD,
    non-loopback http rejected, portless-loopback matching),
  - CIMD document validation (id mismatch, missing or invalid `redirect_uris`,
    secret present, auth method not `none`),
  - SSRF address classification, covering IPv4/IPv6 private, link-local, metadata,
    mapped forms and public,
  - public base URL validation and trailing-slash canonicalisation,
  - consent page (verified host shown).
- **Integration** (`tests/IntegrationTests`) with a fake `ICimdDocumentFetcher`:
  - CIMD loopback happy path with no consent and `iss` present,
  - CIMD https happy path with consent, then Allow → code + `iss`,
  - Deny → `access_denied` + `iss`,
  - configured-client happy path with no consent,
  - unknown or non-https id rejected,
  - `client_id` mismatch rejected,
  - unregistered redirect rejected,
  - refresh works,
  - metadata advertises CIMD and `iss`, has no `registration_endpoint`, and the
    `issuer` equals `PublicBaseUrl` when set,
  - `/mcp/oauth/register` returns 404.
- Replace `McpOAuthClientRemediationTests` and update `OAuthFlowTests`,
  `OAuthMetadataTests`, `McpToolsTests` and `Supports/McpConsentForm.cs`. They
  currently register via DCR.
- Integration tests need the Aspire stack (Keycloak can OOM locally). At minimum,
  build and run unit tests locally; CI runs the rest.

## Left to the builder

- Type and file names for the resolver, fetcher, options and issuer helper.
- Exact cache durations and size and timeout limits, within the ranges above.
- Whether to allow explicit ports in CIMD URLs.
- Consent page wording, within the intent in section 6.
- Whether the consent POST rejects requests that didn't need consent.
