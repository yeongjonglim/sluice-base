# SluiceBase Schema DDL Export — Design

**Date:** 2026-06-14
**Status:** Proposed
**Predecessors:** Schema Browser (`2026-05-07-schema-browser-design.md`), ERD Diagram (`2026-06-13-erd-diagram-design.md`), CSV Export (`2026-05-10-csv-export-design.md`)

## 1. Purpose & scope

Users with `query:execute` on a database can already browse its schema (tree on `/query`, diagram on `/query/diagram`). This feature lets them **export that database's schema as `pg_dump`-equivalent DDL** (a `.sql` file) so they can diff the live schema against the migrations they generate from their codebase (e.g. EF Core, Flyway).

The exported DDL must match what an operator would get from pgAdmin's schema export — pgAdmin invokes `pg_dump` under the hood, so the only faithful approach is to invoke the **real `pg_dump --schema-only`** against the target database. A hand-rolled DDL emitter built from our introspected `SchemaTree` would never be byte-identical and would miss sequences, ownership, comments, and exact constraint/index syntax, so it is explicitly rejected.

### Data-protection invariant (non-negotiable)

`pg_dump` connects with the raw read credential and bypasses **every** SluiceBase protection layer — sensitive-column masking, query authorization, and any future row-based security. Therefore this feature must make it **structurally impossible to emit table data**: `--schema-only` is hard-coded server-side, is never a user-toggleable option, and the engine/endpoint accept no input that could cause data to be dumped (`--data-only`, `-a`, `--inserts`, `--column-inserts`, etc. are not representable). Because no row ever leaves, sensitive-column and row-based protections are out of scope for this feature *by construction* — the guarantee does not depend on any UI being correct.

### In scope

- New method `ExportSchemaDdlAsync(connectionString, ct)` on `ITargetEngine` (Core), returning the DDL as a `string`.
- `PostgresTargetEngine` (Api) implementation that shells out to `pg_dump` with a **fixed** flag set: `--schema-only --no-owner --no-privileges` (chosen for clean migration diffs).
- New endpoint `GET /api/schema/{databaseId}/ddl` returning the DDL as a downloadable file, reusing the existing `GetSchema` authorization flow (per-database `query:execute` role, **read** credential).
- An **"Export DDL"** button on the `/query/diagram` page header, next to the database selector.
- `postgres-client` added to the Dockerfile runtime image so `pg_dump` is on `PATH`, with its major version matched to the provisioned Postgres server.
- Integration tests (real Postgres via Aspire, run in CI) and a frontend Vitest test.

### Out of scope (deferred)

- JSON / other export formats — DDL only.
- Hand-rolled DDL generation from `SchemaTree`.
- A dedicated `schema:export` permission — v1 reuses `query:execute` (see §6).
- Streaming very large dumps — the DDL is buffered as a `string`/byte array in v1 (see §7 risks).
- Non-Postgres engines — no other `ITargetEngine` implementation exists in v1; each future engine implements its own dump.
- A pgAdmin-style **options modal** — v1 ships one button with the fixed flag set above. A later iteration may add an engine-neutral `SchemaExportOptions` record (e.g. `IncludeOwners`, `IncludePrivileges`, `Clean`, schema/table scoping) mapped to flags via a per-engine **allowlist** (never free-text flags), surfaced as a modal. Any such options must remain inside the data-protection invariant — no option can ever cause data to be emitted (see §1).
- Additional object filtering — `pg_dump --schema-only` already emits views, functions, triggers, and sequences; the user explicitly wants these "additional definitions" included now rather than as a later add-on.

### Success criteria

With Aspire running:

1. Alice (has `query:execute` on Blue) opens `/query/diagram`, selects "Blue", and sees an enabled **"Export DDL"** button in the header.
2. Clicking it downloads a `.sql` file containing `CREATE TABLE` (and view/function/sequence) statements for Blue's schema, equivalent to `pg_dump --schema-only`.
3. With no database selected, the button is disabled.
4. A user without `query:execute` on Blue receives `403` from the endpoint.
5. An unknown database id returns `404`.

## 2. Backend — engine abstraction

Add to `ITargetEngine` (`src/SluiceBase.Core/Targets/ITargetEngine.cs`):

```csharp
Task<string> ExportSchemaDdlAsync(string connectionString, CancellationToken ct);
```

`PostgresTargetEngine` (`src/SluiceBase.Api/Targets/PostgresTargetEngine.cs`) implements it:

1. Parse the read connection string with `NpgsqlConnectionStringBuilder` to extract `Host`, `Port`, `Database`, `Username`, `Password`.
2. Start `pg_dump` via `System.Diagnostics.Process` with an **argument list** (`ProcessStartInfo.ArgumentList`), never a composed shell string:
   - `--schema-only --no-owner --no-privileges` (a fixed, hard-coded set in v1 — not derived from any caller input)
   - `-h <host> -p <port> -U <username> -d <database>`
   - `RedirectStandardOutput = true`, `RedirectStandardError = true`, `UseShellExecute = false`.

   `--schema-only` is hard-coded here and is the enforcement point for the data-protection invariant (§1): the method exposes no parameter that could omit it or request data, so no `PostgresTargetEngine` caller — present or future — can cause table data to be dumped.
3. Pass the password through `ProcessStartInfo.Environment["PGPASSWORD"]` — never as a command-line argument and never logged.
4. Read stdout and stderr **asynchronously** (to avoid the pipe-buffer deadlock), and register the `CancellationToken` to kill the process on cancellation.
5. On exit code `0`, return captured stdout. On non-zero exit, throw `InvalidOperationException(stderr)`.

This keeps the `pg_dump` invocation (a Postgres-specific operation) behind the `ITargetEngine` interface, consistent with the project rule that database-specific operations live behind abstractions, with the implementation in the Api layer alongside the existing Npgsql code.

The `pg_dump` executable is resolved from `PATH`. (A future enhancement could make the path configurable; out of scope for v1.)

## 3. Backend — endpoint

Add `GET /api/schema/{databaseId}/ddl` to `src/SluiceBase.Api/Endpoints/SchemaEndpoint.cs`, mapped alongside the existing `GetSchema`. It mirrors `GetSchema`'s authorization exactly:

- Resolve the database → `404` if not found.
- Require a `UserDatabaseRoles` row for the current user with `Permissions.QueryExecute` on this database → `403` otherwise.
- Obtain the **read** connection string via `IServerConnectionFactory.GetConnectionStringAsync(databaseId, CredentialKind.Read, ct)`.
- Call `targetEngine.ExportSchemaDdlAsync(...)`.
- Return `TypedResults.File(bytes, "application/sql", fileName)` where `bytes = Encoding.UTF8.GetBytes(ddl)` and `fileName = "{database.DisplayName}-schema-{yyyyMMdd-HHmmss}.sql"` (display name sanitized for filesystem-safe characters).
- `InvalidOperationException` from the engine → `TypedResults.BadRequest(ex.Message)`, matching `GetSchema`.

Result type: `Results<FileContentHttpResult, NotFound, BadRequest<string>, ForbidHttpResult>`.

Sensitive-column annotation does **not** apply here: DDL describes structure, not data values, and column names are already visible in the schema tree. This is noted as an accepted decision in §6.

## 4. Frontend — Diagram page header

In `src/frontend/src/routes/_authed/query/diagram.tsx`, add an **"Export DDL"** button to the existing header `Box` (which currently holds only `DatabaseSelect`):

- `Button` with `IconDownload`, label "Export DDL", placed in a `Group` with the selector.
- Disabled when `!selectedDatabaseId`.
- Shows a loading state while the export request is in flight.
- On click, runs a TanStack Query **mutation** (`useExportSchemaDdl`) that:
  1. `fetch`es `GET /api/schema/{databaseId}/ddl` through the existing API client (cookie auth, base URL), requesting the response as a `Blob`.
  2. On success, saves the blob using the same Blob/anchor download mechanism as `exportToCsv` (a small shared helper, e.g. `downloadBlob(blob, filename)` factored out of `utils/csv.ts` or added alongside it), with filename `{databaseLabel}-schema-{timestamp}.sql` derived from the selected database's display name.
  3. On error, surfaces a message to the user (Mantine notification if available, else an inline `Alert` consistent with the page's existing schema-error `Alert`).

The API client/`hooks.ts` gains an `exportSchemaDdl(databaseId)` function returning the blob, plus the `useExportSchemaDdl` mutation hook. Because the response is a binary file rather than JSON, the frontend calls the endpoint directly rather than through the generated OpenAPI types; the generated `paths` entry for the new endpoint is otherwise unused.

## 5. Deployment

- Add `postgres-client` to the runtime stage of the **Dockerfile** so `pg_dump` is available on `PATH`.
- **Always install the latest available client; do not pin or version-match.** `pg_dump` is forward-compatible: a given client dumps its own version and **all older** servers, but not a server newer than itself. Installing the newest client therefore maximizes the range of target databases supported with zero version-coordination overhead. On the Alpine runtime image, "latest" is the newest major the base image's repo provides (the `postgres-client` meta package); in CI, the latest from PGDG. They need not agree — both are forward-compatible with the (older) servers being dumped.
- **No server pinning required.** Earlier drafts proposed pinning the Aspire target Postgres images and matching the client major to them; that is unnecessary given forward-compatibility and has been dropped.
- **Newer-server caveat:** if an operator registers a target database **newer** than the image's `pg_dump`, the dump fails cleanly — `pg_dump` exits non-zero and the endpoint returns `400` with the message. The remedy is to keep the image's client current (rebuild on a newer base), not to change code.
- **Local dev:** running the feature locally requires `pg_dump` on the developer's `PATH` (e.g. via Homebrew `libpq`/`postgresql`). This is a documented dev prerequisite, not a code dependency.

## 6. Authorization decision

v1 gates the export behind the **same** `query:execute` per-database role as viewing the schema. Rationale: such users can already browse the schema tree and run `SELECT`s. The accepted nuance is that `pg_dump --schema-only` additionally emits view/function/trigger bodies and sequence definitions — more than the tree exposes. The user has confirmed they want those additional definitions included. If stricter control is later desired, a dedicated `schema:export` permission can be introduced without changing the engine or endpoint shape.

## 7. Risks & mitigations

- **`pg_dump` not installed.** Mitigated by the Dockerfile install (§5); a missing binary surfaces as an error, and a documented dev prerequisite covers local runs.
- **Large schemas / slow dumps.** v1 buffers the whole dump in memory and has no explicit timeout beyond request cancellation. Acceptable for the expected schema sizes; streaming and timeouts are deferred.
- **Credential handling.** The password is passed only via `PGPASSWORD` in the child process environment and is never logged or placed on the command line, eliminating the shell-injection and process-listing exposure.
- **Target server newer than the client.** Because the client is the latest available and not pinned, this is rare; when it happens the dump fails cleanly with a `400` (§5). Remedy: rebuild the image on a base with a newer `pg_dump`.

## 8. Testing

- **Integration tests** (`tests/IntegrationTests`, real Postgres via Aspire — pass in CI per the local-OIDC caveat):
  - Authorized user exports the seeded DB; assert the body contains expected `CREATE TABLE` statements (and at least one non-table object, e.g. a sequence/view, to prove `--schema-only` fidelity).
  - **Data-protection invariant:** assert the output contains **no** data statements (no `COPY ... FROM stdin`, no `INSERT INTO`) even for a seeded table that has rows — proving structure-only output.
  - User without `query:execute` on the target DB → `403`.
  - Unknown database id → `404`.
- **Frontend (Vitest):** the "Export DDL" button is disabled with no DB selected; clicking with a DB selected calls the export function and triggers a download; the error path surfaces a message (API client mocked, download helper spied).
