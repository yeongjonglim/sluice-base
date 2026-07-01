# Schema Object Visualisation — Design

## Summary

Extend schema visualisation beyond tables to cover the other first-class objects a
PostgreSQL database exposes: views, materialized views, functions/procedures,
sequences, custom types/enums, indexes, and extensions. Objects surface in the existing
schema **sidebar browse tree**, grouped by type. The ERD is unchanged this phase.

The schema tree carries **light metadata only** — enough to list and browse each object.
Full object definitions (view SQL, function bodies) are **out of scope this phase**; no
definition text is fetched or shown.

## Goals

- Classify and return all in-scope object types from Postgres introspection.
- Model them as strongly-typed, additive collections on the existing `SchemaTree`.
- Render them in the sidebar grouped by object type, with per-type metadata and icons.
- Keep the existing table/column/PK/FK output and the ERD unchanged.

## Object types in scope

| Object | Home in tree | Light metadata carried |
|---|---|---|
| Tables | `SchemaInfo.Tables` (existing) | columns, PK, FKs, **+ indexes** |
| Views | `SchemaInfo.Views` | columns |
| Materialized views | `SchemaInfo.MaterializedViews` | columns, indexes |
| Functions / procedures | `SchemaInfo.Routines` | kind, return type, parameters, language, signature |
| Sequences | `SchemaInfo.Sequences` | data type, start/increment/min/max, cycle, owning column |
| Custom types / enums | `SchemaInfo.Types` | kind (enum/composite/domain/range), enum labels / attributes / base type |
| Indexes | `TableInfo.Indexes` / `MaterializedViewInfo.Indexes` | columns, unique, primary, method |
| Extensions | `SchemaTree.Extensions` (db-level) | version, schema |

## Domain model

New collections are **additive** so existing consumers (ERD, MCP, sidebar) keep working
without change. All records live in `src/SluiceBase.Core/Schemas/SchemaTree.cs`.

```
SchemaTree(Schemas, Extensions)                              // + db-level Extensions
SchemaInfo(Name, Tables, Views, MaterializedViews,           // + 4 typed lists
           Routines, Sequences, Types)
TableInfo(Name, Columns, PrimaryKey, ForeignKeys, Indexes)   // + Indexes

ViewInfo(Name, Columns)
MaterializedViewInfo(Name, Columns, Indexes)
RoutineInfo(Name, Kind, ReturnType, Parameters, Language, Signature)   // Kind = function | procedure
RoutineParameter(Name, DataType, Mode)
SequenceInfo(Name, DataType, Start, Increment, MinValue, MaxValue, Cycle, OwnedByColumn)
TypeInfo(Name, Kind, EnumLabels?, Attributes?, BaseType?)    // Kind = enum | composite | domain | range
IndexInfo(Name, Columns, IsUnique, IsPrimary, Method)
ExtensionInfo(Name, Version, Schema)
```

`ColumnInfo`, `PrimaryKey`, and `ForeignKey` are reused unchanged.

## Postgres introspection

All queries stay confined to the Postgres adapter (`PostgresTargetEngine`) — no Npgsql
in domain/business code, per the abstraction rule.

- **Columns move from `information_schema.columns` to `pg_catalog`** (`pg_class` +
  `pg_attribute` + `format_type(atttypid, atttypmod)`, nullability from `attnotnull`).
  Materialized views are absent from `information_schema`, and we need `relkind` to
  classify each relation in a single uniform pass:
  - `r`, `p`, `f` → Tables
  - `v` → Views
  - `m` → Materialized views

  `pg_catalog` is also not privilege-gated the way `information_schema` is for read-only
  credentials, matching the reasoning already documented for constraint introspection.
- **Routines**: `pg_proc` + `pg_namespace`, `prokind` for function vs procedure,
  `pg_get_function_result` / `pg_get_function_arguments` for return type and signature,
  `pg_language` for language. No body (`pg_get_functiondef`) this phase.
- **Sequences**: `pg_sequences` for parameters; `pg_depend` to resolve the owning column.
- **Types**: `pg_type` filtered to `typtype IN ('e','c','d','r')`, excluding array/internal
  rows; enum labels via `pg_enum`; composite attributes and domain base types from the
  respective catalogs.
- **Extensions**: `pg_extension` + `pg_namespace` → name, `extversion`, schema.
- **Indexes**: `pg_index` + `pg_class` + `pg_am`, columns resolved from `indkey` via
  `pg_attribute`; `indisunique` / `indisprimary` flags. Grouped under the owning
  relation (table or matview).

## API surface

- `GET /api/schema/{databaseId}` — unchanged route, response enriched with the new
  collections. Fully backward compatible (additive fields).
- **No new endpoints.** Definitions are out of scope, so there is no definition endpoint,
  no `GetObjectDefinitionAsync`, and no client hook for it.
- `ITargetEngine.GetSchemaAsync` remains the single introspection entry point; only the
  Postgres implementation populates the new collections. Other engines (none implemented
  today) simply return empty collections.
- CI continues to gate `openapi.json` / `schema.ts` drift.

## Frontend

- **Extract `SchemaSidebar` from `query/index.tsx` into
  `components/schema/SchemaSidebar.tsx`.** It currently lives inline (~180 lines) and will
  grow past that with per-type rendering; the extraction is focused, with small
  sub-components per object group. No broader refactor.
- Render grouped folders under each schema — Tables / Views / Materialized Views /
  Functions / Sequences / Types — with indexes nested under their owning table and
  Extensions listed at the database level. Distinct Tabler icon per type.
- Per-type rows show light metadata: view/matview columns on expand, function
  parameters + return type, sequence parameters, enum labels, extension version, index
  columns. No SQL or source body anywhere.
- **`SELECT`-snippet generation extends to views** (views are selectable), still skipping
  sensitive columns. Sequences, functions, types, and extensions are display-only.
- `ErdCanvas` / `buildErdModel` — **unchanged** (sidebar-only decision).
- Regenerate `src/frontend/src/api/schema.ts` from OpenAPI.

## Testing

- **IntegrationTests** (Testcontainers Postgres): seed a fixture DB with one of each — a
  view, a materialized view (with an index), a function, a procedure, a sequence, an enum
  type, a composite type, an index, and a contrib extension (e.g. `citext`, bundled in the
  postgres image). Assert `GetSchemaAsync` classifies each into the correct collection
  with the expected metadata. Extend `SchemaEndpointTests` and `TargetEngineTests`.
- **Regression guard:** because column introspection switches from `information_schema`
  to `pg_catalog`, tests must confirm existing table / column / PK / FK output is
  unchanged after the switch.
- **Frontend:** a `schema-hooks` test for the enriched shape and a `SchemaSidebar`
  rendering test covering the grouped folders and per-type rows.

## Out of scope (this phase)

- Object definition text — view SQL, function/procedure bodies.
- ERD changes (sidebar only).
- Triggers; check/unique constraints beyond PK/FK.
- Partitioned and foreign tables as distinct types — folded into Tables.
- Non-Postgres engine implementations — the interface stays engine-neutral; only Postgres
  populates the new data.
- New MCP tooling — the existing MCP schema tool inherits the enriched tree automatically,
  but no new MCP surface is added.
