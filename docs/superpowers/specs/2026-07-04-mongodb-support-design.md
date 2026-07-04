# MongoDB support — design

**Date:** 2026-07-04
**Status:** Approved design, pending implementation plan
**Scope:** Add MongoDB as a second query target alongside PostgreSQL, read-only for v1.

## Goal

Let operators register MongoDB servers in SluiceBase and let users browse schema
and run read queries through the same controlled gateway that governs PostgreSQL
today — same permissions, same sensitive-data enforcement, same query logging, and
the same MCP tools. Writes and the approval workflow are explicitly out of scope
for v1.

## Guiding principle

Generalize the existing engine seam rather than special-case Mongo at call sites.
Today `ITargetEngine` is registered as a lone singleton and everything downstream
assumes "SQL string + relational schema." We turn the engine into a
**kind-resolved registry**, make the query payload **opaque to the pipeline**, and
push language-specific parsing (sensitive-field detection, read/write
classification) **into each engine**. PostgreSQL behavior must stay byte-for-byte
identical after the refactor.

A third engine (Redis/Valkey) is considered likely. The design is validated against
that: the engine seam must be reusable, and the schema model must not be contorted
so badly for a schemaless store that a third engine forces a painful retrofit. See
[Reusability for a third engine](#reusability-for-a-third-engine).

## Decisions (locked)

| Decision | Choice | Rationale |
|---|---|---|
| Query interface | Compass-style assisted UI | Familiar to Mongo users; suggestions from sampled schema |
| Query representation | Structured find + aggregation pipeline (JSON) in v1; mongosh-style text deferred | Clean to parse/enforce/log; text mode added later with no contract change |
| v1 scope | Read-only | Smallest safe slice; de-risks the engine abstraction before Mongo write semantics |
| Schema model | Adapt existing `SchemaTree` with dotted field paths | Max reuse of browser + sensitive-column model; kept additive for future engines |
| Connection topology | Standard + SRV modes, plus `authSource` / `replicaSet` / TLS | SRV covers Atlas/managed clusters via a single DNS name; keeps the host+port model |
| MCP | Single dialect-aware `run_query` tool; surface `kind` in `list_databases`; in v1 | Plumbing is free; leaving tools SQL-only makes the MCP path actively broken for Mongo |
| Sensitive-field enforcement | Engine-provided `FindReferencedPaths` | One enforcement flow; each engine parses its own language |

## Architecture changes (backend)

### Engine registry (replaces the single singleton)

- Introduce `ITargetEngineRegistry` with `ITargetEngine Resolve(string kind)`, backed
  by all registered `ITargetEngine` implementations keyed on their existing `Kind`
  property.
- `Program.cs` registers both `PostgresTargetEngine` (`"postgres"`) and a new
  `MongoTargetEngine` (`"mongodb"`), plus the registry.
- `QueryService` and `SchemaService` stop taking `ITargetEngine` directly; they
  resolve the engine from `Server.Kind` (reachable via the `Database` → `Server`
  relationship they already load) through the registry.

### Connection abstraction

- `ServerConnectionFactory` stops hard-coding `NpgsqlConnectionStringBuilder`.
  Connection-string building becomes a per-kind responsibility: either a method on
  `ITargetEngine` (e.g. `BuildConnectionString(server, database, credential)`) or a
  small per-kind builder the factory dispatches to by `Server.Kind`. The credential
  decryption / `IDataProtector` flow is unchanged.
- The Postgres builder is the current `NpgsqlConnectionStringBuilder` logic, moved
  behind the seam with no behavior change.
- The Mongo builder emits a `mongodb://` (Standard) or `mongodb+srv://` (SRV) URI
  from: connection mode, host (+ port for Standard), `authSource`, `replicaSet`,
  TLS toggle, username, and decrypted password.

### `Server` model growth

Add nullable, Mongo-only fields to `Server` (defaults chosen so existing Postgres
rows are untouched):

- `ConnectionMode` — enum `Standard` | `Srv`, default `Standard`.
- `AuthSource` — nullable string.
- `ReplicaSet` — nullable string.
- `UseTls` — bool, default `false`.

A new EF Core migration is generated on this feature branch (regenerated per the
project's "squash EF migrations on unmerged branches" convention; migration files are
never hand-edited — analyzer warnings are suppressed via `.editorconfig`).

### Sensitive-field enforcement

Add to `ITargetEngine`:

```csharp
IReadOnlyList<ReferencedPath> FindReferencedPaths(string query);
```

where `ReferencedPath` carries `(Schema, Table, Column)` — for Mongo, that is
`(database, collection, fieldPath)` with `fieldPath` in dotted notation.

- `PostgresTargetEngine.FindReferencedPaths` wraps the existing `SqlColumnChecker` /
  `SqlTokenizer` — no change to Postgres detection behavior.
- `MongoTargetEngine.FindReferencedPaths` walks the parsed query document (filter,
  projection, sort keys, and aggregation-pipeline stage field references) collecting
  referenced field paths.
- `QueryService`'s sensitive-column logic calls the resolved engine's
  `FindReferencedPaths` instead of calling `SqlColumnChecker` directly. This yields
  one enforcement flow with engine-specific parsing behind it.

## Query representation & data flow

- The query payload stays a **string** end-to-end. This matches `QueryLog` storage,
  the existing endpoint contract, and lets us add a mongosh-style text mode later with
  zero contract change.
- For Mongo v1 the string is a **JSON query document**:

  ```json
  { "collection": "users", "operation": "find",
    "filter": { "active": true }, "projection": { "name": 1 },
    "sort": { "createdAt": -1 }, "limit": 100 }
  ```

  or an aggregation:

  ```json
  { "collection": "orders", "operation": "aggregate",
    "pipeline": [ { "$match": { "status": "paid" } }, { "$group": { "_id": "$region", "total": { "$sum": "$amount" } } } ] }
  ```

- The Compass-style builder produces this JSON. `MongoTargetEngine.ExecuteQueryAsync`
  parses it, executes via the MongoDB .NET driver, and **flattens** result documents
  into the existing `QueryData(string[] Columns, string?[][] Rows)` shape using dotted
  paths for nested fields. The union of field paths across returned documents forms the
  columns; missing fields render as null cells.
- `ExecuteUpdateAsync` on `MongoTargetEngine` throws `NotSupportedException` in v1
  (writes deferred). The `/update` endpoint already routes on the write credential;
  Mongo servers will not expose write UI, and the engine guards the path defensively.

## Schema introspection (Mongo)

`MongoTargetEngine.GetSchemaAsync` maps Mongo concepts onto the adapted `SchemaTree`:

- **database → `SchemaInfo`**
- **collection → `TableInfo`**
- **sampled fields → `ColumnInfo`**, with nested fields flattened to dotted paths
  (`address.city`) and the inferred BSON type as `DataType`. Polymorphic fields (more
  than one observed type across the sample) render as `mixed` (or a `type1 | type2`
  union string).
- collection `listIndexes` → `IndexInfo`.
- `PrimaryKey`, `ForeignKey`, `Views`, `MaterializedViews`, `Routines`, `Sequences`,
  `Types`, and `Extensions` are left empty.

Sampling size is configurable (default ~1000 documents, mirroring Compass).

`ExportSchemaDdlAsync` returns a synthesized text summary of collections and inferred
fields (DDL export is inherently relational); it must not throw.

**Additive-shape requirement:** the adaptation must read as valid when most of the
relational structure is empty. Consumers (schema browser, MCP `get_schema`, sensitive
targeting) must not assume PK/FK/views exist. This keeps the door open for a
schemaless third engine without a retrofit.

## MCP

The MCP tools in `Mcp/Tools/DatabaseTools.cs` are thin wrappers over the shared
services, so execution, enforcement, and `QuerySource.Mcp` logging work for Mongo
with no plumbing changes. The **LLM-facing contract** must be generalized:

- Keep a single `run_query` tool. Rename the `sql` parameter to `query` and rewrite
  its description to be dialect-aware: for `postgres` pass SQL; for `mongodb` pass the
  query-document JSON (shape spelled out in the description).
- Surface each database's `kind` in `list_databases` output so the model learns the
  dialect before querying (it already calls `list_databases` first).
- Generalize the `get_schema` description ("table/column schema" → engine-neutral
  wording) and the `Blocked` error formatting (relational wording → path-based).

## Frontend

- The query page and schema browser already read `Server.Kind`. Branch on it:
  - `postgres` → existing `SqlEditor`.
  - `mongodb` → new **Mongo query builder**: collection picker, find-filter bar +
    aggregation-pipeline editor, with **field autocomplete sourced from the sampled
    schema tree**. Results render through the existing results table (cells are already
    strings).
- The "Add server" form grows a `kind` selector, and when Mongo is selected, the
  connection-mode toggle (Standard/SRV) plus `authSource` / `replicaSet` / TLS fields.
- Sensitive-field management UI reuses the existing schema-tree targeting, now over
  field paths.

## Testing & Aspire wiring

- Add a **MongoDB container resource** in `AppHost` (alongside the existing
  Postgres/Keycloak seed setup) with a seeded sample database for integration tests.
  Per the project's integration-test constraints, verify via build and rely on CI; CI
  gates `openapi.json` / `schema.ts`. New top-level API routes (if any) must be added
  to the YARP gateway allowlist in AppHost.
- Unit tests:
  - Mongo schema inference — nested documents, arrays, polymorphic fields.
  - Query-JSON parsing — find and aggregation shapes, invalid payloads.
  - `FindReferencedPaths` — field-path detection in filter / projection / pipeline.
  - Connection-string building — Standard vs SRV, with `authSource` / `replicaSet` /
    TLS combinations.
  - Engine registry resolution by kind.
- Frontend tests for the Mongo builder and autocomplete mirror the existing
  `SqlEditor` / `SchemaSidebar` suites.

## Reusability for a third engine

Assessed against Redis/Valkey (single instance or clustered):

**Reuses cleanly (the bulk of the effort):** engine registry + kind resolution;
connection abstraction and the "connection mode + mode-specific options" pattern on
`Server` (Redis: single / Cluster / Sentinel); opaque string query payload; the
`FindReferencedPaths` enforcement seam; permission checks; query logging; MCP
plumbing; Aspire test wiring.

**Does not reuse (inherently per-engine):**
- The `SchemaTree` shape is the weakest joint. It is already bent for Mongo; a
  schemaless key-value store has no natural schema. The additive-shape requirement
  above mitigates this, and "generalize the schema model" is an anticipated later step
  — deliberately *not* foreclosed, but not built in v1.
- The frontend query/schema UX is new per engine (Redis wants a key browser + command
  console). The branch-by-`kind` pattern reuses; the components do not.

Net: the engine seam is the reusable majority; the schema model and frontend editor are
the per-engine remainder. This is an acceptable ratio and confirms the abstraction is
real rather than theater.

## The journey (phased roadmap)

1. **Engine seam** — registry + kind resolution + connection abstraction; Postgres
   refactored onto it with zero behavior change. Pure refactor, fully testable alone.
2. **`Server` model + connection** — Mongo fields, migration, connection-string
   builder, `TestConnection`, add-server form. Ships "register & test a Mongo server."
3. **Schema introspection** — sampling → adapted `SchemaTree`; schema browser renders
   Mongo. Ships the read-only browser.
4. **Read queries** — Mongo query JSON, `ExecuteQuery`, results flattening, backend
   enforcement via `FindReferencedPaths`; minimal builder UI.
   - **4b. MCP generalization** — dialect-aware `run_query`, `kind` in
     `list_databases`, neutral `get_schema` wording. Reuses the step-4 service call.
5. **Compass-style builder polish** — field autocomplete, pipeline stage editor,
   sensitive-field UX.

**Out of v1 scope (later phases):** writes + approval workflow; mongosh-style text
mode; generalized schema model for a schemaless third engine.

Each numbered step is independently shippable and testable, which is how the
implementation plan should sequence the work.
