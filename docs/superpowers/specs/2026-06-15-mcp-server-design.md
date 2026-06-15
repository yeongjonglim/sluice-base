# MCP Server for SluiceBase — Design

**Date:** 2026-06-15
**Status:** Approved (design); implementation plan pending
**Branch:** `feat/mcp-server`

## Goal

Let AI coding tools (Claude Code, Codex) connect to SluiceBase as a remote MCP
server and, **acting as the authenticated user**, list databases, browse
schema, and run read-only queries — reusing SluiceBase's existing per-database
permissions, sensitive-column screening, read-only credentials, and audit
logging without any bypass.

## Scope

**In scope (v1):**

- Remote MCP server hosted in the existing `SluiceBase.Api` (single container),
  streamable-HTTP transport at `/mcp`.
- OAuth 2.1 authorization using the **minimal authorization-server façade**
  (Option B): SluiceBase is the OAuth authorization server to MCP clients and an
  OAuth client to the existing OIDC provider.
- Three MCP tools: `list_databases`, `get_schema`, `run_query` (read-only).
- Opaque access + refresh tokens stored hashed in Postgres.

**Out of scope (v2+):**

- Writes / approval workflow over MCP.
- Session-management UI (view/revoke active MCP connections).
- OAuth scopes narrower than "acts as the user".
- Resource-server-only topology (Option A — delegate AS directly to the IdP).
- Non-Postgres targets (added later behind the existing `ITargetEngine`
  abstraction).

## Key decisions & rationale

| Decision | Choice | Why |
|---|---|---|
| Transport | Streamable-HTTP, in-process in `SluiceBase.Api` at `/mcp` | Both Claude Code and Codex speak streamable HTTP for remote servers; keeps the single-container deployment model. |
| SDK | Official C# `ModelContextProtocol` / `ModelContextProtocol.AspNetCore` | First-party, integrates with ASP.NET auth/DI. |
| Auth model | OAuth 2.1 | Spec-compliant; expected by MCP clients. |
| AS topology | **Option B** — minimal AS façade brokering upstream to the OIDC provider | IdP-agnostic. Works identically on Entra (no DCR / admin app-registration friction) and Keycloak. Delivers the desired UX: user runs `/mcp` → authenticate → sees the **same login page they see today** → returns to the client. Reuses SluiceBase's existing confidential OIDC client; no new IdP config. |
| Token format | Opaque tokens + hashed DB lookup, with refresh tokens | Instant revocation of live access tokens (a real feature for a security gateway); easy to list/manage for the v2 session UI; no signing-key infra. Refresh tokens keep sessions alive without re-login. Per-request validation is one indexed DB lookup (cacheable later). |

### Why not Option A

SluiceBase does not own identity — it delegates to a customer OIDC provider.
Option A (publish protected-resource metadata pointing the client straight at
the IdP) only gives a clean one-click experience when the IdP supports Dynamic
Client Registration **and** audience-scoped access tokens. Keycloak does; **Entra
ID does not** (app registrations are admin-controlled, no open DCR), forcing
per-tenant admin setup and a static `client_id`. Since the estate is mostly
Entra + Keycloak, Option B is the only topology that gives the same smooth flow
everywhere. The big remote-MCP servers (Notion, Figma, Linear, Atlassian) run
this same AS+DCR pattern — the difference is they already owned an AS; SluiceBase
builds a minimal one.

## Architecture

All components live in `SluiceBase.Api` (single container). Only external
runtime deps remain Postgres + the OIDC provider.

| Component | Responsibility |
|---|---|
| **MCP endpoint** (`/mcp`) | Streamable-HTTP MCP server via `ModelContextProtocol.AspNetCore`. Requires the new bearer auth scheme. |
| **MCP OAuth AS** | Endpoints: `/.well-known/oauth-protected-resource`, `/.well-known/oauth-authorization-server`, `/register` (DCR), `/authorize`, `/token`. Brokers login upstream to the existing OIDC provider, reusing the confidential client SluiceBase already has. |
| **Bearer auth handler** | Validates the opaque access token against the token store, loads the session's user, and adds the **same `InternalUserIdClaim`** the cookie path sets, so `ICurrentUserAccessor` works unchanged. |
| **Token store** | New EF entities for OAuth clients, auth codes, and tokens (hashed). |
| **Shared access services** | `ICatalogService` / `ISchemaService` / `IQueryService` extracted from today's inline endpoint logic; consumed by both the HTTP endpoints and the MCP tools. |

### OAuth flow (Option B)

```
Claude Code ──GET /mcp───────────────▶ 401 + WWW-Authenticate (resource metadata URL)
Claude Code ──GET /.well-known/oauth-protected-resource ▶ points at SluiceBase AS
Claude Code ──GET /.well-known/oauth-authorization-server ▶ DCR + endpoints
Claude Code ──POST /register (DCR)───▶ client_id (stored)
Claude Code ──browser: GET /authorize▶ SluiceBase redirects ──▶ Entra/Keycloak login
                                          (same page users see today)
   upstream login ──▶ SluiceBase /signin-oidc ──▶ identity established (existing path)
SluiceBase ──redirect w/ its own code──▶ Claude Code localhost callback
Claude Code ──POST /token (code+PKCE)─▶ opaque access token + refresh token
Claude Code ──GET /mcp (Bearer)──────▶ validated ▶ user mapped ▶ tools available
```

When the access token expires, the client calls `/token` with
`grant_type=refresh_token` and gets a new access token with no user
interaction. Re-authentication is only needed if the refresh token is expired or
revoked.

### Data model (new EF entities, one migration on this branch)

- `McpOAuthClient` — DCR-registered clients: client_id, redirect URIs, name,
  created-at.
- `McpAuthCode` — short-lived authorization codes (hashed) with PKCE challenge,
  linked user, expiry.
- `McpToken` — access + refresh tokens (**hashed**), linked user + client, type,
  expiry, last-used-at. This is the revocation point and the surface for the v2
  session-management UI.

Access-token TTL ~1h; refresh long-lived until revoked.

## MCP tool surface (v1)

| Tool | Backed by | Behavior |
|---|---|---|
| `list_databases` | `ICatalogService` (today's `/api/catalog/server`) | Returns only databases the user has a role on (server admins see all active). Each item: id, display name, server, can-write flag. |
| `get_schema` | `ISchemaService` (today's `/api/schema/{id}`) | Requires `query:execute` on that DB. Returns the schema tree with the **same sensitive/restricted column annotations** the UI receives. |
| `run_query` | `IQueryService` (today's `/api/query`) | Requires `query:execute`. Runs the full pipeline: sensitive-column screening → read-credential connection → timeout → audit log. Read-only. |

Tool inputs use `DatabaseId`; results are returned as structured JSON content.
Errors (no permission, blocked sensitive columns, timeout, SQL error) surface as
MCP tool errors carrying the same messages the HTTP path returns.

## Shared-service refactor (enabler)

Today the query / schema / catalog logic is inline and duplicated across
`QueryEndpoints`, `SchemaEndpoints`, and `CatalogEndpoints`, each repeating:
`currentUser.GetAsync` → check `UserDatabaseRoles` for `query:execute` →
`connectionFactory.GetConnectionStringAsync(..., Read)` → `targetEngine`.

Extract this into three services so HTTP and MCP share one code path (honors the
"abstract behind interfaces" rule in CLAUDE.md and prevents logic drift):

- `ICatalogService.ListAccessibleAsync(user, ct)`
- `ISchemaService.GetAnnotatedSchemaAsync(user, databaseId, ct)`
- `IQueryService.ExecuteAsync(user, databaseId, sql, source, ct)`

The existing `/api/*` endpoints become thin wrappers over these services
(behavior-preserving, covered by existing tests). The MCP tools call the
identical methods. `IQueryService.ExecuteAsync` takes a `source` parameter so
MCP-originated queries are tagged in the audit log. NOTE: `QueryLog` has **no**
source field today (`SourceRequestId` belongs to the unrelated `update_request`
write-approval workflow). This adds a small new `QuerySource` enum (`Ui`/`Mcp`)
column to `QueryLog` — included in the same branch migration — making agent
queries distinguishable from UI queries in history.

## Identity, permission & audit reuse

- The bearer handler resolves the opaque token → `McpToken` → user, then adds
  the `InternalUserIdClaim` to `HttpContext.User`. `ICurrentUserAccessor` and
  every downstream permission check work unchanged.
- All per-database `query:execute` checks, sensitive-column blocking, read-only
  credential use, and query logging are reused verbatim via the shared services.
  The MCP path cannot bypass them.

## Non-functionals

- **Auth wiring:** add a second authentication scheme (opaque bearer) alongside
  the existing cookie scheme. `/mcp` and the bearer-protected resources require
  the bearer scheme; the SPA keeps cookies. MCP/OAuth endpoints are exempt from
  antiforgery (no cookies involved).
- **Config:** no new required env vars — the AS reuses `Oidc__*`. Optional:
  `Mcp__AccessTokenMinutes`, `Mcp__RefreshTokenDays`, `Mcp__Enabled` (feature
  flag, default on).
- **Migrations:** single new migration on this `feat/` branch (squash per
  convention); no data backfill.
- **Testing:** unit tests on the extracted services (behavior parity);
  integration tests for the OAuth dance and a bearer-authenticated `run_query`.
  Verify backend via `dotnet build SluiceBase.slnx` (warnings-as-errors).
  Remember CI gates regenerated `src/SluiceBase.Api/openapi.json` and
  `src/frontend/src/api/schema.ts` — commit those artifacts.
- **Docs:** README section on adding SluiceBase as a remote MCP server in Claude
  Code / Codex and the one-time `/mcp` → authenticate flow.

## Open questions

None blocking. Specific OAuth-endpoint shapes and the exact `ModelContextProtocol`
auth-integration API will be pinned during the implementation plan against the
installed SDK version.
