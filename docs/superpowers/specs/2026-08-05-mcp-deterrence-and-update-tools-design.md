# MCP: sensitive-column deterrence + update-request tools

Date: 2026-08-05
Status: Approved

## Problem

Two gaps in the current MCP surface (`src/SluiceBase.Api/Mcp/Tools/DatabaseTools.cs`,
tools `list_databases` / `get_schema` / `run_query`):

1. When `run_query` hits a sensitive-column restriction, the client (an AI agent)
   receives only a bare exception string. Nothing tells the agent to stop probing,
   so a cooperative model may waste turns brute-forcing variations. We want to
   *deter* that behavior — best-effort — and steer the agent to exclude the columns
   and move on.
2. There is no way for an AI agent to open a write (update) request. The full
   update workflow exists as REST endpoints (`/api/update`) and UI, but the agent
   cannot participate. We want the agent to be able to **submit** an update request
   (and read its status), but **never** approve, reject, cancel, or execute.

## Non-goals / explicitly out of scope

- **This is not a security control.** MCP tool text is advisory; a client can ignore
  it. The real boundary is `SensitiveColumnGuard`, which already hard-rejects every
  blocked query server-side and logs it as `QueryLogStatus.Blocked` with
  `QuerySource.Mcp`. That enforcement and audit are unchanged by this work.
- **No guard/tokenizer hardening.** Making `SqlColumnChecker` view-aware or
  fail-closed on unanalyzable SQL is a separate, security-sensitive effort with its
  own spec and tests. Not in this PR.
- **No approve/reject/cancel/execute over MCP.** Those stay REST/UI-only.
- **No frontend changes.** The agent-directed guidance is deliberately *not* added to
  the REST `ProblemDetails` (that consumer is a human at a UI with its own callout).
  Submitted requests surface in the existing update UI with no new work.
- **No gateway changes.** All tools live under the already-allowlisted `/mcp` route;
  update REST routes already exist.

## Improvement 1 — sensitive-column deterrence (best-effort messaging)

Two message touch-points, both inside `Mcp/`. Guard, audit, tokenizer untouched.

### 1a. `run_query` blocked outcome returns a structured, instructive result

Today `QueryOutcome.Blocked` throws
`"Query touches sensitive columns: a.b.c"`. Replace with a **returned object**
(not a throw). Rationale: the MCP SDK serializes a returned object into the tool
result's text content, which is exactly what the client model reads; its `IsError`
shaping is unreliable for object-returning tools (documented in `McpToolsTests`),
so controlling the *content* is what matters.

Shape:

```json
{
  "error": "sensitive_columns_blocked",
  "blockedColumns": ["hr.employees.ssn", "hr.employees.salary"],
  "guidance": "These columns are restricted by policy and cannot be returned. Re-run the query without them. Do not attempt to work around this restriction (SELECT *, column aliases, casts, row-serialization functions, or resubmitting the same request) — every variation is blocked identically and each attempt is recorded. You cannot override this; only an operator can grant a column bypass."
}
```

- `blockedColumns` carries **identities only, never values** — needed so a cooperative
  model can drop exactly those columns.
- The payload construction is extracted into a small pure helper
  (`SensitiveColumnBlockPayload.From(blockedColumns)`) so it is unit-testable without DI.

### 1b. Proactive policy, stated before the first block

- Extend `run_query`'s `[Description]` with a one-line rule: sensitive columns are
  enforced server-side; on a block, exclude the named columns rather than retrying.
- Set `ServerInstructions` on `AddMcpServer(o => o.ServerInstructions = …)` in
  `Program.cs` so every client session sees the house rule up front.
  (`McpServerOptions.ServerInstructions` confirmed present in ModelContextProtocol.Core 1.3.0.)

## Improvement 2 — update-request tools (submit + status read-back)

### Shared service extraction

Extract submit/list/get logic out of `UpdateEndpoints` into a new
`IUpdateRequestService` (in `src/SluiceBase.Api/Services/`), mirroring the existing
`IQueryService` outcome-record pattern, so the REST endpoints and the new MCP tools
call one implementation and cannot drift. Mutation paths (approve/reject/cancel/
execute/preview) are **not** moved — MCP never touches them.

Service surface:

- `SubmitAsync(user, databaseId, sql, reason, ct)` →
  `SubmitOutcome { Ok(detail), NotFound, Forbidden, BadRequest(msg) }`
  (validates: db exists, `update:submit`, not disabled, `CanWrite`; creates a
  `Pending` `UpdateRequest` via `UpdateRequest.Create`).
- `ListAsync(user, filters…, ct)` → summaries for databases where the user holds any
  update permission (submit/approve/execute), same filter semantics as today.
- `GetAsync(user, id, ct)` → `GetOutcome { Ok(detail), NotFound }`, masking as
  NotFound when the user lacks any update permission on the request's database.

`UpdateEndpoints.Submit/List/Get` become thin adapters mapping outcomes to
`TypedResults`. Their existing request/response record types are reused.

### New MCP tool-type `Mcp/Tools/UpdateTools.cs`

A separate `[McpServerToolType]` (keeps read/write tools in focused files),
registered with a second `.WithTools<UpdateTools>()`.

- `submit_update_request(databaseId, sql, reason)` — gated by `update:submit`.
  Returns `{ id, status: "Pending", message: "Submitted for human review — you cannot approve or execute it." }`.
- `list_update_requests(databaseId?, status?)` — status summaries.
- `get_update_request(id)` — full detail incl. event history.

Tool descriptions state plainly that the agent cannot approve/reject/cancel/execute —
those require a human via the app. Failure outcomes throw `InvalidOperationException`/
`ArgumentException` with clear messages, matching the existing `DatabaseTools` style.

## Testing

- **Unit (SluiceBase.Api.Tests, runs locally):** `SensitiveColumnBlockPayload.From`
  produces the expected `error`/`blockedColumns`/`guidance` shape, including the
  column identities and no values.
- **Integration (IntegrationTests, CI-gated — the Aspire stack is unavailable in
  automated local sessions):** extend `McpToolsTests` to (a) call `run_query`
  against a query touching a seeded sensitive column and assert the returned content
  contains `sensitive_columns_blocked` + the blocked column + guidance; (b) list the
  tools and assert the three update tools are present and approve/execute are absent;
  (c) `submit_update_request` creates a `Pending` request visible via
  `get_update_request` and via the REST list, and the agent has no approve/execute
  tool. Local verification is `dotnet build` + the unit test; CI runs integration.

## File impact

- `src/SluiceBase.Api/Mcp/Tools/DatabaseTools.cs` — blocked-result change + description.
- `src/SluiceBase.Api/Mcp/SensitiveColumnBlockPayload.cs` — new pure helper.
- `src/SluiceBase.Api/Mcp/Tools/UpdateTools.cs` — new tool-type.
- `src/SluiceBase.Api/Services/IUpdateRequestService.cs` — new service.
- `src/SluiceBase.Api/Endpoints/UpdateEndpoints.cs` — Submit/List/Get become adapters.
- `src/SluiceBase.Api/Program.cs` — register service, `WithTools<UpdateTools>`, `ServerInstructions`.
- `tests/SluiceBase.Api.Tests/…` — payload unit test.
- `tests/IntegrationTests/McpToolsTests.cs` — new integration coverage.
