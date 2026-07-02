# Connect AI Tools (MCP) — In-App Instructions — Design

## Problem

SluiceBase runs a remote MCP server (`/mcp`) so AI coding agents connect as the
authenticated user, reusing per-database permissions, sensitive-column screening,
and audit logging. Today the only instructions for adding it live in the README
(`## Connecting AI tools (MCP)`). Users must leave the app, find the docs, and
hand-edit `your-domain` in the snippet. There is no in-app surface.

## Goal

An in-app, copy-ready way to connect an AI coding agent to this SluiceBase
instance, framed around what makes SluiceBase distinctive: the agent connects
**as you** — scoped, revocable, and audited.

## Scope

**In scope**
- A modal ("Connect AI tools") reachable from an unobtrusive header trigger.
- Client-specific instructions for: Claude Code, Cursor, VS Code, GitHub Copilot, Codex.
- Copy-ready snippets with the live endpoint (`origin + /mcp`) baked in.
- One-click install deeplinks for the clients that support them (Cursor, VS Code).
- Operator-configurable MCP server name (`Mcp__ServerName`, default `sluicebase`).
- Gating: the trigger and modal appear only when the MCP server is enabled.

**Out of scope** (unchanged from the MCP server design)
- Live session management (view/revoke active MCP connections).
- Any change to the MCP protocol, tools, or OAuth flow.
- New UI dependencies (`@mantine/code-highlight` is **not** added).

## Non-goals / YAGNI

- No dedicated route/page — a modal keeps it lightweight and discoverable enough
  via a persistent header control.
- No per-user personal tokens or copyable secrets — auth is the existing OAuth
  browser flow; snippets contain only a public URL and a server alias.

## User-facing behaviour

1. When MCP is enabled, a subtle ghost `ActionIcon` (`IconSparkles`, tooltip
   "Connect AI tools") sits in the app header next to the theme toggle.
2. Clicking it opens a modal titled "Connect AI tools".
3. The modal opens with a one-line purpose statement and a **trust strip** — the
   signature element — communicating three facts: runs as you, uses your
   permissions and sensitive-column screening, every query audited.
4. A `Tabs` control selects the client. Each tab shows a two-step flow:
   - **Step 1 — Add the server:** a `Code` block containing the client-specific
     snippet with the real endpoint and configured server name, plus a
     `CopyButton`. Cursor and VS Code additionally show a one-click install
     button/badge.
   - **Step 2 — Authenticate:** short copy explaining first use opens the browser
     to the usual login (no extra credentials).
5. Below the tabs: the three tools (`list_databases`, `get_schema`, `run_query`)
   as compact text, and a link to the README/docs.
6. When MCP is disabled, neither the trigger nor the modal renders.

## Architecture

### Frontend

New files under `src/frontend/src`:

- **`components/mcp/mcpClients.ts`** — the data seam. Exports
  `Array<McpClient>` where

  ```ts
  interface McpClient {
    id: string;                 // "claude-code" | "cursor" | ...
    label: string;              // "Claude Code"
    icon: React.FC;            // tabler icon
    snippetLang: string;        // "bash" | "json" | "toml" — display hint only
    buildSnippet(ctx: McpConnectionContext): string;
    buildDeeplink?(ctx: McpConnectionContext): string; // Cursor, VS Code
    authNote: string;
  }

  interface McpConnectionContext {
    endpoint: string;   // window.location.origin + "/mcp"
    serverName: string; // from branding injection, default "sluicebase"
  }
  ```

  Keeping snippet/deeplink construction as pure functions of
  `McpConnectionContext` makes the modal presentational and the logic unit
  testable in isolation. Adding a client later is a single new object.

- **`components/mcp/ConnectMcpModal.tsx`** — presentational modal
  (`<Modal opened onClose title>`, matching `access.tsx` convention). Computes
  the `McpConnectionContext` once from `window.location.origin` and branding,
  renders the trust strip, `Tabs` over `mcpClients`, per-tab `Code` +
  `CopyButton` (+ deeplink button when present), and the tools/docs footer.

- **`components/mcp/ConnectMcpTrigger.tsx`** — the header `ActionIcon` + tooltip
  that owns the modal open/close state via `useState` and renders both the
  trigger and `ConnectMcpModal`. Renders `null` when MCP is disabled.

Wiring:

- `src/routes/_authed.tsx` — mount `<ConnectMcpTrigger />` in the header
  `Group`, before the color-scheme toggle.
- `src/theme/BrandingContext.tsx` + `src/main.tsx` — extend `BrandingValue` and
  the `window.__BRANDING__` type with `mcpEnabled: boolean` and
  `mcpServerName: string`; default `mcpEnabled` to `false` and `mcpServerName`
  to `"sluicebase"` when absent. Gating on a **default-false** flag means a
  stale/served-without-injection page never shows instructions for a disabled
  server.

### Snippets (endpoint `E = origin + "/mcp"`, name `N = serverName`)

| Client | Snippet | Deeplink |
|---|---|---|
| Claude Code | `claude mcp add --transport http {N} {E}` | — |
| Cursor | JSON: `{ "{N}": { "url": "{E}" } }` | `cursor://anysphere.cursor-deeplink/mcp/install?name={N}&config={base64(JSON.stringify({url:E}))}` |
| VS Code | `code --add-mcp '{"name":"{N}","type":"http","url":"{E}"}'` | `vscode:mcp/install?{urlEncoded({name:N,type:"http",url:E})}` |
| GitHub Copilot | `.vscode/mcp.json`: `{ "servers": { "{N}": { "type": "http", "url": "{E}" } } }` | — |
| Codex | TOML: `[mcp_servers.{N}]` / `transport = "http"` / `url = "{E}"` | — |

Exact VS Code/Copilot deeplink/config shape is verified against current client
docs during implementation; the `mcpClients.ts` seam localizes any adjustment.

### Backend

- **`src/SluiceBase.Api/Mcp/McpOptions.cs`** — add
  `public string ServerName { get; set; } = "sluicebase";` plus a
  `GetValidatedServerName(ILogger)` method that returns `ServerName` when it
  matches `^[A-Za-z0-9_-]+$`, otherwise logs a warning and returns
  `"sluicebase"`. Validation prevents an operator value with spaces/dots from
  producing an invalid TOML table name (`[mcp_servers.My Server]`) or JSON key.
  Mirrors the existing `GetValidatedPrimaryColor` pattern on `BrandingOptions`.

- **`src/SluiceBase.Api/Middleware/BrandingHtmlMiddleware.cs`** — inject
  `IOptions<McpOptions>`; include `mcpEnabled = mcp.Enabled` and
  `mcpServerName = mcp.GetValidatedServerName(logger)` in the serialized
  `window.__BRANDING__` object. (Middleware and `McpOptions` share the
  `SluiceBase.Api` assembly, so the `internal` type is accessible.)

- **`README.md`** — document `Mcp__ServerName` in the config table (default
  `sluicebase`) and note the in-app "Connect AI tools" entry point.

No new env vars are *required*; `Mcp__ServerName` is optional with a default.
Config reads stay at the existing options/injection sites — no database-specific
or Npgsql coupling is introduced.

## Aesthetic direction

The modal lives inside the existing Mantine app and follows its language (teal
accent, existing spacing, `Modal` convention) rather than introducing a new
visual identity that would clash with surrounding UI. Boldness is spent in one
place — the **signature**:

> A "connect as you" trust strip at the top of the modal: three quiet
> affordances (runs as you · your permissions & sensitive-column screening ·
> every query audited). It reframes MCP setup from a neutral dev chore into a
> security-gateway handshake, which is what SluiceBase is.

Everything else stays disciplined default. Copy is active-voice and user-facing
("Add the server", "Authenticate"), matching action names to outcomes.

## Error handling & edge cases

- **MCP disabled / no injection:** `mcpEnabled` defaults to `false`; trigger and
  modal render nothing.
- **Invalid `Mcp__ServerName`:** backend falls back to `sluicebase` and warns;
  the frontend always receives a safe identifier.
- **Copy unsupported:** rely on Mantine `CopyButton`'s built-in handling;
  snippets remain selectable text.
- **Deeplink client not installed:** the OS simply does nothing on the custom
  scheme; the copy-config path beside it remains available as the fallback.

## Testing

Vitest component/unit tests matching existing frontend patterns:

- `mcpClients` builders: each snippet contains the endpoint and the server name;
  Cursor/VS Code deeplinks are well-formed (Cursor config is valid base64 that
  decodes to the expected JSON).
- `ConnectMcpModal`: renders a tab per client; switching tabs swaps the snippet;
  copy button present; tools listed.
- `ConnectMcpTrigger`: renders the control when `mcpEnabled` is true; renders
  nothing when false.

Backend: `dotnet build SluiceBase.slnx` (warnings-as-errors). `McpOptions`
validation covered by a small unit test (valid name passes; name with spaces
falls back to `sluicebase`). CI gates regenerated `openapi.json` /
`schema.ts` — no API surface changes here, so those artifacts should be
unaffected, but confirm no diff.

## Open questions

None blocking. VS Code/Copilot deeplink and config shapes are confirmed against
current client docs at implementation time; the `mcpClients.ts` seam isolates
any change.
