# Schema object metadata — complete the display (#144)

Follow-up to #137, which introspected views, materialized views, functions,
sequences, types, indexes, and extensions and threaded **light metadata** through
the API into the generated TypeScript types. The schema browser renders only a
subset of that metadata. This phase surfaces the rest, and picks up the one item
#137 explicitly deferred — **per-object SQL definitions** for views, materialized
views, and functions.

## Goal

Every field the `SchemaTree` API returns is visible in the schema browser, and a
user can read a view's / materialized view's / function's SQL definition without
downloading the whole-schema DDL dump.

## Gap analysis (API field → shown today)

| Object | Exposed by API | Rendered today | Missing |
|---|---|---|---|
| Foreign keys | referenced schema/table/columns | 🔗 icon on column | **→ referenced table(columns)** |
| Routines | kind, returnType, language, signature | signature + returnType | **kind, language, definition** |
| Sequences | start, increment, min, max, cycle, ownedBy | dataType only | **start / increment / min / max / cycle / owned-by** |
| Types | kind, enumLabels, attributes, baseType | kind + enum labels | **composite attributes, domain base type** |
| Indexes | columns, unique, primary, method | columns, unique/primary | **method** |
| Extensions | version, schema | version | **schema** |
| Views | columns | columns | **definition** (new API field) |
| Materialized views | columns, indexes | columns, indexes | **definition** (new API field) |

Columns, primary keys, table indexes, and view/matview columns are already fully
shown and are untouched.

## Presentation: drawer vs. inline

Rich objects open a **right-side overlay drawer** through a trailing ⓘ control;
one-token gaps fold into the existing inline detail line (no click, cheaper).

| Object | Surfacing | New metadata |
|---|---|---|
| View | Drawer | definition (SQL), columns |
| Materialized view | Drawer | definition (SQL), columns, indexes |
| Function / procedure | Drawer | kind, language, signature, return type, definition (SQL) |
| Sequence | Drawer | start, increment, min, max, cycle, owned-by |
| Type | Drawer | composite attributes / domain base type (enum labels stay inline) |
| Extension | Inline | schema → `version · schema` |
| Index | Inline | method → `cols · unique · btree` |
| Foreign key | Inline | target → FK column detail gains `→ ref_table(cols)` |

Interaction rules:

- **Row-click keeps toggling expand/collapse**, exactly as #137 shipped it. The ⓘ
  is a separate trailing control, sitting next to the existing append-SELECT
  button in the same `stickyRight` group.
- The drawer is non-modal in spirit (dismiss with Esc or the close button) and
  does not shrink the editor/results — it overlays the right pane.

Two deliberate judgment calls:

1. **Extension `schema` is inline**, not a drawer — it is a single missing scalar,
   so a drawer would be overkill.
2. **SQL definitions reuse `SqlEditor` in a read-only configuration** rather than a
   plain `<Code>` block, so definitions get the same syntax highlighting as the
   editor with no new dependency.

## Backend

All changes are additive; existing consumers (ERD, MCP, DDL export) keep working
unchanged. Database-specific SQL stays confined to `PostgresTargetEngine`, per the
abstraction rule.

### Domain model (`src/SluiceBase.Core/Schemas/SchemaTree.cs`)

Add a nullable `Definition` to three records:

```
ViewInfo(Name, Columns, Definition?)
MaterializedViewInfo(Name, Columns, Indexes, Definition?)
RoutineInfo(Name, Kind, ReturnType, Language, Signature, Definition?)
```

`Definition` is nullable: introspection can fail for an individual object (e.g.
insufficient privilege on the underlying source), and a missing definition must
degrade gracefully rather than fail the whole schema load.

### Introspection (`PostgresTargetEngine`)

- **View / materialized view definition** — `pg_get_viewdef(c.oid, true)` for
  relations with `relkind IN ('v','m')`. Pulled alongside the existing relation
  enumeration; the pretty-print flag (`true`) matches how psql renders `\d+`.
- **Routine definition** — `pg_get_functiondef(p.oid)`. Safe for every routine we
  return, because the routines query is already filtered to `prokind IN ('f','p')`
  (`pg_get_functiondef` throws for aggregate and window functions, which are
  excluded).

### Generated contract

Regenerate `src/SluiceBase.Api/openapi.json` and
`src/frontend/src/api/schema.ts` from the running API. CI gates that these stay in
sync (per project convention), so both are committed.

### Backend tests

Extend `SchemaEndpointTests` / `TargetEngineTests`: seed a view, a materialized
view, and a function, and assert each carries a non-empty `Definition` containing
the expected `SELECT` / function body.

## Frontend

### New component — `components/schema/SchemaObjectDrawer.tsx`

- Mantine `Drawer`, `position="right"`, opened by a `selected` object descriptor.
- Renders a metadata card switched on object kind:
  - **View** — definition (read-only `SqlEditor`), column list.
  - **Materialized view** — definition, column list, index list.
  - **Function** — kind, language, signature, return type, definition.
  - **Sequence** — data type, start, increment, min, max, cycle, owned-by.
  - **Type** — kind, plus composite attributes or domain base type.
- The definition block reuses `SqlEditor` configured read-only (no line-run
  keymap, `editable={false}`), so highlighting is consistent with the editor.

### `SchemaSidebar.tsx`

- Owns `useState<SelectedObject | null>` and renders `SchemaObjectDrawer` itself,
  so **`QueryPage` needs no change**. `SelectedObject` is a discriminated union
  over the drawer-eligible kinds carrying the object payload.
- Adds a trailing ⓘ `ActionIcon` (mirroring `AppendSelectButton`) to view,
  materialized-view, function, sequence, and type rows; clicking sets `selected`.
- Inline additions:
  - `IndexRows` detail gains `method` → `cols · unique · btree`.
  - Foreign-key columns gain `→ ref_table(cols)` in their detail (resolved from
    the table's `foreignKeys`, keyed by column).
  - Extension detail becomes `version · schema`.

### Frontend tests (`SchemaSidebar.test.tsx`)

- Clicking ⓘ on a sequence opens the drawer and shows its start/increment/etc.
- A composite type's attributes render in the drawer.
- A view's definition renders in the drawer.
- Index `method` and FK target render inline (no drawer).

## Out of scope

- **Table-level drawer** — tables have no missing metadata; they stay inline-only.
- **Editing** any metadata.
- **Definitions for indexes / sequences / types / extensions** — Postgres has no
  single-object `pg_get_*def` for these, and reconstructing DDL by hand is out of
  proportion; the whole-schema DDL export already covers them.
- **View/matview column sensitivity** — still the separate follow-up noted in #137.
