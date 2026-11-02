# SluiceBase

![Frontend Coverage](https://raw.githubusercontent.com/yeongjonglim/sluice-base/gh-pages/badges/frontend-coverage.svg)

SluiceBase is a self-hosted database query gateway. It gives a team controlled, auditable access to databases — secured by OIDC authentication, per-user permissions, and an optional approval workflow for write operations.

## Features

- **OIDC authentication** — integrates with any OIDC provider (Keycloak, Auth0, Entra ID, etc.)
- **Permission model** — assign read/write permissions per user and database
- **Write approval workflow** — write requests require approval before execution
- **Query history** — all queries are logged with results
- **Schema browser** — explore table schemas directly in the UI
- **Customisable branding** — app name, colour, logo, and favicon configurable at runtime

## Architecture

A single Docker container runs the .NET 10 API which also serves the React frontend. The only external dependencies at runtime are a PostgreSQL database (metadata store) and an OIDC provider.

```
Browser → SluiceBase (React SPA + .NET 10 API) → PostgreSQL (metadata)
                                                 → Target databases
                                                 → OIDC provider
```

## Quick start

### Prerequisites

- Docker
- PostgreSQL 16+ (metadata store)
- An OIDC provider with a confidential client configured (see [OIDC setup](#oidc-setup))

### Docker Compose

```yaml
services:
  postgres:
    image: postgres:17-alpine
    environment:
      POSTGRES_USER: sluicebase
      POSTGRES_PASSWORD: changeme
      POSTGRES_DB: sluicebase
    volumes:
      - postgres-data:/var/lib/postgresql/data

  app:
    image: ghcr.io/yeongjonglim/sluice-base:latest
    ports:
      - "8080:8080"
    environment:
      ConnectionStrings__Metadata: "Host=postgres;Database=sluicebase;Username=sluicebase;Password=changeme"
      Oidc__Authority: "https://your-keycloak/realms/your-realm"
      Oidc__ClientId: "sluicebase"
      Oidc__ClientSecret: "your-client-secret"
      Permissions__Bootstrap__Admins__0: "admin@your-company.com"
    depends_on:
      - postgres

volumes:
  postgres-data:
```

### Run the container directly

```bash
docker run -p 8080:8080 \
  -e ConnectionStrings__Metadata="Host=...;Database=sluicebase;Username=...;Password=..." \
  -e Oidc__Authority="https://your-keycloak/realms/your-realm" \
  -e Oidc__ClientId="sluicebase" \
  -e Oidc__ClientSecret="your-client-secret" \
  ghcr.io/yeongjonglim/sluice-base:latest
```

The app is available at `http://localhost:8080`.

### Behind a reverse proxy

The container listens on HTTP only, so deploy it behind a reverse proxy (nginx, Caddy, Traefik, a Kubernetes ingress) that terminates TLS. Three things make the app see its public `https://` address:

1. **`ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`** on the container. ASP.NET Core then honours `X-Forwarded-Proto` and `X-Forwarded-For` from any proxy address. Without it every request looks like plain `http`, so the OIDC redirect URI becomes `http://…/signin-oidc` and sign-in fails. Since any sender is trusted, only the proxy should be able to reach port 8080.
2. **The proxy forwards the original `Host` header and sets `X-Forwarded-Proto`.** `X-Forwarded-Host` is not honoured, so the public hostname must arrive in `Host`. ingress-nginx does both by default. Plain nginx needs:
   ```nginx
   proxy_set_header Host $host;
   proxy_set_header X-Forwarded-Proto $scheme;
   proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
   ```
3. **`PublicBaseUrl=https://your-domain`** pins the app's absolute URLs (the MCP OAuth issuer, links MCP tools return), so they don't depend on request headers at all (see [Public URL](#sign-in-and-consent)). Without it each request's own scheme and `Host` are used, so a user reaching the app at an internal hostname gets URLs with that hostname.

## Environment variables

| Variable | Required | Default | Description |
|---|---|---|---|
| `ConnectionStrings__Metadata` | ✓ | — | PostgreSQL connection string for the SluiceBase metadata database |
| `Oidc__Authority` | ✓ | — | OIDC authority URL (e.g. `https://keycloak/realms/myrealm`) |
| `Oidc__ClientId` | ✓ | — | OIDC client ID |
| `Oidc__ClientSecret` | ✓ | — | OIDC client secret |
| `ASPNETCORE_FORWARDEDHEADERS_ENABLED` | ✓ behind a proxy | `false` | Trust `X-Forwarded-Proto`/`X-Forwarded-For` from the TLS-terminating proxy — see [Behind a reverse proxy](#behind-a-reverse-proxy) |
| `PublicBaseUrl` | | _(derived from request)_ | Public origin of this server, e.g. `https://sluice.example.com` (scheme, host, optional port; no path; trailing slash removed, host lowercased). Strongly recommended behind a reverse proxy; used for absolute URLs such as the MCP OAuth issuer, the `iss` value and links MCP tools return. If unset it is built from the request's scheme and `Host` header (correct only when the [proxy setup](#behind-a-reverse-proxy) is right) and a warning is logged at startup |
| `Migrations__AutoApply` | | `true` | Apply pending DB migrations on startup |
| `Permissions__Bootstrap__Admins__0` | | — | Email of the first admin (granted full permissions on first login) |
| `Branding__AppName` | | `SluiceBase` | Application name shown in the UI |
| `Branding__PrimaryColor` | | `teal` | Mantine colour — see [supported values](#branding-colours) |
| `Branding__LogoUrl` | | — | URL to a custom logo image — see [using local files](#using-local-branding-files) |
| `Branding__FaviconUrl` | | — | URL to a custom favicon — see [using local files](#using-local-branding-files) |
| `Query__TimeoutSeconds` | | `30` | Maximum query execution time in seconds |
| `Mcp__Enabled` | | `true` | Enable the MCP server and its OAuth endpoints (see [Connecting AI tools](#connecting-ai-tools-mcp)) |
| `Mcp__ServerName` | | `sluicebase` | Client alias shown in connect snippets (letters, digits, `-`, `_`; falls back to `sluicebase` if invalid) |
| `Mcp__AccessTokenMinutes` | | `60` | Lifetime of an MCP access token in minutes |
| `Mcp__RefreshTokenDays` | | `30` | Lifetime of an MCP refresh token in days |
| `Mcp__AuthCodeSeconds` | | `120` | Lifetime of an MCP OAuth authorization code in seconds |
| `Mcp__Clients__N__ClientId`, `__Name`, `__RedirectUris__M` | | _(empty)_ | Pre-registered public MCP clients (PKCE, no secret) for tools without CIMD support. Their redirect URIs are trusted as configured and skip the consent screen (see [Sign-in and consent](#sign-in-and-consent)) |

### Branding colours

`Branding__PrimaryColor` accepts any Mantine built-in colour name:

`dark` `gray` `red` `pink` `grape` `violet` `indigo` `blue` `cyan` `teal` `green` `lime` `yellow` `orange`

### Using local branding files

Mount a directory into `/app/wwwroot/branding/` inside the container and set `LogoUrl`/`FaviconUrl` to the corresponding relative paths. The files are served natively as static assets.

```yaml
# docker-compose.yml
services:
  app:
    image: ghcr.io/yeongjonglim/sluice-base:latest
    volumes:
      - ./branding:/app/wwwroot/branding:ro
    environment:
      - Branding__LogoUrl=/branding/logo.png
      - Branding__FaviconUrl=/branding/favicon.ico
```

Place `logo.png` and `favicon.ico` in a local `./branding/` directory. Both `LogoUrl` and `FaviconUrl` also accept remote `http(s)://` URLs if the assets are hosted externally — in that case no volume mount is needed.

## Database migrations

Migrations are applied automatically on startup by default. Set `Migrations__AutoApply=false` to disable this behaviour.

To apply migrations manually using the .NET CLI:

```bash
dotnet ef database update \
  --project src/SluiceBase.Api \
  --connection "Host=...;Database=sluicebase;Username=...;Password=..."
```

## OIDC setup

Register a **confidential** OIDC client with these settings:

| Setting | Value |
|---|---|
| Client type | Confidential (authorization code + PKCE) |
| Redirect URI | `https://your-domain/signin-oidc` |
| Post-logout redirect URI | `https://your-domain/signout-callback-oidc` |
| Scopes | `openid`, `profile`, `email` |

The ID token must include the `sub`, `email`, and `name` claims.

**Keycloak:** create a realm, add a client with the settings above, and point `Oidc__Authority` to `https://your-keycloak/realms/your-realm`.

## Connecting AI tools (MCP)

SluiceBase exposes a [Model Context Protocol](https://modelcontextprotocol.io) server so AI coding tools (Claude Code, Codex) can list databases, browse schema, and run read-only queries **as the authenticated user** — reusing the same per-database permissions, sensitive-column screening, and audit logging as the web UI.

The MCP endpoint is served at `https://your-domain/mcp` (streamable HTTP). Authentication uses OAuth 2.1: SluiceBase acts as the authorization server and brokers login to your existing OIDC provider, so users sign in with the **same login page they already use** — no extra credentials, and tokens are user-scoped and revocable.

Signed-in users can also open **Connect AI tools** (the ✨ icon in the app header) for copy-ready, per-client setup snippets pre-filled with this instance's URL and server name.

### Claude Code

```bash
claude mcp add --transport http sluicebase https://your-domain/mcp
```

Then in Claude Code, run `/mcp`, select **sluicebase → Authenticate**. A browser opens to your OIDC provider's login page; after signing in you're returned to the client and the tools become available.

### Codex

Add a remote MCP server pointing at the same URL in your Codex MCP configuration:

```toml
[mcp_servers.sluicebase]
transport = "http"
url = "https://your-domain/mcp"
```

Codex runs the same OAuth flow on first use.

### Tools

| Tool | Description |
|---|---|
| `list_databases` | Lists the databases the user can query, grouped by server |
| `get_schema` | Returns a database's table/column schema (sensitive columns flagged) |
| `run_query` | Executes a read-only SQL query (subject to the user's `query:execute` permission, sensitive-column blocking, and audit logging — MCP-origin queries are tagged in the history) |

Set `Mcp__Enabled=false` to disable the MCP server and its OAuth endpoints entirely.

### Sign-in and consent

There is no client self-registration. An MCP client identifies itself in one of two ways:

- **CIMD (Client ID Metadata Documents).** The `client_id` is an `https://` URL pointing at the tool's own metadata document. SluiceBase fetches it, validates it, and caches it per its `Cache-Control` (5 minutes to 24 hours). This needs direct outbound HTTPS from SluiceBase (no proxy support); private, loopback and link-local targets are refused. In proxy-only networks only configured clients work. NAT64-only egress (IPv6 hosts reaching IPv4 through `64:ff9b::/96`) isn't supported either: those addresses are refused as non-public.
- **Configured clients.** For tools without CIMD, an operator pre-registers a public client (PKCE, no secret) with `Mcp__Clients__N__ClientId`, `__Name` and `__RedirectUris__M`. Its redirect URIs are trusted as configured.

**Consent.** After signing in, the user sees a consent screen, with the verified domain of the tool, only when a CIMD client's callback is not loopback (for example claude.ai or ChatGPT on the web). Loopback callbacks (Claude Code, Codex, VS Code, MCP Inspector) and configured clients go straight through. If a user didn't start the connection themselves, they choose **Deny**.

**Public URL.** Set `PublicBaseUrl` to the origin users reach SluiceBase at. The trailing slash is removed and the host lowercased. It becomes the OAuth issuer and the `iss` parameter returned on authorization responses.

| Tool | Support |
|---|---|
| Claude Code, Claude Desktop, claude.ai | CIMD |
| ChatGPT | CIMD |
| Codex | CIMD |
| VS Code, GitHub Copilot in VS Code | CIMD (VS Code 1.106+; Copilot inferred from VS Code) |
| MCP Inspector | CIMD, using a metadata URL entered under **Client Settings → Client ID Metadata Document** |
| Cursor | Not supported out of the box (no CIMD yet); add a configured client |

**Cursor.** Add a configured client with redirect URI `cursor://anysphere.cursor-mcp/oauth/callback`, then have users set that client id in Cursor's `mcp.json`.

**Upgrading.** Self-registration was removed, so existing MCP connections must sign in again once.

## Development setup

SluiceBase uses [.NET Aspire](https://learn.microsoft.com/en-us/dotnet/aspire/) to orchestrate all services locally, including Keycloak and PostgreSQL.

**Prerequisites:** .NET 10 SDK, Node.js 24, Docker

```bash
git clone https://github.com/yeongjonglim/sluice-base.git
cd sluice-base

dotnet run --project src/AppHost
```

The Aspire dashboard opens at `https://localhost:15888`. Once all services are healthy, use the **Seed Server Registry** command on the Metadata database resource to populate sample target databases.

The local stack runs behind a YARP gateway at `https://localhost:5443` (Keycloak login, the SPA, and the API are all reached through it). Dev users are seeded in Keycloak: `alice@example.com` / `dev` (bootstrap admin) and `bob@example.com` / `dev`.

### Testing the MCP server locally

The MCP endpoint is served through the gateway at **`https://localhost:5443/mcp`**.

1. Trust the dev HTTPS certificate so an MCP client accepts the `localhost` TLS connection:

   ```bash
   dotnet dev-certs https --trust
   ```

2. Start the stack (`dotnet run --project src/AppHost`) and run the **Seed Server Registry** command from the dashboard.

3. Sign in to `https://localhost:5443` once as `alice@example.com` / `dev` to bootstrap the admin user. Alice has `server:manage`, so `list_databases` works immediately. To exercise the permission path with a non-admin, sign in as `bob@example.com` and grant him `query:execute` on a database via the **Access** admin page.

4. Connect a client:

   **MCP Inspector** (quickest way to drive the OAuth flow and call tools):

   ```bash
   NODE_TLS_REJECT_UNAUTHORIZED=0 npx @modelcontextprotocol/inspector
   ```

   Set Transport to **Streamable HTTP** and URL to `https://localhost:5443/mcp`. The web Inspector has no metadata document of its own, so under **Client Settings → Client ID Metadata Document** paste a public one that lists the Inspector's callback (`http://localhost:6274/oauth/callback`), such as MCPJam's `https://www.mcpjam.com/.well-known/oauth/client-metadata.json`, enable **Use Client ID Metadata Document**, and connect. The web callback follows the address bar, so open the Inspector at `http://localhost:6274`, not `127.0.0.1`. (`NODE_TLS_REJECT_UNAUTHORIZED=0` is only needed to accept the local dev certificate.)

   To use the dev configured client instead, run the Inspector's CLI or TUI, which can pin the callback: `--client-id sluicebase-dev --callback-url http://127.0.0.1:43123/callback`.

   **Claude Code:**

   ```bash
   claude mcp add --transport http sluicebase https://localhost:5443/mcp
   ```

   Then run `/mcp`, select **sluicebase → Authenticate**, and sign in via the Keycloak page.

5. Smoke-test the OAuth discovery without a client (no auth required):

   ```bash
   curl -k https://localhost:5443/.well-known/oauth-protected-resource
   curl -k -i https://localhost:5443/mcp   # expect 401 + WWW-Authenticate: Bearer resource_metadata=...
   ```

The OAuth + tool round-trip is also covered by the integration tests:

```bash
dotnet test tests/IntegrationTests --filter "McpTokenServiceTests|OAuthFlowTests|OAuthMetadataTests|McpToolsTests"
```

## License

[MIT](LICENSE)
