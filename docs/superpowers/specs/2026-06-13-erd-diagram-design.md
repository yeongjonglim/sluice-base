# SluiceBase ERD Diagram — Design

**Date:** 2026-06-13
**Status:** Proposed
**Predecessors:** Schema Browser (`2026-05-07-schema-browser-design.md`), Catalog Endpoint (`2026-05-13-catalog-endpoint-design.md`), Column-based query authorization (`2026-05-19-column-based-query-authorization-design.md`)

## 1. Purpose & scope

Users with `query:execute` on a database can already browse its schema as a collapsible tree on `/query`. This feature lets them **view that schema as an interactive Entity-Relationship Diagram** — table boxes with their columns, and lines connecting foreign keys — on a new `/query/diagram` page.

The schema browser deliberately deferred constraints/keys/relationships. An ERD needs them, so the core backend work is **introspecting primary and foreign keys** and exposing them through a new endpoint. The frontend renders the diagram with React Flow, lazy-loaded so its weight is only paid when the page is opened.

### In scope

- `SchemaRelationships` / `PrimaryKey` / `ForeignKey` domain records in `Core`.
- `GetRelationshipsAsync` added to `ITargetEngine`; implemented in `PostgresTargetEngine` via `information_schema` constraint views.
- `GET /api/schema/{databaseId}/relationships` endpoint, gated **exactly like** `GET /api/schema/{databaseId}` (per-database `query:execute` role check).
- New nested route `/query/diagram` (`routes/_authed/query/diagram.tsx`), guarded identically to `/query`.
- A "Diagram" link nested under the existing **Query** navbar item (alongside Editor and History).
- A shared database-selector component extracted from the current query page selector, reused on the diagram page.
- React Flow (`@xyflow/react`) + `@dagrejs/dagre` auto-layout, both `React.lazy`-loaded into a separate chunk fetched only when `/query/diagram` mounts.
- Table nodes show columns with PK/FK markers; restricted/sensitive columns show the same lock icon as the tree. FK edges drawn between tables (across schemas).
- Integration tests, Vitest unit tests (pure transform + hook), and a Playwright E2E spec.

### Out of scope (deferred)

- Editing the schema / generating DDL (this is a read-only viewer, unlike pgAdmin's ERD editor).
- Persisting node positions server-side (positions are client-side only in v1; see §7).
- Exporting the diagram as an image/PDF.
- Indexes, unique constraints, check constraints, views, functions, sequences.
- A per-schema selector — all non-system schemas render together (§2, decision 5).
- Non-Postgres engines (no other `ITargetEngine` implementation exists in v1).
- Lazy/partial loading of very large diagrams (see §8 risks).

### Success criteria

With Aspire running:

1. Alice (has `query:execute` on Blue) sees a "Diagram" link nested under **Query** in the navbar.
2. Alice opens `/query/diagram`, selects "Blue", and sees table nodes laid out automatically with their columns.
3. Foreign-key relationships are drawn as edges between the related tables, including any that cross schema boundaries.
4. Primary-key columns are marked; restricted columns appear with a lock icon, consistent with the schema tree.
5. The React Flow bundle is **not** downloaded until `/query/diagram` is opened (verifiable as a separate network chunk request on first navigation).
6. Bob (no `query:execute` on Blue) is redirected away from `/query/diagram` and gets 403 from the relationships endpoint.
7. `dotnet test` and `npm run test` pass; `npm run test:e2e` passes the new `query-diagram.spec.ts`.

## 2. Architectural decisions

| # | Decision | Rationale |
|---|----------|-----------|
| 1 | Nested `/query/diagram` route, not a top-level `/erd` | Keeps the query workspace as the single home for "exploring a database"; follows the existing `/query` + `/query/history` nesting. Pages stay focused; parallelism via browser tabs. |
| 2 | Separate `GetRelationshipsAsync` + endpoint, not extending `GetSchemaAsync` | ERD is used infrequently; loading the common schema-tree path with 2 extra constraint queries on every `/query` visit is wasteful. The diagram page composes the existing `useSchema` (columns, already access-annotated) with a new `useRelationships`. |
| 3 | `information_schema` constraint views for PK/FK | Consistent with decision #3 of the schema-browser design (portable across engines). pg_catalog is the fallback only if composite-FK correctness proves insufficient (§4). |
| 4 | React Flow (`@xyflow/react`) + `@dagrejs/dagre` | Modern open-source schema visualizers (Liam ERD, ChartDB, DrawDB) converged on React Flow for read-from-existing-schema ERDs; it is TypeScript-first and composes with Mantine. pgAdmin's `react-diagrams` is editor-first and less maintained — not needed here. `dagre` gives automatic layout since tables have no stored coordinates; it is lighter than `elkjs` (no web worker). |
| 5 | Render all non-system schemas together, edges cross boundaries | Confirmed with user — most complete view. System schemas already excluded upstream in the engine SQL. |
| 6 | Restricted columns shown with a lock icon, not hidden | Confirmed with user. Consistent with the tree; avoids breaking an FK edge whose column is restricted. Structure is visible; values stay protected at query time. |
| 7 | `React.lazy` + dynamic `import()` for the diagram canvas | Vite emits the React Flow + dagre dependency as a separate chunk fetched only when the canvas mounts. This is the project's first use of code-splitting. |
| 8 | Node positions are client-side only in v1 | No metadata-DB schema change; revisiting a database re-runs auto-layout. Persistence (localStorage or server) is a clean later addition. |

## 3. Domain model

New file `SluiceBase.Core/Schemas/SchemaRelationships.cs`:

```csharp
namespace SluiceBase.Core.Schemas;

public sealed record SchemaRelationships(
    IReadOnlyList<PrimaryKey> PrimaryKeys,
    IReadOnlyList<ForeignKey> ForeignKeys);

public sealed record PrimaryKey(
    string Schema,
    string Table,
    IReadOnlyList<string> Columns);

public sealed record ForeignKey(
    string ConstraintName,
    string Schema,
    string Table,
    IReadOnlyList<string> Columns,
    string ReferencedSchema,
    string ReferencedTable,
    IReadOnlyList<string> ReferencedColumns);
```

Pure data records, serialised directly as the endpoint response. `Columns`/`ReferencedColumns` are ordered lists to support composite keys.

## 4. `ITargetEngine` extension

Add to `SluiceBase.Core/Targets/ITargetEngine.cs`:

```csharp
Task<SchemaRelationships> GetRelationshipsAsync(string connectionString, CancellationToken ct);
```

### `PostgresTargetEngine` implementation

Two queries against `information_schema`, mirroring the system-schema exclusion already used in `GetSchemaAsync`.

**Primary keys:**

```sql
SELECT tc.table_schema, tc.table_name, kcu.column_name, kcu.ordinal_position
FROM information_schema.table_constraints tc
JOIN information_schema.key_column_usage kcu
  ON tc.constraint_name = kcu.constraint_name
 AND tc.table_schema = kcu.table_schema
WHERE tc.constraint_type = 'PRIMARY KEY'
  AND tc.table_schema NOT IN ('information_schema', 'pg_catalog', 'pg_toast')
ORDER BY tc.table_schema, tc.table_name, kcu.ordinal_position;
```

**Foreign keys** (joined on ordinal position so composite keys pair correctly — avoids the cartesian pitfall of `constraint_column_usage`):

```sql
SELECT
    rc.constraint_name,
    kcu.table_schema, kcu.table_name, kcu.column_name, kcu.ordinal_position,
    ccu.table_schema  AS ref_schema,
    ccu.table_name    AS ref_table,
    ccu.column_name   AS ref_column
FROM information_schema.referential_constraints rc
JOIN information_schema.key_column_usage kcu
  ON kcu.constraint_name = rc.constraint_name
 AND kcu.constraint_schema = rc.constraint_schema
JOIN information_schema.key_column_usage ccu
  ON ccu.constraint_name = rc.unique_constraint_name
 AND ccu.constraint_schema = rc.unique_constraint_schema
 AND ccu.ordinal_position = kcu.position_in_unique_constraint
WHERE kcu.table_schema NOT IN ('information_schema', 'pg_catalog', 'pg_toast')
ORDER BY rc.constraint_name, kcu.ordinal_position;
```

Rows are grouped in C# by `(schema, table)` for PKs and by `constraint_name` for FKs into the record hierarchy — no extra round-trips. Connection handling matches `GetSchemaAsync` (`NpgsqlDataSource.Create` / read credential). The endpoint stays engine-agnostic.

## 5. API endpoint

Added to `SchemaEndpoints` (`SluiceBase.Api/Endpoints/SchemaEndpoint.cs`):

| Endpoint | Method | Auth | Notes |
|---|---|---|---|
| `/api/schema/{databaseId}/relationships` | GET | per-database `query:execute` | Returns `SchemaRelationships` |

The handler reuses the **exact** authorization shape of `GetSchema`: load the database (404 if missing), check `UserDatabaseRoles` for a `query:execute` role on that `databaseId` (403 if absent), resolve the read connection string via `IServerConnectionFactory`, call `targetEngine.GetRelationshipsAsync`, return `Ok`. `InvalidOperationException` → `BadRequest`, matching the existing handler. No sensitive-column annotation is applied — relationship payloads are structural (key column names), and the tree already exposes those names under the same access rules.

## 6. Frontend

### 6.1 Route & guard

New file `routes/_authed/query/diagram.tsx` → `/query/diagram`, with the **same** `beforeLoad` guard as `routes/_authed/query/index.tsx` (`me.permissions.includes("query:execute")` → else `redirect({ to: "/" })`).

### 6.2 Navbar

Add a third nested `NavLink` ("Diagram", `IconSitemap` or `IconBinaryTree`) under the existing **Query** parent in `routes/_authed.tsx`, beside the Editor (`/query`) and History (`/query/history`) links. Visible only when `useHasPermission("query:execute")`.

### 6.3 Shared database selector

Extract the catalog-driven `Select` currently inline in `query/index.tsx` into `components/DatabaseSelect.tsx` (props: `value`, `onChange`), backed by `useCatalogServer()`. Reuse it in both `query/index.tsx` and `query/diagram.tsx`. Selected `databaseId` persists via the existing `useSessionState` pattern.

### 6.4 Lazy-loaded canvas

- `components/erd/ErdCanvas.tsx` imports `@xyflow/react` and `@dagrejs/dagre`. It is **not** imported directly by the route.
- The route does: `const ErdCanvas = React.lazy(() => import("@/components/erd/ErdCanvas"))` and renders it inside `<Suspense fallback={<Center h="100%"><Loader/></Center>}>`. This isolates React Flow into its own Vite chunk loaded on first mount.

### 6.5 Data → diagram transform

A pure function `buildErdModel(tree, relationships)` in `components/erd/buildErdModel.ts` (unit-tested independently of React) produces React Flow `nodes` and `edges`:

- One node per table; node data = ordered columns with `{ name, dataType, isPrimaryKey, isForeignKey, isRestricted }`. PK/FK flags derived from `relationships`; `isRestricted`/`isSensitive` carried from `useSchema`'s annotated `ColumnInfo`.
- One edge per `ForeignKey` (source table → referenced table), labelled with the constraint name; connected to column-level ports where practical, else table-to-table.
- `dagre` computes initial `x/y` for every node; nodes remain draggable afterwards.

### 6.6 Table node component

`components/erd/TableNode.tsx`: Mantine-styled card. Header = `schema.table`. Each column row shows name, `<Code>` data type, a key icon for PK, a relation icon for FK, and the `IconLock` used by the tree for `isRestricted` columns. React Flow handles/ports attached for edge anchoring.

### 6.7 Page composition

`query/diagram.tsx`: `<DatabaseSelect>` at top; below it `useSchema(databaseId)` + `useRelationships(databaseId)`. While either loads → `Skeleton`/`Loader`; on error → Mantine `Alert`; when both resolve → `<Suspense><ErdCanvas tree={...} relationships={...} /></Suspense>`. React Flow fills the remaining viewport height with pan/zoom and a `Controls` + `MiniMap`.

### 6.8 Hook

Added to `api/hooks.ts`:

```ts
type SchemaRelationshipsResponse =
  paths["/api/schema/{databaseId}/relationships"]["get"]["responses"][200]["content"]["application/json"];

export function useRelationships(databaseId: string | null) {
  return useQuery({
    queryKey: ["relationships", databaseId] as const,
    queryFn: () =>
      apiRequest<void, SchemaRelationshipsResponse>(
        `/api/schema/${databaseId}/relationships`,
      ),
    enabled: databaseId !== null,
    staleTime: 5 * 60 * 1000,
  });
}
```

`schema.ts` is regenerated via `npm run gen:api` after the backend adds the endpoint to `openapi.json`.

## 7. Node position persistence

v1: positions are ephemeral — auto-layout runs each time a database is selected. Not persisted to the metadata DB (no migration) or localStorage. Documented as a deliberate deferral; adding localStorage keyed by `databaseId` later requires no API change.

## 8. Tests

### 8.1 Backend integration (`SchemaRelationshipEndpointTests.cs`)

Existing `[Collection("Aspire")]` fixture + `KeycloakLoginHelper`:

| Test | Asserts |
|---|---|
| `GetRelationships_ReturnsKeys_ForBlue` | Blue returns ≥1 primary key and ≥1 foreign key; a known seed FK appears with correct referenced table/column |
| `GetRelationships_Returns403_ForBob` | User without a `query:execute` role on Blue gets 403 |
| `GetRelationships_Returns401_ForAnonymous` | Unauthenticated → 401 |
| `GetRelationships_Returns404_ForUnknownDatabase` | Unknown `databaseId` → 404 |

### 8.2 Frontend Vitest

- `buildErdModel.test.ts`: given a small `tree` + `relationships`, produces the expected node count, column PK/FK flags, restricted-column flag passthrough, and one edge per FK.
- `relationships-hooks.test.ts`: `useRelationships` is disabled when `databaseId` is `null`; uses query key `["relationships", databaseId]`.

### 8.3 Playwright E2E (`e2e/query-diagram.spec.ts`)

Signed in as Alice:

1. Navigate to `/query/diagram` — page renders with the database selector.
2. Select "Blue" — table nodes appear.
3. At least one FK edge is rendered.
4. A restricted column shows the lock icon.

### 8.4 Out of test scope

- Auto-layout aesthetics / exact coordinates.
- Large-schema performance.
- Non-Postgres engines.

## 9. Packages, risks, acceptance

### 9.1 New packages

Frontend: `@xyflow/react`, `@dagrejs/dagre`. No new backend (NuGet) packages — relationship queries use the existing `Npgsql` connection.

### 9.2 DI / registration

None beyond the new handler on the existing `SchemaEndpoints` group.

### 9.3 Risks & open questions

- **Composite-FK correctness:** handled by joining on `position_in_unique_constraint`/`ordinal_position`; if any engine quirk surfaces, fall back to pg_catalog (`pg_constraint.conkey/confkey`) inside `PostgresTargetEngine` only.
- **Large schemas:** hundreds of tables produce a dense diagram; auto-layout and React Flow handle it, but readability degrades. Lazy/filtered rendering is a future enhancement and needs no API change.
- **`ITargetEngine` interface change:** adding `GetRelationshipsAsync` breaks any other implementation; none exists in v1.

### 9.4 Acceptance criteria

- `dotnet build SluiceBase.slnx` clean (warnings-as-errors).
- `dotnet test SluiceBase.slnx` passes (prior + new relationship tests).
- `npm run build` clean (TS strict + ESLint, including `Array<T>` rule).
- `npm run test` passes (prior + new tests).
- `aspire run`: Alice opens `/query/diagram`, selects Blue, sees nodes + FK edges + lock icons; the React Flow chunk loads only on that navigation; Bob is redirected from `/query/diagram` and gets 403.
- `npm run test:e2e` passes `query-diagram.spec.ts`.

## 10. References

- Schema Browser design: `docs/superpowers/specs/2026-05-07-schema-browser-design.md`
- Catalog Endpoint design: `docs/superpowers/specs/2026-05-13-catalog-endpoint-design.md`
- Column-based query authorization: `docs/superpowers/specs/2026-05-19-column-based-query-authorization-design.md`
- `ITargetEngine`: `src/SluiceBase.Core/Targets/ITargetEngine.cs`
- `PostgresTargetEngine`: `src/SluiceBase.Api/Targets/PostgresTargetEngine.cs`
- `SchemaEndpoint`: `src/SluiceBase.Api/Endpoints/SchemaEndpoint.cs`
- React Flow: https://reactflow.dev — chosen per Liam ERD ADR (2024-11-12) and ChartDB/DrawDB precedent
