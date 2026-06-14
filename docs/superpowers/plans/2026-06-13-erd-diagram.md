# ERD Diagram Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let users with `query:execute` view the schema of a chosen database as an interactive Entity-Relationship Diagram (table boxes + foreign-key lines) on a new `/query/diagram` page.

**Architecture:** Extend the existing `GET /api/schema/{databaseId}` contract so its `SchemaTree` also carries primary keys and foreign keys (one cache entry serves both the tree and the diagram). The frontend renders the diagram with React Flow + a dagre auto-layout, code-split via the router's existing `autoCodeSplitting` plus a `vendor/erd` manual chunk so the heavy dependency only loads on the diagram page.

**Tech Stack:** .NET 10 / Npgsql / `information_schema` (backend); React 19 + TypeScript + Mantine + TanStack Query/Router + `@xyflow/react` + `@dagrejs/dagre` (frontend).

**Spec:** `docs/superpowers/specs/2026-06-13-erd-diagram-design.md`

---

## File Structure

**Backend**
- Modify: `src/SluiceBase.Core/Schemas/SchemaTree.cs` — add `PrimaryKey`/`ForeignKey` records; add two params to `SchemaTree`.
- Modify: `src/SluiceBase.Api/Targets/PostgresTargetEngine.cs` — populate PK/FK in `GetSchemaAsync`.
- Modify: `src/SluiceBase.Api/Endpoints/SchemaEndpoint.cs:92` — carry PK/FK through the annotation rebuild.
- Modify: `tests/IntegrationTests/SchemaEndpointTests.cs` — add PK/FK assertions.

**Frontend**
- Modify: `src/frontend/package.json` — add `@xyflow/react`, `@dagrejs/dagre`.
- Modify: `src/frontend/vite.config.ts` — add a `vendor/erd` manual chunk.
- Modify: `src/frontend/src/api/schema.ts` — regenerated via `npm run gen:api`.
- Create: `src/frontend/src/components/DatabaseSelect.tsx` — shared catalog database picker.
- Modify: `src/frontend/src/routes/_authed/query/index.tsx` — use `DatabaseSelect`.
- Create: `src/frontend/src/components/erd/buildErdModel.ts` — pure `SchemaTree` → nodes/edges.
- Create: `src/frontend/src/components/erd/__tests__/buildErdModel.test.ts` — unit tests.
- Create: `src/frontend/src/components/erd/TableNode.tsx` — custom React Flow node.
- Create: `src/frontend/src/components/erd/ErdCanvas.tsx` — React Flow canvas + dagre layout.
- Create: `src/frontend/src/routes/_authed/query/diagram.tsx` — `/query/diagram` route.
- Modify: `src/frontend/src/routes/_authed.tsx` — add the nested "Diagram" nav link.

> **Lazy-loading note:** This repo already sets `autoCodeSplitting: true` in `vite.config.ts`, so each route module (including `query/diagram.tsx`) is its own chunk loaded on navigation. Importing React Flow inside the diagram route's component tree therefore keeps it out of the editor/initial bundle. We additionally add a `vendor/erd` manual chunk (matching the existing `vendor/*` pattern) so React Flow + dagre are isolated and only fetched alongside the diagram route. No `React.lazy`/`Suspense` is needed — this is the idiomatic approach for this codebase and supersedes the spec's `React.lazy` phrasing while achieving the identical "loads only on `/query/diagram`" outcome.

---

## Task 1: Extend the schema contract with PK/FK records

**Files:**
- Modify: `src/SluiceBase.Core/Schemas/SchemaTree.cs`
- Modify: `src/SluiceBase.Api/Targets/PostgresTargetEngine.cs:74`
- Modify: `src/SluiceBase.Api/Endpoints/SchemaEndpoint.cs:92`

This task only changes the type and fixes the two construction sites so the solution compiles. PK/FK are populated as empty lists here; Task 2 fills them.

- [ ] **Step 1: Add the new records and extend `SchemaTree`**

Replace the entire contents of `src/SluiceBase.Core/Schemas/SchemaTree.cs` with:

```csharp
namespace SluiceBase.Core.Schemas;

public sealed record SchemaTree(
    IReadOnlyList<SchemaInfo> Schemas,
    IReadOnlyList<PrimaryKey> PrimaryKeys,
    IReadOnlyList<ForeignKey> ForeignKeys);

public sealed record SchemaInfo(string Name, IReadOnlyList<TableInfo> Tables);
public sealed record TableInfo(string Name, IReadOnlyList<ColumnInfo> Columns);
public sealed record ColumnInfo(string Name, string DataType, bool IsNullable, bool IsSensitive = false, bool IsRestricted = false);

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

- [ ] **Step 2: Fix the engine construction site**

In `src/SluiceBase.Api/Targets/PostgresTargetEngine.cs`, change the return at the end of `GetSchemaAsync` (line ~74):

```csharp
        return new SchemaTree(schemas, [], []);
```

- [ ] **Step 3: Fix the endpoint annotation rebuild**

In `src/SluiceBase.Api/Endpoints/SchemaEndpoint.cs`, change line ~92 so the rebuilt tree carries the PK/FK lists through unchanged:

```csharp
            return TypedResults.Ok(new SchemaTree(annotatedSchemas, tree.PrimaryKeys, tree.ForeignKeys));
```

- [ ] **Step 4: Build to verify it compiles**

Run: `dotnet build SluiceBase.slnx`
Expected: build succeeds, warnings-as-errors clean.

- [ ] **Step 5: Commit**

```bash
git add src/SluiceBase.Core/Schemas/SchemaTree.cs src/SluiceBase.Api/Targets/PostgresTargetEngine.cs src/SluiceBase.Api/Endpoints/SchemaEndpoint.cs
git commit -m "feat: add primary/foreign key records to schema contract"
```

---

## Task 2: Introspect primary and foreign keys in the engine

**Files:**
- Modify: `src/SluiceBase.Api/Targets/PostgresTargetEngine.cs`
- Test: `tests/IntegrationTests/SchemaEndpointTests.cs`

Known seed facts (from `src/AppHost/seed/blue/01-init.sql`): `public.users` has PK `id`; `public.orders.user_id` is a FK referencing `public.users.id`.

- [ ] **Step 1: Write the failing integration tests**

Add these two tests to `tests/IntegrationTests/SchemaEndpointTests.cs` (inside the `SchemaEndpointTests` class, after `GetSchema_ReturnsTree_ForBlueDatabase`). They reuse the existing `AuthorizedSessionWithBlueServerAsync` helper and deserialize into the real `SchemaTree` type (imported via `using SluiceBase.Core.Schemas;`, already present):

```csharp
    [Fact]
    public async Task GetSchema_IncludesPrimaryKeys_ForBlueDatabase()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, _, databaseId) = await AuthorizedSessionWithBlueServerAsync(ct);
        using var _ = session;

        var tree = await session.Client.GetFromJsonAsync<SchemaTree>($"/api/schema/{databaseId}", ct);

        Assert.NotNull(tree);
        var usersPk = Assert.Single(
            tree.PrimaryKeys,
            pk => pk.Schema == "public" && pk.Table == "users");
        Assert.Equal(["id"], usersPk.Columns);
    }

    [Fact]
    public async Task GetSchema_IncludesForeignKeys_ForBlueDatabase()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, _, databaseId) = await AuthorizedSessionWithBlueServerAsync(ct);
        using var _ = session;

        var tree = await session.Client.GetFromJsonAsync<SchemaTree>($"/api/schema/{databaseId}", ct);

        Assert.NotNull(tree);
        var ordersFk = Assert.Single(
            tree.ForeignKeys,
            fk => fk.Schema == "public" && fk.Table == "orders"
                  && fk.Columns.Contains("user_id"));
        Assert.Equal("public", ordersFk.ReferencedSchema);
        Assert.Equal("users", ordersFk.ReferencedTable);
        Assert.Equal(["id"], ordersFk.ReferencedColumns);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test SluiceBase.slnx --filter "FullyQualifiedName~SchemaEndpointTests.GetSchema_IncludesPrimaryKeys_ForBlueDatabase|FullyQualifiedName~SchemaEndpointTests.GetSchema_IncludesForeignKeys_ForBlueDatabase"`
Expected: FAIL — `PrimaryKeys`/`ForeignKeys` are empty (Task 1 returned `[]`), so `Assert.Single` throws.

- [ ] **Step 3: Implement PK/FK introspection in `GetSchemaAsync`**

In `src/SluiceBase.Api/Targets/PostgresTargetEngine.cs`, replace the whole `GetSchemaAsync` method (lines ~35-75) with the version below. It keeps the existing columns query and connection handling, then runs two more queries on the same open connection and returns a fully-populated `SchemaTree`:

```csharp
    public async Task<SchemaTree> GetSchemaAsync(string connectionString, CancellationToken ct)
    {
        const string excludeSystemSchemas =
            "table_schema NOT IN ('information_schema', 'pg_catalog', 'pg_toast')";

        const string columnsSql = $"""
                           SELECT table_schema, table_name, column_name, data_type, is_nullable
                           FROM information_schema.columns
                           WHERE {excludeSystemSchemas}
                           ORDER BY table_schema, table_name, ordinal_position;
                           """;

        const string primaryKeysSql = """
                           SELECT tc.table_schema, tc.table_name, kcu.column_name
                           FROM information_schema.table_constraints tc
                           JOIN information_schema.key_column_usage kcu
                             ON tc.constraint_name = kcu.constraint_name
                            AND tc.table_schema = kcu.table_schema
                           WHERE tc.constraint_type = 'PRIMARY KEY'
                             AND tc.table_schema NOT IN ('information_schema', 'pg_catalog', 'pg_toast')
                           ORDER BY tc.table_schema, tc.table_name, kcu.ordinal_position;
                           """;

        const string foreignKeysSql = """
                           SELECT
                               rc.constraint_name,
                               kcu.table_schema, kcu.table_name, kcu.column_name,
                               ccu.table_schema AS ref_schema,
                               ccu.table_name   AS ref_table,
                               ccu.column_name  AS ref_column
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
                           """;

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var connection = await dataSource.OpenConnectionAsync(ct);

        // Columns
        var columnRows = new List<(string Schema, string Table, string Column, string DataType, bool IsNullable)>();
        await using (var command = new NpgsqlCommand(columnsSql, connection))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                columnRows.Add((
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4) == "YES"));
            }
        }

        // Primary keys
        var pkRows = new List<(string Schema, string Table, string Column)>();
        await using (var command = new NpgsqlCommand(primaryKeysSql, connection))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                pkRows.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
            }
        }

        // Foreign keys
        var fkRows = new List<(string Constraint, string Schema, string Table, string Column, string RefSchema, string RefTable, string RefColumn)>();
        await using (var command = new NpgsqlCommand(foreignKeysSql, connection))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                fkRows.Add((
                    reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                    reader.GetString(4), reader.GetString(5), reader.GetString(6)));
            }
        }

        var schemas = columnRows
            .GroupBy(r => r.Schema)
            .Select(sg => new SchemaInfo(
                sg.Key,
                [
                    .. sg.GroupBy(r => r.Table)
                        .Select(tg => new TableInfo(
                            tg.Key,
                            [.. tg.Select(c => new ColumnInfo(c.Column, c.DataType, c.IsNullable))]))
                ]))
            .ToList();

        var primaryKeys = pkRows
            .GroupBy(r => (r.Schema, r.Table))
            .Select(g => new PrimaryKey(g.Key.Schema, g.Key.Table, [.. g.Select(r => r.Column)]))
            .ToList();

        var foreignKeys = fkRows
            .GroupBy(r => r.Constraint)
            .Select(g => new ForeignKey(
                g.Key,
                g.First().Schema,
                g.First().Table,
                [.. g.Select(r => r.Column)],
                g.First().RefSchema,
                g.First().RefTable,
                [.. g.Select(r => r.RefColumn)]))
            .ToList();

        return new SchemaTree(schemas, primaryKeys, foreignKeys);
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test SluiceBase.slnx --filter "FullyQualifiedName~SchemaEndpointTests"`
Expected: PASS (all schema endpoint tests, including the two new ones).

- [ ] **Step 5: Commit**

```bash
git add src/SluiceBase.Api/Targets/PostgresTargetEngine.cs tests/IntegrationTests/SchemaEndpointTests.cs
git commit -m "feat: introspect primary and foreign keys in schema endpoint"
```

---

## Task 3: Regenerate the frontend API types

**Files:**
- Modify: `src/frontend/src/api/schema.ts` (generated)

- [ ] **Step 1: Rebuild the API so `openapi.json` reflects the new contract**

Run: `dotnet build src/SluiceBase.Api/SluiceBase.Api.csproj`
(The csproj sets `OpenApiDocumentsDirectory=.` and generates documents on non-Release builds, so this rewrites `src/SluiceBase.Api/openapi.json` with the new `primaryKeys`/`foreignKeys` fields.)

- [ ] **Step 2: Regenerate types from the OpenAPI document**

Run: `cd src/frontend && npm run gen:api`
(This runs `openapi-typescript ../SluiceBase.Api/openapi.json -o src/api/schema.ts`.)

- [ ] **Step 3: Verify the new fields exist**

Run: `cd src/frontend && grep -n "primaryKeys\|foreignKeys\|referencedTable" src/api/schema.ts`
Expected: matches for `primaryKeys`, `foreignKeys`, and `referencedTable`.

- [ ] **Step 4: Type-check passes**

Run: `cd src/frontend && npx tsc -b`
Expected: no errors (existing `useSchema` consumers still compile; new fields are additive).

- [ ] **Step 5: Commit**

```bash
git add src/frontend/src/api/schema.ts
git commit -m "chore: regenerate api types with key relationships"
```

---

## Task 4: Add React Flow + dagre and the vendor chunk

**Files:**
- Modify: `src/frontend/package.json`
- Modify: `src/frontend/vite.config.ts`

- [ ] **Step 1: Install the dependencies**

Run: `cd src/frontend && npm install @xyflow/react @dagrejs/dagre`
Expected: both added to `dependencies` in `package.json`.

- [ ] **Step 2: Add a `vendor/erd` manual chunk**

In `src/frontend/vite.config.ts`, inside the `manualChunks(id)` function, add this rule alongside the existing `vendor/*` rules (e.g. directly after the `@tabler` line):

```ts
            if (id.includes("@xyflow") || id.includes("dagre")) return "vendor/erd";
```

- [ ] **Step 3: Verify the build still works**

Run: `cd src/frontend && npm run build`
Expected: build succeeds; a `vendor/erd` chunk is emitted. (Nothing imports it yet, so it may be tree-shaken out until Task 8 — that is fine.)

- [ ] **Step 4: Commit**

```bash
git add src/frontend/package.json src/frontend/package-lock.json src/frontend/vite.config.ts
git commit -m "chore: add react flow and dagre with isolated vendor chunk"
```

---

## Task 5: Extract a shared `DatabaseSelect` component

**Files:**
- Create: `src/frontend/src/components/DatabaseSelect.tsx`
- Modify: `src/frontend/src/routes/_authed/query/index.tsx`

This avoids duplicating the catalog-driven picker between the editor and diagram pages.

- [ ] **Step 1: Create the component**

Create `src/frontend/src/components/DatabaseSelect.tsx`:

```tsx
import { Select } from "@mantine/core";
import { useCatalogServer } from "@/api/hooks";

interface DatabaseSelectProps {
  value: string | null;
  onChange: (value: string | null) => void;
}

export function DatabaseSelect({ value, onChange }: DatabaseSelectProps) {
  const servers = useCatalogServer();

  const databaseOptions = (servers.data?.servers ?? []).flatMap((s) =>
    s.databases.map((d) => ({
      value: d.id,
      label: `${s.name} — ${d.displayName}`,
    })),
  );

  return (
    <Select
      placeholder="Select a database"
      data={databaseOptions}
      value={value}
      onChange={onChange}
      size="sm"
    />
  );
}
```

- [ ] **Step 2: Use it in the editor page**

In `src/frontend/src/routes/_authed/query/index.tsx`:

1. Add the import near the other component imports:

```tsx
import { DatabaseSelect } from "@/components/DatabaseSelect";
```

2. Remove the now-unused `databaseOptions` constant (the `const databaseOptions = (servers.data?.servers ?? []).flatMap(...)` block) and the `const servers = useCatalogServer();` line above it.
3. Replace the inline `<Select placeholder="Select a database" ... mb="xs" size="sm" />` block (lines ~155-162) with this exact markup, which preserves the previous `mb="xs"` spacing using the already-imported `Box`:

```tsx
          <Box mb="xs">
            <DatabaseSelect value={selectedDatabaseId} onChange={setSelectedDatabaseId} />
          </Box>
```

4. Remove `useCatalogServer` from the `@/api/hooks` import (it is now unused in this file), and remove `Select` from the `@mantine/core` import if it is no longer referenced elsewhere in the file (check with a search before removing).

- [ ] **Step 3: Type-check and lint**

Run: `cd src/frontend && npx tsc -b && npm run lint`
Expected: no errors.

- [ ] **Step 4: Run existing frontend tests**

Run: `cd src/frontend && npm run test`
Expected: PASS (no behavior change).

- [ ] **Step 5: Commit**

```bash
git add src/frontend/src/components/DatabaseSelect.tsx src/frontend/src/routes/_authed/query/index.tsx
git commit -m "refactor: extract shared DatabaseSelect component"
```

---

## Task 6: Build the pure `buildErdModel` transform

**Files:**
- Create: `src/frontend/src/components/erd/buildErdModel.ts`
- Test: `src/frontend/src/components/erd/__tests__/buildErdModel.test.ts`

This pure function maps a `SchemaTree` (columns + keys) to React Flow nodes/edges. No layout/positioning here (that lives in `ErdCanvas`), so it is deterministic and unit-testable.

- [ ] **Step 1: Write the failing test**

Create `src/frontend/src/components/erd/__tests__/buildErdModel.test.ts`:

```ts
import { describe, expect, it } from "vitest";
import { buildErdModel } from "@/components/erd/buildErdModel";

const tree = {
  schemas: [
    {
      name: "public",
      tables: [
        {
          name: "users",
          columns: [
            { name: "id", dataType: "integer", isNullable: false, isSensitive: false, isRestricted: false },
            { name: "email", dataType: "text", isNullable: false, isSensitive: true, isRestricted: true },
          ],
        },
        {
          name: "orders",
          columns: [
            { name: "id", dataType: "integer", isNullable: false, isSensitive: false, isRestricted: false },
            { name: "user_id", dataType: "integer", isNullable: false, isSensitive: false, isRestricted: false },
          ],
        },
      ],
    },
  ],
  primaryKeys: [
    { schema: "public", table: "users", columns: ["id"] },
    { schema: "public", table: "orders", columns: ["id"] },
  ],
  foreignKeys: [
    {
      constraintName: "orders_user_id_fkey",
      schema: "public",
      table: "orders",
      columns: ["user_id"],
      referencedSchema: "public",
      referencedTable: "users",
      referencedColumns: ["id"],
    },
  ],
};

describe("buildErdModel", () => {
  it("creates one node per table keyed by schema.table", () => {
    const { nodes } = buildErdModel(tree);
    expect(nodes.map((n) => n.id).sort()).toEqual(["public.orders", "public.users"]);
  });

  it("marks primary-key and foreign-key columns", () => {
    const { nodes } = buildErdModel(tree);
    const orders = nodes.find((n) => n.id === "public.orders")!;
    const idCol = orders.data.columns.find((c) => c.name === "id")!;
    const fkCol = orders.data.columns.find((c) => c.name === "user_id")!;
    expect(idCol.isPrimaryKey).toBe(true);
    expect(fkCol.isForeignKey).toBe(true);
    expect(fkCol.isPrimaryKey).toBe(false);
  });

  it("passes through the restricted flag", () => {
    const { nodes } = buildErdModel(tree);
    const users = nodes.find((n) => n.id === "public.users")!;
    expect(users.data.columns.find((c) => c.name === "email")!.isRestricted).toBe(true);
  });

  it("creates one edge per foreign key linking the two tables", () => {
    const { edges } = buildErdModel(tree);
    expect(edges).toHaveLength(1);
    expect(edges[0]).toMatchObject({
      source: "public.orders",
      target: "public.users",
      label: "orders_user_id_fkey",
    });
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd src/frontend && npm run test -- buildErdModel`
Expected: FAIL — module `@/components/erd/buildErdModel` does not exist.

- [ ] **Step 3: Implement the transform**

Create `src/frontend/src/components/erd/buildErdModel.ts`:

```ts
import type { Edge, Node } from "@xyflow/react";
import type { paths } from "@/api/schema";

type SchemaTree =
  paths["/api/schema/{databaseId}"]["get"]["responses"][200]["content"]["application/json"];

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
}

export type TableNode = Node<TableNodeData, "table">;

export interface ErdModel {
  nodes: Array<TableNode>;
  edges: Array<Edge>;
}

export function buildErdModel(tree: SchemaTree): ErdModel {
  const pkColumns = new Map<string, Set<string>>();
  for (const pk of tree.primaryKeys) {
    pkColumns.set(`${pk.schema}.${pk.table}`, new Set(pk.columns));
  }

  const fkColumns = new Map<string, Set<string>>();
  for (const fk of tree.foreignKeys) {
    const key = `${fk.schema}.${fk.table}`;
    const set = fkColumns.get(key) ?? new Set<string>();
    for (const c of fk.columns) set.add(c);
    fkColumns.set(key, set);
  }

  const nodes: Array<TableNode> = [];
  for (const schema of tree.schemas) {
    for (const table of schema.tables) {
      const tableKey = `${schema.name}.${table.name}`;
      const pks = pkColumns.get(tableKey) ?? new Set<string>();
      const fks = fkColumns.get(tableKey) ?? new Set<string>();
      nodes.push({
        id: tableKey,
        type: "table",
        position: { x: 0, y: 0 },
        data: {
          schema: schema.name,
          table: table.name,
          columns: table.columns.map((c) => ({
            name: c.name,
            dataType: c.dataType,
            isNullable: c.isNullable,
            isSensitive: c.isSensitive,
            isRestricted: c.isRestricted,
            isPrimaryKey: pks.has(c.name),
            isForeignKey: fks.has(c.name),
          })),
        },
      });
    }
  }

  const edges: Array<Edge> = tree.foreignKeys.map((fk) => ({
    id: fk.constraintName,
    source: `${fk.schema}.${fk.table}`,
    target: `${fk.referencedSchema}.${fk.referencedTable}`,
    label: fk.constraintName,
  }));

  return { nodes, edges };
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd src/frontend && npm run test -- buildErdModel`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add src/frontend/src/components/erd/buildErdModel.ts src/frontend/src/components/erd/__tests__/buildErdModel.test.ts
git commit -m "feat: add buildErdModel schema-to-diagram transform"
```

---

## Task 7: Build the `TableNode` component

**Files:**
- Create: `src/frontend/src/components/erd/TableNode.tsx`

A custom React Flow node rendering a table box: header `schema.table`, one row per column with type, PK/FK markers, and the lock icon for restricted columns.

- [ ] **Step 1: Implement the component**

Create `src/frontend/src/components/erd/TableNode.tsx`:

```tsx
import { Box, Code, Group, Text } from "@mantine/core";
import { IconKey, IconLink, IconLock } from "@tabler/icons-react";
import { Handle, Position, type NodeProps } from "@xyflow/react";
import type { TableNode as TableNodeType } from "@/components/erd/buildErdModel";

export function TableNode({ data }: NodeProps<TableNodeType>) {
  return (
    <Box
      style={{
        border: "1px solid var(--mantine-color-default-border)",
        borderRadius: 6,
        background: "var(--mantine-color-body)",
        minWidth: 220,
        overflow: "hidden",
      }}
    >
      <Handle type="target" position={Position.Left} style={{ opacity: 0 }} />
      <Handle type="source" position={Position.Right} style={{ opacity: 0 }} />
      <Box
        px="sm"
        py={6}
        style={{
          background: "var(--mantine-color-default-hover)",
          borderBottom: "1px solid var(--mantine-color-default-border)",
        }}
      >
        <Text size="sm" fw={600}>
          {data.schema}.{data.table}
        </Text>
      </Box>
      {data.columns.map((col) => (
        <Group key={col.name} px="sm" py={3} gap="xs" wrap="nowrap" justify="space-between">
          <Group gap={6} wrap="nowrap">
            {col.isPrimaryKey && <IconKey size={13} color="var(--mantine-color-yellow-6)" />}
            {col.isForeignKey && <IconLink size={13} color="var(--mantine-color-blue-5)" />}
            <Text size="xs">{col.name}</Text>
            {col.isRestricted && <IconLock size={12} color="var(--mantine-color-dimmed)" />}
          </Group>
          <Code fz={10}>{col.dataType}</Code>
        </Group>
      ))}
    </Box>
  );
}
```

- [ ] **Step 2: Type-check**

Run: `cd src/frontend && npx tsc -b`
Expected: no errors.

- [ ] **Step 3: Commit**

```bash
git add src/frontend/src/components/erd/TableNode.tsx
git commit -m "feat: add ERD table node component"
```

---

## Task 8: Build the `ErdCanvas` with dagre layout

**Files:**
- Create: `src/frontend/src/components/erd/ErdCanvas.tsx`

Applies a dagre layout to the nodes from `buildErdModel`, then renders React Flow with pan/zoom, controls, and a minimap. React Flow's CSS is imported here so it lands in the `vendor/erd`-adjacent route chunk.

- [ ] **Step 1: Implement the canvas**

Create `src/frontend/src/components/erd/ErdCanvas.tsx`:

```tsx
import { useMemo } from "react";
import Dagre from "@dagrejs/dagre";
import {
  Background,
  Controls,
  MiniMap,
  ReactFlow,
  type Edge,
} from "@xyflow/react";
import "@xyflow/react/dist/style.css";
import { TableNode } from "@/components/erd/TableNode";
import { buildErdModel, type TableNode as TableNodeType } from "@/components/erd/buildErdModel";
import type { paths } from "@/api/schema";

type SchemaTree =
  paths["/api/schema/{databaseId}"]["get"]["responses"][200]["content"]["application/json"];

const nodeTypes = { table: TableNode };

// Rough node height estimate for layout spacing: header + per-column rows.
function estimateHeight(node: TableNodeType): number {
  return 34 + node.data.columns.length * 22;
}

function layout(nodes: Array<TableNodeType>, edges: Array<Edge>): Array<TableNodeType> {
  const g = new Dagre.graphlib.Graph().setDefaultEdgeLabel(() => ({}));
  g.setGraph({ rankdir: "LR", nodesep: 40, ranksep: 120 });

  const width = 240;
  for (const node of nodes) {
    g.setNode(node.id, { width, height: estimateHeight(node) });
  }
  for (const edge of edges) {
    g.setEdge(edge.source, edge.target);
  }

  Dagre.layout(g);

  return nodes.map((node) => {
    const { x, y } = g.node(node.id);
    const height = estimateHeight(node);
    return { ...node, position: { x: x - width / 2, y: y - height / 2 } };
  });
}

export function ErdCanvas({ tree }: { tree: SchemaTree }) {
  const { nodes, edges } = useMemo(() => {
    const model = buildErdModel(tree);
    return { nodes: layout(model.nodes, model.edges), edges: model.edges };
  }, [tree]);

  return (
    <ReactFlow
      nodes={nodes}
      edges={edges}
      nodeTypes={nodeTypes}
      fitView
      minZoom={0.1}
      proOptions={{ hideAttribution: true }}
    >
      <Background />
      <Controls />
      <MiniMap pannable zoomable />
    </ReactFlow>
  );
}

export default ErdCanvas;
```

- [ ] **Step 2: Type-check and build**

Run: `cd src/frontend && npx tsc -b && npm run build`
Expected: no errors; a `vendor/erd` chunk is emitted in the build output.

- [ ] **Step 3: Commit**

```bash
git add src/frontend/src/components/erd/ErdCanvas.tsx
git commit -m "feat: add ERD canvas with dagre auto-layout"
```

---

## Task 9: Add the `/query/diagram` route and page

**Files:**
- Create: `src/frontend/src/routes/_authed/query/diagram.tsx`

- [ ] **Step 1: Create the route**

Create `src/frontend/src/routes/_authed/query/diagram.tsx`:

```tsx
import { Alert, Box, Center, Loader, Stack } from "@mantine/core";
import { createFileRoute, redirect } from "@tanstack/react-router";
import { ErdCanvas } from "@/components/erd/ErdCanvas";
import { DatabaseSelect } from "@/components/DatabaseSelect";
import { meQueryOptions, useSchema } from "@/api/hooks";
import { useSessionState } from "@/utils/useSessionState";

export const Route = createFileRoute("/_authed/query/diagram")({
  beforeLoad: ({ context }) => {
    const me = context.queryClient.getQueryData(meQueryOptions.queryKey);
    if (!me?.permissions.includes("query:execute")) {
      throw redirect({ to: "/" });
    }
  },
  component: DiagramPage,
});

function DiagramPage() {
  const [selectedDatabaseId, setSelectedDatabaseId] = useSessionState<string | null>(
    "sluice:query:db",
    null,
  );
  const schema = useSchema(selectedDatabaseId);

  return (
    <Stack
      gap={0}
      style={{
        margin: "calc(-1 * var(--mantine-spacing-sm))",
        height: "calc(100vh - 44px)",
      }}
    >
      <Box p="xs" style={{ borderBottom: "1px solid var(--mantine-color-default-border)" }}>
        <DatabaseSelect value={selectedDatabaseId} onChange={setSelectedDatabaseId} />
      </Box>
      <Box style={{ flex: 1, minHeight: 0 }}>
        {!selectedDatabaseId && (
          <Center h="100%">
            <Box c="dimmed">Select a database to view its diagram</Box>
          </Center>
        )}
        {selectedDatabaseId && schema.isPending && (
          <Center h="100%">
            <Loader />
          </Center>
        )}
        {selectedDatabaseId && schema.isError && (
          <Alert color="red" m="md" title="Failed to load schema">
            {schema.error instanceof Error ? schema.error.message : "Unknown error"}
          </Alert>
        )}
        {selectedDatabaseId && schema.data && <ErdCanvas tree={schema.data} />}
      </Box>
    </Stack>
  );
}
```

> The route uses the **same** `useSessionState` key `"sluice:query:db"` as the editor page, so the chosen database stays in sync across the editor and diagram views within a session.

- [ ] **Step 2: Type-check, lint, build**

Run: `cd src/frontend && npx tsc -b && npm run lint && npm run build`
Expected: no errors; the route plugin generates the `/query/diagram` route into `routeTree.gen.ts`, and the build emits the `vendor/erd` chunk only reachable from this route.

- [ ] **Step 3: Commit**

```bash
git add src/frontend/src/routes/_authed/query/diagram.tsx src/frontend/src/routeTree.gen.ts
git commit -m "feat: add /query/diagram ERD page"
```

---

## Task 10: Add the nested "Diagram" nav link

**Files:**
- Modify: `src/frontend/src/routes/_authed.tsx`

- [ ] **Step 1: Add the icon import**

In `src/frontend/src/routes/_authed.tsx`, add `IconSitemap` to the existing `@tabler/icons-react` import block (alphabetical placement is fine; the import is a list).

- [ ] **Step 2: Add the nav link**

Inside the `<NavLink label="Query" ...>` parent (around lines 145-164), after the "History" `<NavLink>` and before the parent's closing `</NavLink>`, add:

```tsx
              <NavLink
                label="Diagram"
                leftSection={<IconSitemap size={16} />}
                component={Link}
                to="/query/diagram"
                active={location.pathname === "/query/diagram"}
                pl="md"
                onClick={closeMobileNav}
              />
```

- [ ] **Step 3: Type-check, lint, build**

Run: `cd src/frontend && npx tsc -b && npm run lint && npm run build`
Expected: no errors.

- [ ] **Step 4: Run the full frontend test suite**

Run: `cd src/frontend && npm run test`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/frontend/src/routes/_authed.tsx
git commit -m "feat: add Diagram link under Query navigation"
```

---

## Final verification

- [ ] **Backend:** `dotnet build SluiceBase.slnx` clean; `dotnet test SluiceBase.slnx` passes.
- [ ] **Frontend:** `cd src/frontend && npm run build` clean; `npm run test` passes; `npm run lint` clean.
- [ ] **Manual (`aspire run`):** Alice opens `/query/diagram`, selects "Blue", sees `users`/`orders`/`products`/`transactions` nodes laid out automatically; the `orders → users` and `transactions → users`/`products` FK edges are drawn; PK columns show a key icon; any restricted column shows a lock icon. In browser devtools Network, confirm the `vendor/erd` chunk is only requested when navigating to `/query/diagram`, not on `/query`. Bob is redirected from `/query/diagram` to `/` and `GET /api/schema/{databaseId}` returns 403 for him.
