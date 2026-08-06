# SluiceBase ERD Schema Filter — Design

**Date:** 2026-08-06
**Status:** Proposed
**Predecessors:** ERD Diagram (`2026-06-13-erd-diagram-design.md`), Schema Browser (`2026-05-07-schema-browser-design.md`), MongoDB support (`2026-07-04-mongodb-support-design.md`)

## 1. Purpose & scope

The ERD page (`/query/diagram`) currently renders **every table from every schema** of the selected database at once (`2026-06-13-erd-diagram-design.md`, §2 decision 5). On a Postgres database with several schemas this is dense and hard to read. This feature adds a **schema filter** to the diagram toolbar so a user can narrow the diagram to the schema(s) they care about.

This is a **frontend-only** change. The `GET /api/schema/{databaseId}` contract already returns all schemas in one `SchemaTree`; the filter narrows what the pure `buildErdModel` transform emits from that same cached response. No backend, API, engine, or DDL-export change.

### In scope

- A new `components/SchemaSelect.tsx` wrapping Mantine `MultiSelect`, listing the loaded database's schema names.
- Filter state in `routes/_authed/query/diagram.tsx`: multi-select, **all schemas selected by default**, reset to "all" when the database changes.
- The picker is rendered **only when the database has more than one schema** — single-schema databases (and single-schema engines such as MongoDB) show no picker.
- `buildErdModel(tree, visibleSchemas?)` gains an optional filter argument. Omitted → today's exact behaviour. Provided → base nodes from visible schemas, plus **cross-schema referenced tables pulled in** so a visible table's outbound foreign key stays connected.
- Pulled-in ("external") table nodes are visually de-emphasised in `TableNode.tsx`.
- `ErdCanvas.tsx` accepts the filter and passes it into `buildErdModel` inside its existing layout effect.
- Vitest unit tests for the new `buildErdModel` behaviour and a light `SchemaSelect` render/toggle test. Existing tests stay green.

### Out of scope (deferred)

- Persisting the schema selection across reloads / to `sessionStorage` (see §3, decision 3). The database selection stays persisted as today; the schema selection is ephemeral component state.
- Pulling in **incoming** cross-schema references (a hidden-schema table whose FK points *into* a visible schema). Only **outbound** FKs of visible tables pull in their referenced table (§4).
- Expanding a pulled-in table's own foreign keys (one hop only, §4).
- Filtering by object type (views, matviews, etc.), search-within-schema, or per-schema colouring.
- Any backend / engine / openapi change. `schema.ts` is unchanged (no contract change).

### Success criteria

With Aspire running and logged in as a user with `query:execute`:

1. On a Postgres database that exposes **more than one** schema, a schema picker appears in the diagram toolbar between the database selector and the Export DDL button, showing **all schemas selected**; the diagram is unchanged from today.
2. Deselecting a schema removes its tables from the diagram and re-lays-out the remaining ones.
3. When a still-visible table has a foreign key into a now-hidden schema, the referenced table is still drawn (visibly de-emphasised, its header naming the hidden schema) and the FK edge remains.
4. On a database exposing **exactly one** schema, no picker is shown.
5. Switching databases resets the picker to "all schemas".
6. `npm run lint`, `npm run test`, and the TypeScript build pass.

## 2. Architectural decisions

| # | Decision | Rationale |
|---|----------|-----------|
| 1 | Frontend-only; filter the existing `SchemaTree` client-side | The full schema is already fetched and cached once by `useSchema(databaseId)` (`2026-06-13`, §2 decision 2). Filtering client-side needs no round-trip, no contract change, and reuses the same cache entry the tree view uses. |
| 2 | Multi-select, **all selected by default** | Confirmed with user. Preserves today's "everything at once" view as the default; the filter is purely additive opt-out. |
| 3 | Selection is ephemeral component state, not `sessionStorage` | Confirmed with user. "All by default" makes a reload-reset harmless, and it sidesteps stale-selection bookkeeping across database switches. Persistence is a clean later addition if wanted. |
| 4 | Picker rendered only when `schemas.length > 1` | Confirmed with user. A single-schema database (or a single-schema engine like MongoDB) has nothing to filter; a dead control would be noise. Uses the loaded schema count — no engine-kind metadata is needed on the client. |
| 5 | Cross-schema FK: **pull in** the referenced table (one hop, outbound only) | Confirmed with user. Keeps relationships legible when a schema is hidden, without the unbounded cascade that pulling in incoming references or expanding pulled-in tables' own FKs would cause. |
| 6 | Mantine `MultiSelect`, not a custom `Popover` + `Checkbox.Group` | Confirmed with user. First-class in Mantine 9.4.2 (`withCheckIcon` list + removable pills), consistent with `DatabaseSelect` wrapping `Select`, and less code. Tradeoff: one pill per selected schema and no built-in "N of M" collapse; acceptable for typical schema counts, mitigatable later via `maw`/`renderPill` if it looks noisy. |

## 3. Filter state (`routes/_authed/query/diagram.tsx`)

The page already owns `selectedDatabaseId` (persisted via `useSessionState`) and calls `useSchema(selectedDatabaseId)`. Add:

- `allSchemaNames = (schema.data?.schemas ?? []).map((s) => s.name)`.
- `const [selectedSchemas, setSelectedSchemas] = useState<Array<string> | null>(null)` — **`null` means "all"** (the default sentinel).
- `effectiveSelected = selectedSchemas ?? allSchemaNames`.
- An effect that **resets `selectedSchemas` to `null`** when `selectedDatabaseId` changes, and also when `selectedSchemas` is non-null but contains a name no longer present in `allSchemaNames` (guards against a stale selection after data changes). Keyed on `selectedDatabaseId` and the joined schema-name list.
- The picker renders only when `allSchemaNames.length > 1`:

  ```tsx
  {allSchemaNames.length > 1 && (
    <SchemaSelect
      schemas={allSchemaNames}
      value={effectiveSelected}
      onChange={(next) => setSelectedSchemas(next)}
    />
  )}
  ```

- The value handed to the canvas is `visibleSchemas = selectedSchemas == null ? undefined : new Set(selectedSchemas)`. `undefined` → unfiltered (identical to today). Passed as `<ErdCanvas tree={schema.data} visibleSchemas={visibleSchemas} />`.

Toolbar layout: the existing `Group justify="space-between"` keeps `DatabaseSelect` (left) and Export (right); `SchemaSelect` sits immediately after `DatabaseSelect` in the left cluster.

## 4. Filtering logic (`components/erd/buildErdModel.ts`)

New optional second parameter:

```ts
export function buildErdModel(tree: SchemaTree, visibleSchemas?: Set<string>): ErdModel
```

When `visibleSchemas` is `undefined`, the function behaves **exactly as today** (all schemas, all edges) — the existing tests call `buildErdModel(tree)` and must stay green.

When `visibleSchemas` is provided:

1. **Build an index** of every table in the tree keyed by `` `${schema.name}.${table.name}` `` → `{ schemaName, table }`, so a referenced table can be looked up regardless of its schema's visibility.
2. **Base nodes** — for each schema whose `name ∈ visibleSchemas`, emit a node per table exactly as today (PK/FK column flags, sensitive/restricted passthrough), with `isExternal: false`.
3. **Edges + pull-in** — for each **base** table, for each of its foreign keys:
   - Emit the edge (`source`, `target`, `sourceHandle`, `targetHandle`, `label`) exactly as today.
   - If the target id `` `${fk.referencedSchema}.${fk.referencedTable}` `` is **not already a node**, look it up in the index and, if found, emit it as a node with `isExternal: true`. If the referenced table is not in the tree at all, no node is added (same dangling-edge outcome as today; not our concern to fix here).
   - A pulled-in table's **own** foreign keys are **not** processed — one hop only. Incoming references from hidden schemas are **not** pulled in.
4. De-duplicate pulled-in nodes by id (multiple visible tables may reference the same hidden table).

`TableNodeData` gains `isExternal: boolean`. `estimateHeight`/layout in `ErdCanvas` are unaffected — external nodes are ordinary nodes with real columns.

Note on the unfiltered path: because all tables are base nodes when unfiltered, the emitted nodes and edges are byte-for-byte what today's code produces (every FK belongs to a base table, every referenced table is a base node), so behaviour and existing tests are preserved.

## 5. Visual treatment (`components/erd/TableNode.tsx`)

When `data.isExternal` is true, render the card de-emphasised so it reads as "referenced, from a schema you've hidden":

- Reduced opacity (e.g. `opacity: 0.6`) and a **dashed** border instead of solid.
- No header/label change is needed — the header already prints `data.schema}.{data.table`, which names the hidden schema.

`TableNodeData.isExternal` is optional-safe: for the unfiltered/all path it is `false`, giving today's appearance.

## 6. Canvas (`components/erd/ErdCanvas.tsx`)

- Add prop: `visibleSchemas?: Set<string>`.
- Inside the existing layout effect, call `buildErdModel(tree, visibleSchemas)` and add `visibleSchemas` to the effect's dependency array, so toggling a schema re-runs layout — the same code path that already handles a database (tree) change.
- No change to `fitView`/controls/minimap. Re-fit on filter change is **not** added in v1; the behaviour matches switching databases today. (If it feels off during verification, `useReactFlow().fitView()` after `setNodes` is a minimal follow-up — noted, not scoped.)

## 7. New component (`components/SchemaSelect.tsx`)

Sibling of `DatabaseSelect.tsx`, wrapping Mantine `MultiSelect`:

```tsx
interface SchemaSelectProps {
  schemas: Array<string>;
  value: Array<string>;
  onChange: (value: Array<string>) => void;
}
```

- `data = schemas` (name → `{ value: name, label: name }`), `value`, `onChange`, `size="sm"`, a placeholder/label such as "Schemas", `clearable={false}` (clearing to empty would blank the diagram; "all" is the meaningful reset). Not `searchable` in v1.
- Self-contained and presentational — the `null`/"all" sentinel logic lives entirely in the page (§3); this component only ever sees concrete arrays.

## 8. Tests

### 8.1 Frontend Vitest (TDD)

Extend `components/erd/__tests__/buildErdModel.test.ts`. The existing fixture is single-schema `public`; add a second schema (e.g. `audit`) with a table (e.g. `log`) and a cross-schema FK from `public.orders` → `audit.log` to exercise pull-in.

| Test | Asserts |
|---|---|
| unfiltered call unchanged | `buildErdModel(tree)` still yields the current nodes/edges (existing tests already cover this). |
| filter to a subset | `buildErdModel(tree, new Set(["public"]))` yields only `public.*` base nodes (all `isExternal: false`) — an `audit`-only table not referenced by `public` is absent. |
| cross-schema FK pulls in referenced table | With `public` visible and an FK `public.orders → audit.log`, the `audit.log` node is present and flagged `isExternal: true`, and the edge to it survives. |
| no reverse pull-in | A hidden-schema table whose FK points into a visible schema is **not** added. |

A light `components/__tests__/SchemaSelect.test.tsx` (following `SchemaSidebar.test.tsx` precedent): renders the given schema names and fires `onChange` on toggle.

### 8.2 Out of test scope

- Auto-layout aesthetics / coordinates.
- The `diagram.tsx` `null`→"all" reset wiring beyond what the component/unit tests cover (verified manually via Aspire, §1 success criteria).

## 9. Risks & acceptance

### 9.1 Risks

- **Pill width with many schemas** — `MultiSelect` shows one pill per selected schema and has no native "N of M" collapse (Mantine 9.4.2). Fine for typical counts; mitigatable later with `maw` (pills scroll) or a `renderPill` summary. Deliberately not pre-optimised.
- **Stale selection after data change** — handled by the reset effect (§3); switching databases or a changed schema set falls back to "all".
- **Signature change to `buildErdModel`** — additive optional parameter; all existing callers (`ErdCanvas`, tests) pass one argument and are unaffected.

### 9.2 Acceptance criteria

- `npm run lint` clean (ESLint gate incl. `Array<T>` rule).
- `npm run test` passes (prior + new tests).
- TypeScript build clean.
- With Aspire running: on a multi-schema Postgres database the picker appears with all schemas on and today's diagram; deselecting a schema removes its tables and keeps cross-schema references drawn (de-emphasised); a single-schema database shows no picker; switching databases resets to all.

## 10. References

- ERD Diagram design: `docs/superpowers/specs/2026-06-13-erd-diagram-design.md`
- Schema Browser design: `docs/superpowers/specs/2026-05-07-schema-browser-design.md`
- MongoDB support design: `docs/superpowers/specs/2026-07-04-mongodb-support-design.md`
- `buildErdModel`: `src/frontend/src/components/erd/buildErdModel.ts`
- `ErdCanvas`: `src/frontend/src/components/erd/ErdCanvas.tsx`
- `TableNode`: `src/frontend/src/components/erd/TableNode.tsx`
- Diagram page: `src/frontend/src/routes/_authed/query/diagram.tsx`
- `DatabaseSelect` (component precedent): `src/frontend/src/components/DatabaseSelect.tsx`
- Mantine `MultiSelect`: `@mantine/core` 9.4.2
