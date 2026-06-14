# SluiceBase Schema DDL Export — Design

**Date:** 2026-06-14
**Status:** Proposed
**Predecessors:** Schema Browser (`2026-05-07-schema-browser-design.md`), ERD Diagram (`2026-06-13-erd-diagram-design.md`), CSV Export (`2026-05-10-csv-export-design.md`)

## 1. Purpose & scope

Users with `query:execute` on a database can already browse its schema (tree on `/query`, diagram on `/query/diagram`). This feature lets them **export that database's schema as `pg_dump`-equivalent DDL** (a `.sql` file) so they can diff the live schema against the migrations they generate from their codebase (e.g. EF Core, Flyway).

The exported DDL must match what an operator would get from pgAdmin's schema export — pgAdmin invokes `pg_dump` under the hood, so the only faithful approach is to invoke the **real `pg_dump --schema-only`** against the target database. A hand-rolled DDL emitter built from our introspected `SchemaTree` would never be byte-identical and would miss sequences, ownership, comments, and exact constraint/index syntax, so it is explicitly rejected.

### In scope

- New method `ExportSchemaDdlAsync(connectionString, ct)` on `ITargetEngine` (Core), returning the DDL as a `string`.
- `PostgresTargetEngine` (Api) implementation that shells out to `pg_dump --schema-only`.
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
- Configurable `pg_dump` flags in the UI (e.g. `--no-owner`, `--no-privileges`) — v1 uses plain `--schema-only` to match pgAdmin defaults.
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
   - `--schema-only`
   - `-h <host> -p <port> -U <username> -d <database>`
   - `RedirectStandardOutput = true`, `RedirectStandardError = true`, `UseShellExecute = false`.
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
- **Version constraint:** `pg_dump` refuses to dump from a server whose major version is newer than the client. The installed `postgres-client` major version **must be ≥** the Postgres major version the Aspire AppHost provisions. The implementation plan will read the provisioned version from the AppHost and pin the client package to match (or a known-compatible newer major).
- **Local dev:** running the feature locally requires `pg_dump` on the developer's `PATH` (e.g. via Homebrew `libpq`/`postgresql`). This is a documented dev prerequisite, not a code dependency.

## 6. Authorization decision

v1 gates the export behind the **same** `query:execute` per-database role as viewing the schema. Rationale: such users can already browse the schema tree and run `SELECT`s. The accepted nuance is that `pg_dump --schema-only` additionally emits view/function/trigger bodies and sequence definitions — more than the tree exposes. The user has confirmed they want those additional definitions included. If stricter control is later desired, a dedicated `schema:export` permission can be introduced without changing the engine or endpoint shape.

## 7. Risks & mitigations

- **`pg_dump` not installed / wrong version.** Mitigated by the Dockerfile install and version-matching (§5); a missing binary surfaces as a `400` with the process error, and a documented dev prerequisite covers local runs.
- **Large schemas / slow dumps.** v1 buffers the whole dump in memory and has no explicit timeout beyond request cancellation. Acceptable for the expected schema sizes; streaming and timeouts are deferred.
- **Credential handling.** The password is passed only via `PGPASSWORD` in the child process environment and is never logged or placed on the command line, eliminating the shell-injection and process-listing exposure.
- **Server version drift over time.** If the managed Postgres major version is later bumped, the `postgres-client` pin must be bumped in lockstep; called out in §5 and to be noted near the Dockerfile install.

## 8. Testing

- **Integration tests** (`tests/IntegrationTests`, real Postgres via Aspire — pass in CI per the local-OIDC caveat):
  - Authorized user exports the seeded DB; assert the body contains expected `CREATE TABLE` statements (and at least one non-table object, e.g. a sequence/view, to prove `--schema-only` fidelity).
  - User without `query:execute` on the target DB → `403`.
  - Unknown database id → `404`.
- **Frontend (Vitest):** the "Export DDL" button is disabled with no DB selected; clicking with a DB selected calls the export function and triggers a download; the error path surfaces a message (API client mocked, download helper spied).
