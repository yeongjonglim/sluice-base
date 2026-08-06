# ERD Schema Filter Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a multi-select schema filter to the ERD diagram page (`/query/diagram`) so a user can narrow the diagram to specific schema(s), shown only when the database has more than one schema.

**Architecture:** Frontend-only. The `GET /api/schema/{databaseId}` response already returns every schema in one cached `SchemaTree`; the pure `buildErdModel` transform gains an optional `visibleSchemas` filter that narrows the emitted nodes/edges and pulls in cross-schema referenced tables. The diagram page owns the filter state (a `null` sentinel meaning "all"), renders a Mantine `MultiSelect` wrapper, and passes a stable `Set` into `ErdCanvas`.

**Tech Stack:** React 19 + TypeScript (strict), Mantine 9.4.2 (`MultiSelect`), `@xyflow/react` + `@dagrejs/dagre` (ERD canvas), Vitest + React Testing Library, ESLint (flat config).

## Global Constraints

- **Frontend-only.** No backend, engine, openapi, or `src/api/schema.ts` change. Do **not** run `npm run gen:api`.
- **Branch:** work is on `feat/erd-schema-filter` (already created). Never commit to `main`.
- **Commit messages:** a single subject line only — no body paragraph.
- **TypeScript array syntax:** use `Array<T>`, never `T[]` (ESLint `@typescript-eslint/array-type`).
- **No effects for derived/reset state.** ESLint runs `react-hooks` flat-recommended (`set-state-in-effect` is an error) **and** `eslint-plugin-react-you-might-not-need-an-effect`. Reset-on-database-change uses the render-time "adjust state during render" pattern (a `prevDatabaseId` state compare), not `useEffect`.
- **Per-task gate:** every task ends by running `npm run lint` and `npm run test` (both from `src/frontend`) and both must pass before committing.
- All shell commands below run from the `src/frontend` directory.

## File Structure

- **Modify** `src/components/erd/buildErdModel.ts` — add optional `visibleSchemas?: Set<string>` param; add `isExternal: boolean` to `TableNodeData`; index tables for cross-schema pull-in.
- **Modify** `src/components/erd/__tests__/buildErdModel.test.ts` — add a multi-schema fixture and filter/pull-in tests (existing single-schema tests untouched).
- **Modify** `src/components/erd/TableNode.tsx` — de-emphasise nodes whose `data.isExternal` is true (dashed border + reduced opacity).
- **Create** `src/components/SchemaSelect.tsx` — presentational Mantine `MultiSelect` wrapper (sibling of `DatabaseSelect.tsx`).
- **Create** `src/components/__tests__/SchemaSelect.test.tsx` — render + toggle tests.
- **Modify** `src/components/erd/ErdCanvas.tsx` — accept `visibleSchemas?: Set<string>` and forward it into `buildErdModel`.
- **Modify** `src/routes/_authed/query/diagram.tsx` — own the filter state, render `SchemaSelect` when `schemas.length > 1`, pass a memoised `Set` to `ErdCanvas`.

---

### Task 1: `buildErdModel` schema filter + `isExternal`

**Files:**
- Modify: `src/components/erd/buildErdModel.ts`
- Test: `src/components/erd/__tests__/buildErdModel.test.ts`

**Interfaces:**
- Consumes: existing `SchemaTree` type (already exported from this file).
- Produces:
  - `buildErdModel(tree: SchemaTree, visibleSchemas?: Set<string>): ErdModel` — when `visibleSchemas` is omitted, output is identical to today's. When provided, only tables in visible schemas become base nodes; a base table's outbound FK into a hidden schema pulls in the referenced table (one hop) flagged `isExternal: true`.
  - `TableNodeData` now includes `isExternal: boolean` (`false` for base nodes, `true` for pulled-in referenced tables).

- [ ] **Step 1: Write the failing tests**

Append to `src/components/erd/__tests__/buildErdModel.test.ts` (leave the existing `tree` fixture and its `describe` block unchanged):

```ts
// A two-schema tree exercising the filter:
//  - public.orders -> public.users (same-schema FK)
//  - public.orders -> audit.log     (cross-schema FK: pulls audit.log in when audit is hidden)
//  - audit.log     -> public.users  (a pulled-in table's own FK: must NOT be drawn — one hop only)
//  - audit.settings -> public.users (hidden table referencing INTO a visible schema: no reverse pull-in)
const multiSchemaTree = {
  schemas: [
    {
      name: "public",
      tables: [
        {
          name: "users",
          columns: [
            { name: "id", dataType: "integer", isNullable: false, isSensitive: false, isRestricted: false },
          ],
          primaryKey: { columns: ["id"] },
          foreignKeys: [],
          indexes: [],
        },
        {
          name: "orders",
          columns: [
            { name: "id", dataType: "integer", isNullable: false, isSensitive: false, isRestricted: false },
            { name: "user_id", dataType: "integer", isNullable: false, isSensitive: false, isRestricted: false },
            { name: "audit_id", dataType: "integer", isNullable: false, isSensitive: false, isRestricted: false },
          ],
          primaryKey: { columns: ["id"] },
          foreignKeys: [
            {
              constraintName: "orders_user_fk",
              columns: ["user_id"],
              referencedSchema: "public",
              referencedTable: "users",
              referencedColumns: ["id"],
            },
            {
              constraintName: "orders_audit_fk",
              columns: ["audit_id"],
              referencedSchema: "audit",
              referencedTable: "log",
              referencedColumns: ["id"],
            },
          ],
          indexes: [],
        },
      ],
      views: [],
      materializedViews: [],
      routines: [],
      sequences: [],
      types: [],
    },
    {
      name: "audit",
      tables: [
        {
          name: "log",
          columns: [
            { name: "id", dataType: "integer", isNullable: false, isSensitive: false, isRestricted: false },
            { name: "actor_id", dataType: "integer", isNullable: false, isSensitive: false, isRestricted: false },
          ],
          primaryKey: { columns: ["id"] },
          foreignKeys: [
            {
              constraintName: "log_actor_fk",
              columns: ["actor_id"],
              referencedSchema: "public",
              referencedTable: "users",
              referencedColumns: ["id"],
            },
          ],
          indexes: [],
        },
        {
          name: "settings",
          columns: [
            { name: "id", dataType: "integer", isNullable: false, isSensitive: false, isRestricted: false },
            { name: "owner_id", dataType: "integer", isNullable: false, isSensitive: false, isRestricted: false },
          ],
          primaryKey: { columns: ["id"] },
          foreignKeys: [
            {
              constraintName: "settings_owner_fk",
              columns: ["owner_id"],
              referencedSchema: "public",
              referencedTable: "users",
              referencedColumns: ["id"],
            },
          ],
          indexes: [],
        },
      ],
      views: [],
      materializedViews: [],
      routines: [],
      sequences: [],
      types: [],
    },
  ],
  extensions: [],
};

describe("buildErdModel schema filter", () => {
  it("unfiltered output includes every schema's tables and FKs", () => {
    const { nodes, edges } = buildErdModel(multiSchemaTree);
    expect(nodes.map((n) => n.id).sort()).toEqual([
      "audit.log",
      "audit.settings",
      "public.orders",
      "public.users",
    ]);
    // All four FKs are drawn when nothing is filtered.
    expect(edges.map((e) => e.label).sort()).toEqual([
      "log_actor_fk",
      "orders_audit_fk",
      "orders_user_fk",
      "settings_owner_fk",
    ]);
    // Base nodes are never flagged external.
    expect(nodes.every((n) => n.data.isExternal === false)).toBe(true);
  });

  it("filtering to one schema keeps only its tables plus pulled-in referenced tables", () => {
    const { nodes } = buildErdModel(multiSchemaTree, new Set(["public"]));
    expect(nodes.map((n) => n.id).sort()).toEqual([
      "audit.log", // pulled in via public.orders -> audit.log
      "public.orders",
      "public.users",
    ]);
    // audit.settings is NOT pulled in (it only references INTO public; no reverse pull-in).
    expect(nodes.find((n) => n.id === "audit.settings")).toBeUndefined();
  });

  it("flags a pulled-in cross-schema table as external and keeps base tables non-external", () => {
    const { nodes } = buildErdModel(multiSchemaTree, new Set(["public"]));
    expect(nodes.find((n) => n.id === "audit.log")!.data.isExternal).toBe(true);
    expect(nodes.find((n) => n.id === "public.orders")!.data.isExternal).toBe(false);
  });

  it("keeps the cross-schema FK edge to the pulled-in table but not the pulled-in table's own FKs", () => {
    const { edges } = buildErdModel(multiSchemaTree, new Set(["public"]));
    const labels = edges.map((e) => e.label).sort();
    // orders' two FKs are drawn; audit.log's own FK (log_actor_fk) is NOT (one hop only).
    expect(labels).toEqual(["orders_audit_fk", "orders_user_fk"]);
    expect(edges.find((e) => e.source === "public.orders" && e.target === "audit.log")).toBeTruthy();
    expect(edges.find((e) => e.source === "audit.log")).toBeUndefined();
  });
});
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `npx vitest run src/components/erd/__tests__/buildErdModel.test.ts`
Expected: the four new tests FAIL — `buildErdModel` ignores the second argument, so filtering returns all four tables and `n.data.isExternal` is `undefined` (not a boolean). The existing tests still pass.

- [ ] **Step 3: Rewrite `buildErdModel.ts` with the filter**

Replace the entire contents of `src/components/erd/buildErdModel.ts` with:

```ts
import type { Edge, Node } from "@xyflow/react";
import type { paths } from "@/api/schema";

export type SchemaTree =
  paths["/api/schema/{databaseId}"]["get"]["responses"][200]["content"]["application/json"];

type SchemaTable = SchemaTree["schemas"][number]["tables"][number];

export interface ErdColumn {
  name: string;
  dataType: string;
  isNullable: boolean;
  isSensitive: boolean;
  isRestricted: boolean;
  isPrimaryKey: boolean;
  isForeignKey: boolean;
}

export interface TableNodeData extends Record<string, unknown> {
  schema: string;
  table: string;
  columns: Array<ErdColumn>;
  // True for a table pulled into the diagram only because a visible table's
  // foreign key references it, while its own schema is filtered out.
  isExternal: boolean;
}

export type TableNode = Node<TableNodeData, "table">;

export interface ErdModel {
  nodes: Array<TableNode>;
  edges: Array<Edge>;
}

function buildTableNode(schemaName: string, table: SchemaTable, isExternal: boolean): TableNode {
  const pkColumns = new Set(table.primaryKey?.columns ?? []);
  const fkColumns = new Set(table.foreignKeys.flatMap((fk) => fk.columns));

  return {
    id: `${schemaName}.${table.name}`,
    type: "table",
    position: { x: 0, y: 0 },
    data: {
      schema: schemaName,
      table: table.name,
      isExternal,
      columns: table.columns.map((c) => ({
        name: c.name,
        dataType: c.dataType,
        isNullable: c.isNullable,
        isSensitive: c.isSensitive,
        isRestricted: c.isRestricted,
        isPrimaryKey: pkColumns.has(c.name),
        isForeignKey: fkColumns.has(c.name),
      })),
    },
  };
}

/**
 * Build the React Flow node/edge model from a schema tree.
 *
 * When `visibleSchemas` is omitted, every table in every schema is rendered
 * (the diagram's original behaviour). When provided, only tables in the listed
 * schemas are rendered as "base" nodes; a base table's outbound foreign key
 * into a hidden schema pulls the referenced table in (one hop) flagged
 * `isExternal`, so the relationship stays visible.
 */
export function buildErdModel(tree: SchemaTree, visibleSchemas?: Set<string>): ErdModel {
  const nodes: Array<TableNode> = [];
  const edges: Array<Edge> = [];

  // Index every table by `${schema}.${table}` so a referenced table can be
  // resolved for pull-in regardless of whether its schema is currently visible.
  const tableIndex = new Map<string, { schemaName: string; table: SchemaTable }>();
  for (const schema of tree.schemas) {
    for (const table of schema.tables) {
      tableIndex.set(`${schema.name}.${table.name}`, { schemaName: schema.name, table });
    }
  }

  const nodeIds = new Set<string>();
  const addNode = (schemaName: string, table: SchemaTable, isExternal: boolean) => {
    const id = `${schemaName}.${table.name}`;
    if (nodeIds.has(id)) return;
    nodeIds.add(id);
    nodes.push(buildTableNode(schemaName, table, isExternal));
  };

  const isVisible = (schemaName: string) =>
    visibleSchemas === undefined || visibleSchemas.has(schemaName);

  // Base nodes: every table in a visible schema.
  for (const schema of tree.schemas) {
    if (!isVisible(schema.name)) continue;
    for (const table of schema.tables) {
      addNode(schema.name, table, false);
    }
  }

  // Edges from base tables' outbound FKs; pull in referenced tables from hidden
  // schemas (one hop) so the relationship stays drawn.
  for (const schema of tree.schemas) {
    if (!isVisible(schema.name)) continue;
    for (const table of schema.tables) {
      const sourceId = `${schema.name}.${table.name}`;
      for (const fk of table.foreignKeys) {
        const targetId = `${fk.referencedSchema}.${fk.referencedTable}`;
        if (!nodeIds.has(targetId)) {
          const referenced = tableIndex.get(targetId);
          if (referenced) {
            addNode(referenced.schemaName, referenced.table, true);
          }
        }
        edges.push({
          // constraintName is unique within the database — safe to use as the React Flow edge id.
          id: fk.constraintName,
          source: sourceId,
          target: targetId,
          // Anchor the edge at the related column rows (first column for composite keys),
          // matching the per-column handle ids rendered by TableNode.
          sourceHandle: fk.columns[0],
          targetHandle: fk.referencedColumns[0],
          label: fk.constraintName,
        });
      }
    }
  }

  return { nodes, edges };
}
```

Note: the unfiltered path produces the same nodes (same schema/table iteration order) and the same edges (one per FK, same order) as before — only `isExternal: false` is added to node data, which the existing tests don't assert against.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `npx vitest run src/components/erd/__tests__/buildErdModel.test.ts`
Expected: PASS — all existing tests plus the four new filter tests.

- [ ] **Step 5: Lint and full test gate**

Run: `npm run lint && npm run test`
Expected: both PASS (no `T[]`/`Array<T>` violations; coverage thresholds still met).

- [ ] **Step 6: Commit**

```bash
git add src/components/erd/buildErdModel.ts src/components/erd/__tests__/buildErdModel.test.ts
git commit -m "Add optional schema filter with cross-schema pull-in to buildErdModel"
```

---

### Task 2: De-emphasise pulled-in (external) table nodes

**Files:**
- Modify: `src/components/erd/TableNode.tsx`

**Interfaces:**
- Consumes: `TableNodeData.isExternal` (from Task 1).
- Produces: no new exported symbols — visual behaviour only.

There is no unit test for this task: `TableNode` renders `@xyflow/react` `Handle`s, which require React Flow store context, so it cannot be rendered standalone (the repo has no `TableNode` test for this reason). Task 1's tests already prove `isExternal` reaches node data; this task's gate is the type check, lint, and the manual Aspire check in Task 4.

- [ ] **Step 1: Apply the conditional styling**

In `src/components/erd/TableNode.tsx`, change the outer `<Box>`'s `style` object so the border and opacity depend on `data.isExternal`. Replace:

```tsx
    <Box
      style={{
        border: "1px solid var(--mantine-color-default-border)",
        borderRadius: "var(--mantine-radius-sm)",
        background: "var(--mantine-color-body)",
        minWidth: 220,
        overflow: "hidden",
      }}
    >
```

with:

```tsx
    <Box
      style={{
        // Pulled-in tables from a hidden schema read as "referenced only": dashed + faded.
        border: data.isExternal
          ? "1px dashed var(--mantine-color-default-border)"
          : "1px solid var(--mantine-color-default-border)",
        borderRadius: "var(--mantine-radius-sm)",
        background: "var(--mantine-color-body)",
        minWidth: 220,
        overflow: "hidden",
        opacity: data.isExternal ? 0.6 : 1,
      }}
    >
```

- [ ] **Step 2: Type-check, lint, and test gate**

Run: `npx tsc -b && npm run lint && npm run test`
Expected: all PASS. (`data.isExternal` is now a required boolean on `TableNodeData`, so the type check confirms every node sets it.)

- [ ] **Step 3: Commit**

```bash
git add src/components/erd/TableNode.tsx
git commit -m "De-emphasise pulled-in external table nodes in the ERD"
```

---

### Task 3: `SchemaSelect` component

**Files:**
- Create: `src/components/SchemaSelect.tsx`
- Test: `src/components/__tests__/SchemaSelect.test.tsx`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `SchemaSelect({ schemas, value, onChange })` — a presentational Mantine `MultiSelect`. Props:
  - `schemas: Array<string>` — all available schema names.
  - `value: Array<string>` — currently selected schema names.
  - `onChange: (value: Array<string>) => void` — fired with the new selection.

- [ ] **Step 1: Write the failing test**

Create `src/components/__tests__/SchemaSelect.test.tsx`:

```tsx
import { fireEvent, render, screen } from "@testing-library/react";
import { MantineProvider } from "@mantine/core";
import { describe, expect, it, vi } from "vitest";
import { SchemaSelect } from "@/components/SchemaSelect";

function renderSelect(value: Array<string>) {
  const onChange = vi.fn();
  render(
    <MantineProvider>
      <SchemaSelect schemas={["public", "audit"]} value={value} onChange={onChange} />
    </MantineProvider>,
  );
  return { onChange };
}

describe("SchemaSelect", () => {
  it("shows each selected schema as a pill", () => {
    renderSelect(["public"]);
    expect(screen.getByText("public")).toBeInTheDocument();
  });

  it("calls onChange with the added schema when an option is picked", () => {
    const { onChange } = renderSelect([]);
    fireEvent.click(screen.getByPlaceholderText("Schemas"));
    fireEvent.click(screen.getByRole("option", { name: "audit" }));
    expect(onChange).toHaveBeenCalledWith(["audit"]);
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `npx vitest run src/components/__tests__/SchemaSelect.test.tsx`
Expected: FAIL — module `@/components/SchemaSelect` does not exist.

- [ ] **Step 3: Create the component**

Create `src/components/SchemaSelect.tsx`:

```tsx
import { MultiSelect } from "@mantine/core";

interface SchemaSelectProps {
  schemas: Array<string>;
  value: Array<string>;
  onChange: (value: Array<string>) => void;
}

export function SchemaSelect({ schemas, value, onChange }: SchemaSelectProps) {
  return (
    <MultiSelect
      placeholder="Schemas"
      data={schemas}
      value={value}
      onChange={onChange}
      size="sm"
      // Clearing to empty would blank the diagram; "all schemas" is the meaningful reset,
      // which the page handles via its null-selection sentinel.
      clearable={false}
    />
  );
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `npx vitest run src/components/__tests__/SchemaSelect.test.tsx`
Expected: PASS — both tests.

- [ ] **Step 5: Lint and full test gate**

Run: `npm run lint && npm run test`
Expected: both PASS.

- [ ] **Step 6: Commit**

```bash
git add src/components/SchemaSelect.tsx src/components/__tests__/SchemaSelect.test.tsx
git commit -m "Add SchemaSelect multi-select component for the ERD toolbar"
```

---

### Task 4: Wire the filter through `ErdCanvas` and the diagram page

**Files:**
- Modify: `src/components/erd/ErdCanvas.tsx`
- Modify: `src/routes/_authed/query/diagram.tsx`

**Interfaces:**
- Consumes: `buildErdModel(tree, visibleSchemas?)` (Task 1), `SchemaSelect` (Task 3).
- Produces: `ErdCanvas` gains an optional `visibleSchemas?: Set<string>` prop. No other exported surface changes.

This task is plumbing across two files that share one concern (routing the filter into the canvas); neither has a clean standalone unit test (`ErdCanvas` renders React Flow; the page composes queries and React Flow). Its gate is the type check, lint, the full test run, and the manual Aspire verification steps below.

- [ ] **Step 1: Add the `visibleSchemas` prop to `ErdCanvas`**

In `src/components/erd/ErdCanvas.tsx`, change the component signature and the layout effect. Replace:

```tsx
export function ErdCanvas({ tree }: { tree: SchemaTree }) {
  // Controlled state with change handlers so nodes are draggable and React Flow can
  // record measured dimensions (which the minimap needs to render node rectangles).
  const [nodes, setNodes, onNodesChange] = useNodesState<TableNodeType>([]);
  const [edges, setEdges, onEdgesChange] = useEdgesState<Edge>([]);

  useEffect(() => {
    const model = buildErdModel(tree);
    setNodes(layout(model.nodes, model.edges));
    setEdges(model.edges);
  }, [tree, setNodes, setEdges]);
```

with:

```tsx
export function ErdCanvas({
  tree,
  visibleSchemas,
}: {
  tree: SchemaTree;
  visibleSchemas?: Set<string>;
}) {
  // Controlled state with change handlers so nodes are draggable and React Flow can
  // record measured dimensions (which the minimap needs to render node rectangles).
  const [nodes, setNodes, onNodesChange] = useNodesState<TableNodeType>([]);
  const [edges, setEdges, onEdgesChange] = useEdgesState<Edge>([]);

  useEffect(() => {
    const model = buildErdModel(tree, visibleSchemas);
    setNodes(layout(model.nodes, model.edges));
    setEdges(model.edges);
  }, [tree, visibleSchemas, setNodes, setEdges]);
```

(The caller memoises `visibleSchemas` so its identity is stable across unrelated re-renders — see Step 2 — which keeps this effect from re-running and discarding dragged node positions.)

- [ ] **Step 2: Wire filter state into the diagram page**

In `src/routes/_authed/query/diagram.tsx`:

1. Update the React import to add `useMemo` and `useState`:

```tsx
import { useMemo, useState } from "react";
```

2. Add the `SchemaSelect` import next to the `DatabaseSelect` import:

```tsx
import { SchemaSelect } from "@/components/SchemaSelect";
```

3. Inside `DiagramPage`, immediately after the existing hooks (`selectedDatabaseId`, `schema`, `catalog`, `exportDdl`), add the filter state, the render-time database-change reset, and the derived values:

```tsx
  // null = "all schemas" (the default). Becomes a concrete array once the user narrows it.
  const [selectedSchemas, setSelectedSchemas] = useState<Array<string> | null>(null);

  // Reset the schema filter to "all" whenever the selected database changes.
  // Done during render (not in an effect) per react.dev "You Might Not Need an Effect".
  const [prevDatabaseId, setPrevDatabaseId] = useState(selectedDatabaseId);
  if (selectedDatabaseId !== prevDatabaseId) {
    setPrevDatabaseId(selectedDatabaseId);
    setSelectedSchemas(null);
  }

  const allSchemaNames = (schema.data?.schemas ?? []).map((s) => s.name);
  const effectiveSelected = selectedSchemas ?? allSchemaNames;

  // Stable Set identity (keyed on the selection state) so ErdCanvas's layout effect
  // only re-runs when the filter actually changes, preserving dragged node positions.
  const visibleSchemas = useMemo(
    () => (selectedSchemas === null ? undefined : new Set(selectedSchemas)),
    [selectedSchemas],
  );
```

4. In the toolbar `Group`, render `SchemaSelect` after `DatabaseSelect` (only when the database exposes more than one schema). Replace:

```tsx
        <Group justify="space-between" wrap="nowrap">
          <DatabaseSelect value={selectedDatabaseId} onChange={setSelectedDatabaseId} />
          <Button
```

with:

```tsx
        <Group justify="space-between" wrap="nowrap">
          <Group gap="xs" wrap="nowrap">
            <DatabaseSelect value={selectedDatabaseId} onChange={setSelectedDatabaseId} />
            {allSchemaNames.length > 1 && (
              <SchemaSelect
                schemas={allSchemaNames}
                value={effectiveSelected}
                onChange={setSelectedSchemas}
              />
            )}
          </Group>
          <Button
```

5. Pass the filter into the canvas. Replace:

```tsx
        {selectedDatabaseId && schema.data && <ErdCanvas tree={schema.data} />}
```

with:

```tsx
        {selectedDatabaseId && schema.data && (
          <ErdCanvas tree={schema.data} visibleSchemas={visibleSchemas} />
        )}
```

- [ ] **Step 3: Type-check, lint, and full test gate**

Run: `npx tsc -b && npm run lint && npm run test`
Expected: all PASS. In particular, lint must not report `set-state-in-effect`, `react-you-might-not-need-an-effect`, or `exhaustive-deps` errors (the reset is render-time, not an effect; `visibleSchemas` depends only on `selectedSchemas`).

- [ ] **Step 4: Manual verification in the running app**

Start the app per the project's local run instructions (Aspire; app at `https://localhost:5443`, log in `alice`/`dev`). Then on `/query/diagram`:

1. Select a Postgres database that has **more than one** schema → the "Schemas" multi-select appears between the database selector and Export DDL, with **all schemas selected** and the diagram unchanged from before.
2. Deselect a schema → its tables disappear and the diagram re-lays-out.
3. Confirm a still-visible table whose FK points into the now-hidden schema keeps its referenced table drawn, **dashed and faded**, its header naming the hidden schema, with the FK edge intact.
4. Select a database that exposes **exactly one** schema → **no** picker is shown.
5. Switch back to the multi-schema database → the picker is reset to **all schemas**.

- [ ] **Step 5: Commit**

```bash
git add src/components/erd/ErdCanvas.tsx src/routes/_authed/query/diagram.tsx
git commit -m "Wire schema filter into the ERD diagram page"
```

---

## Self-Review

**Spec coverage:**

- Frontend-only, filter the cached `SchemaTree` (spec §1, §2.1) → Tasks 1 & 4; no `gen:api` (Global Constraints).
- Multi-select, all-on default (spec §2.2, §3) → Task 4 `null` sentinel + `effectiveSelected`.
- Ephemeral state, not `sessionStorage` (spec §2.3) → Task 4 uses `useState`, no persistence.
- Picker only when `schemas.length > 1` (spec §2.4, §3) → Task 4 `allSchemaNames.length > 1` guard.
- Cross-schema outbound pull-in, one hop, no reverse (spec §2.5, §4) → Task 1 logic + tests (`orders_audit_fk` pulled in; `log_actor_fk` and `settings_owner_fk` excluded).
- `isExternal` flag + de-emphasised nodes (spec §4, §5) → Tasks 1 & 2.
- `buildErdModel(tree, visibleSchemas?)`, unfiltered unchanged (spec §4) → Task 1 signature + unfiltered test.
- `ErdCanvas` prop + effect dep (spec §6) → Task 4 Step 1.
- `SchemaSelect` wrapping Mantine `MultiSelect`, `clearable={false}`, not searchable (spec §7) → Task 3.
- Tests: filter subset, pull-in external, edge survives, no reverse pull-in, `SchemaSelect` render/toggle (spec §8.1) → Tasks 1 & 3.

**Placeholder scan:** none — every step contains concrete code or an exact command.

**Type consistency:** `buildErdModel(tree, visibleSchemas?: Set<string>)`, `TableNodeData.isExternal: boolean`, and `SchemaSelect`'s `{ schemas, value, onChange }` (all `Array<string>`) are used identically in Tasks 1, 2, 3, and 4. `visibleSchemas` is a `Set<string>` everywhere (built in the page, consumed by `ErdCanvas` and `buildErdModel`).
