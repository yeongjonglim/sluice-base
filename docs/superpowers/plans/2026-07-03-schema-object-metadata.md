# Schema Object Metadata Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Surface every metadata field the `SchemaTree` API returns in the schema browser, and add per-object SQL definitions (views, materialized views, functions) shown through a right-side detail drawer.

**Architecture:** Backend gains a nullable `Definition` on `ViewInfo`/`MaterializedViewInfo`/`RoutineInfo`, introspected via `pg_get_viewdef` / `pg_get_functiondef` in `PostgresTargetEngine`. The generated OpenAPI + TS contract is regenerated. Frontend adds tiny inline metadata (index method, FK target, extension schema) directly in `SchemaSidebar`, and a new `SchemaObjectDrawer` opened by a per-row ⓘ button for the richer objects.

**Tech Stack:** .NET 10 + Npgsql (backend), React 19 + TypeScript + Mantine 8 + `@uiw/react-codemirror` (frontend), Vitest + Testing Library (frontend tests), xUnit + Aspire.Hosting.Testing (backend integration tests).

## Global Constraints

- Branch is `feat/schema-object-metadata`; never commit to `main`. Commit messages are a single subject line, no body.
- TypeScript: use `Array<T>`, never `T[]` (ESLint `@typescript-eslint/array-type`).
- Database-specific SQL stays inside `PostgresTargetEngine` — no Npgsql in domain/business code.
- All new records/fields are **additive**: do not alter existing `SchemaTree` output consumed by the ERD, MCP, or DDL export.
- `openapi.json` and `schema.ts` are generated artifacts gated by CI — regenerate and commit them; never hand-edit.
- IntegrationTests need a healthy Aspire stack that does not come up in this automated environment. Verify backend changes with `dotnet build`; the integration tests are authored here and run in CI. Do **not** block on running them locally.
- Frontend CI enforces coverage (change-threshold 80%) — every new frontend branch needs a test.

---

### Task 1: Backend — `Definition` field + introspection

**Files:**
- Modify: `src/SluiceBase.Core/Schemas/SchemaTree.cs:38-52`
- Modify: `src/SluiceBase.Api/Targets/PostgresTargetEngine.cs` (routines SQL `112-122`, add a relation-definitions query, routine reader `233-246`, view/matview/routine assembly `321-393`)
- Test: `tests/IntegrationTests/TargetEngineTests.cs`

**Interfaces:**
- Produces: `ViewInfo(Name, Columns, Definition?)`, `MaterializedViewInfo(Name, Columns, Indexes, Definition?)`, `RoutineInfo(Name, Kind, ReturnType, Language, Signature, Definition?)` — all `Definition` params are `string?` and default to `null`.

- [ ] **Step 1: Write the failing backend tests**

Add these two tests to `tests/IntegrationTests/TargetEngineTests.cs` before the closing brace (the `active_orders` view, `order_totals` matview, and `order_count` function are already seeded in the blue test database):

```csharp
    [Fact]
    public async Task TargetEngine_Postgres_GetSchema_ReturnsViewAndMatviewDefinitions()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = await factory.InitialisedApp.GetConnectionStringAsync("blue-appdb", ct);
        Assert.NotNull(connectionString);

        var tree = await _targetEngine.GetSchemaAsync(connectionString, ct);
        var pub = tree.Schemas.Single(s => s.Name == "public");

        var view = pub.Views.Single(v => v.Name == "active_orders");
        Assert.NotNull(view.Definition);
        Assert.Contains("SELECT", view.Definition!, StringComparison.OrdinalIgnoreCase);

        var matview = pub.MaterializedViews.Single(m => m.Name == "order_totals");
        Assert.NotNull(matview.Definition);
        Assert.Contains("SELECT", matview.Definition!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TargetEngine_Postgres_GetSchema_ReturnsRoutineDefinitions()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = await factory.InitialisedApp.GetConnectionStringAsync("blue-appdb", ct);
        Assert.NotNull(connectionString);

        var tree = await _targetEngine.GetSchemaAsync(connectionString, ct);
        var routines = tree.Schemas.Single(s => s.Name == "public").Routines;

        var fn = routines.Single(r => r.Name == "order_count");
        Assert.NotNull(fn.Definition);
        Assert.Contains("CREATE OR REPLACE FUNCTION", fn.Definition!, StringComparison.OrdinalIgnoreCase);
    }
```

- [ ] **Step 2: Verify the tests fail to compile**

Run: `dotnet build tests/IntegrationTests/IntegrationTests.csproj --configuration Debug`
Expected: FAIL — `'ViewInfo' does not contain a definition for 'Definition'` (and matview/routine equivalents).

- [ ] **Step 3: Add `Definition` to the domain records**

In `src/SluiceBase.Core/Schemas/SchemaTree.cs`, replace the three records:

```csharp
public sealed record ViewInfo(string Name, IReadOnlyList<ColumnInfo> Columns, string? Definition = null);

public sealed record MaterializedViewInfo(
    string Name,
    IReadOnlyList<ColumnInfo> Columns,
    IReadOnlyList<IndexInfo> Indexes,
    string? Definition = null);
```

and the routine record:

```csharp
// A function or procedure. Signature is the rendered argument list (e.g. "uid integer");
// it carries the parameter metadata directly, so no separate parameter records are needed.
// Definition is the full CREATE ... statement from pg_get_functiondef, or null if unavailable.
public sealed record RoutineInfo(
    string Name,
    string Kind,
    string? ReturnType,
    string Language,
    string Signature,
    string? Definition = null);
```

- [ ] **Step 4: Introspect definitions in `PostgresTargetEngine`**

4a. Extend the routines query (`routinesSql`, ~line 112) to select the definition as a 7th column:

```csharp
        const string routinesSql = """
                           SELECT n.nspname, p.proname, p.prokind, l.lanname,
                                  pg_get_function_result(p.oid) AS return_type,
                                  pg_get_function_arguments(p.oid) AS signature,
                                  pg_get_functiondef(p.oid) AS definition
                           FROM pg_proc p
                           JOIN pg_namespace n ON n.oid = p.pronamespace
                           JOIN pg_language l ON l.oid = p.prolang
                           WHERE p.prokind IN ('f', 'p')
                             AND n.nspname NOT IN ('information_schema', 'pg_catalog', 'pg_toast')
                           ORDER BY n.nspname, p.proname;
                           """;
```

4b. Add a new relation-definitions query. Place it just after `extensionsSql` (~line 173), before `NpgsqlDataSource.Create`:

```csharp
        // View and materialized-view definitions. pg_get_viewdef(oid, true) pretty-prints the
        // stored SELECT the same way psql's \d+ does; keyed back onto each relation by (schema,
        // name). relkind 'v' = view, 'm' = materialized view.
        const string relationDefinitionsSql = """
                           SELECT n.nspname, c.relname, pg_get_viewdef(c.oid, true)
                           FROM pg_class c
                           JOIN pg_namespace n ON n.oid = c.relnamespace
                           WHERE c.relkind IN ('v', 'm')
                             AND n.nspname NOT IN ('information_schema', 'pg_catalog', 'pg_toast')
                           ORDER BY n.nspname, c.relname;
                           """;
```

4c. Extend the routine reader tuple (~line 234) to carry the definition:

```csharp
        // Routines
        var routineRows = new List<(string Schema, string Name, char Kind, string Language, string? ReturnType, string Signature, string? Definition)>();
        await using (var command = new NpgsqlCommand(routinesSql, connection))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                routineRows.Add((
                    reader.GetString(0), reader.GetString(1), reader.GetFieldValue<char>(2),
                    reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6)));
            }
        }
```

4d. Read relation definitions into a lookup. Add this block immediately after the extensions reader block (~line 287, after its closing `}`):

```csharp
        // Relation definitions (views + materialized views), keyed by (schema, name).
        var definitionByRelation = new Dictionary<(string Schema, string Rel), string>();
        await using (var command = new NpgsqlCommand(relationDefinitionsSql, connection))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                definitionByRelation[(reader.GetString(0), reader.GetString(1))] =
                    reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
            }
        }
```

4e. Pass the definition into `RoutineInfo` construction (`routinesBySchema`, ~line 325):

```csharp
        var routinesBySchema = routineRows
            .GroupBy(r => r.Schema)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<RoutineInfo>)[.. g.Select(r => new RoutineInfo(
                    r.Name,
                    r.Kind == 'p' ? "procedure" : "function",
                    r.ReturnType,
                    r.Language,
                    r.Signature,
                    r.Definition))]);
```

4f. Pass definitions into the view/matview construction inside the relation switch (~line 378):

```csharp
                        case 'v':
                            views.Add(new ViewInfo(
                                rel.Key,
                                columns,
                                definitionByRelation.GetValueOrDefault((schemaName, rel.Key))));
                            break;
                        case 'm':
                            matViews.Add(new MaterializedViewInfo(
                                rel.Key,
                                columns,
                                indexes,
                                definitionByRelation.GetValueOrDefault((schemaName, rel.Key))));
                            break;
```

- [ ] **Step 5: Verify the solution builds**

Run: `dotnet build src/SluiceBase.Api/SluiceBase.Api.csproj --configuration Debug && dotnet build tests/IntegrationTests/IntegrationTests.csproj --configuration Debug`
Expected: PASS (0 errors). The integration tests themselves run in CI against the Aspire stack.

- [ ] **Step 6: Commit**

```bash
git add src/SluiceBase.Core/Schemas/SchemaTree.cs src/SluiceBase.Api/Targets/PostgresTargetEngine.cs tests/IntegrationTests/TargetEngineTests.cs
git commit -m "Introspect view, matview, and routine definitions"
```

---

### Task 2: Backend — regenerate the OpenAPI + TypeScript contract

**Files:**
- Modify (generated): `src/SluiceBase.Api/openapi.json`
- Modify (generated): `src/frontend/src/api/schema.ts`

**Interfaces:**
- Produces: `components["schemas"]["ViewInfo"].definition`, `["MaterializedViewInfo"].definition`, `["RoutineInfo"].definition`, each typed `null | string`, consumed by Task 4.

- [ ] **Step 1: Regenerate the OpenAPI document**

The API build target emits `openapi.json`.

Run: `dotnet build src/SluiceBase.Api/SluiceBase.Api.csproj --configuration Debug`
Then confirm the new fields landed:
Run: `git diff --unified=1 -- src/SluiceBase.Api/openapi.json`
Expected: `definition` added under `ViewInfo`, `MaterializedViewInfo`, and `RoutineInfo`.

- [ ] **Step 2: Regenerate the TypeScript types**

Run: `cd src/frontend && npm run gen:api`
Then confirm:
Run: `git diff --unified=1 -- src/frontend/src/api/schema.ts`
Expected: `definition: null | string;` added to the `ViewInfo`, `MaterializedViewInfo`, and `RoutineInfo` schema interfaces.

- [ ] **Step 3: Sanity-check the frontend still type-checks**

Run: `cd src/frontend && npm run build`
Expected: PASS — existing code compiles against the regenerated types (the new field is additive).

- [ ] **Step 4: Commit**

```bash
git add src/SluiceBase.Api/openapi.json src/frontend/src/api/schema.ts
git commit -m "Regenerate API contract for object definitions"
```

---

### Task 3: Frontend — inline metadata (index method, FK target, extension schema)

**Files:**
- Modify: `src/frontend/src/components/schema/SchemaSidebar.tsx` (`IndexRows` `326-347`, `ColumnRows` `290-322`, table call site `458-467`, extension row `566-573`)
- Test: `src/frontend/src/components/schema/__tests__/SchemaSidebar.test.tsx`

**Interfaces:**
- Consumes: table `foreignKeys` shape `{ columns: Array<string>; referencedTable: string; referencedColumns: Array<string> }` from `useSchema`.
- Produces: no new exports; behavioral only.

- [ ] **Step 1: Write the failing test assertions**

In `SchemaSidebar.test.tsx`, inside the existing `it("renders every object type with its metadata when expanded", ...)` test, add these assertions at the end of the test body (before the closing `});`):

```tsx
    // Index method is shown inline after the columns/uniqueness.
    expect(screen.getAllByText(/status · unique · btree/)[0]).toBeInTheDocument();
    // Foreign-key columns show their reference target inline.
    expect(screen.getAllByText(/→ users\.id/)[0]).toBeInTheDocument();
    // Extensions show their owning schema after the version.
    expect(screen.getAllByText(/1\.8 · public/)[0]).toBeInTheDocument();
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd src/frontend && npm run test -- SchemaSidebar`
Expected: FAIL — none of `status · unique · btree`, `→ users.id`, or `1.8 · public` match yet.

- [ ] **Step 3: Add the index method inline**

In `IndexRows`, replace the `detail` expression:

```tsx
          detail={`${ix.columns.join(", ")}${ix.isPrimary ? " · pk" : ix.isUnique ? " · unique" : ""} · ${ix.method}`}
```

- [ ] **Step 4: Add the FK target inline**

Replace the `ColumnRows` component (lines ~290-322) so it accepts the full foreign-key objects and resolves each FK column's target positionally:

```tsx
function ColumnRows({
  columns,
  depth,
  primaryKey = [],
  foreignKeys = [],
}: {
  columns: Array<Column>;
  depth: number;
  primaryKey?: Array<string>;
  foreignKeys?: Array<{ columns: Array<string>; referencedTable: string; referencedColumns: Array<string> }>;
}) {
  const pk = new Set(primaryKey);
  const fk = new Set(foreignKeys.flatMap((f) => f.columns));
  // A FK column references its parent column by position within the same constraint.
  const targetOf = (name: string): string | undefined => {
    for (const f of foreignKeys) {
      const i = f.columns.indexOf(name);
      if (i !== -1) {
        const refCol = f.referencedColumns[i] ?? f.referencedColumns[0];
        return refCol ? `${f.referencedTable}.${refCol}` : f.referencedTable;
      }
    }
    return undefined;
  };
  return (
    <>
      {columns.map((c) => {
        const role = pk.has(c.name) ? "pk" : fk.has(c.name) ? "fk" : "plain";
        const target = role === "fk" ? targetOf(c.name) : undefined;
        return (
          <TreeRow
            key={c.name}
            leaf
            depth={depth}
            name={c.name}
            detail={`${c.dataType}${c.isNullable ? " · null" : ""}${target ? ` · → ${target}` : ""}`}
            detailSuffix={sensitivityMarker(c)}
            icon={columnIcon(role)}
            faded={c.isRestricted}
          />
        );
      })}
    </>
  );
}
```

- [ ] **Step 5: Update the table call site to pass full FK objects**

In the tables branch, change the `foreignKeys` prop passed to `ColumnRows` (line ~464) from the flattened column list to the full array:

```tsx
                            <ColumnRows
                              columns={t.columns}
                              depth={3}
                              primaryKey={t.primaryKey?.columns}
                              foreignKeys={t.foreignKeys}
                            />
```

- [ ] **Step 6: Add the extension schema inline**

In the extensions map (line ~571), change the extension `detail`:

```tsx
            detail={`${e.version} · ${e.schema}`}
```

- [ ] **Step 7: Run the test to verify it passes**

Run: `cd src/frontend && npm run test -- SchemaSidebar`
Expected: PASS.

- [ ] **Step 8: Lint**

Run: `cd src/frontend && npm run lint`
Expected: PASS (no `array-type` violations — all annotations use `Array<T>`).

- [ ] **Step 9: Commit**

```bash
git add src/frontend/src/components/schema/SchemaSidebar.tsx src/frontend/src/components/schema/__tests__/SchemaSidebar.test.tsx
git commit -m "Show index method, FK targets, and extension schema inline"
```

---

### Task 4: Frontend — `SchemaObjectDrawer` + per-row ⓘ trigger

**Files:**
- Create: `src/frontend/src/components/schema/SchemaObjectDrawer.tsx`
- Modify: `src/frontend/src/components/schema/SchemaSidebar.tsx` (imports, state, object rows for view/matview/function/sequence/type, drawer render)
- Test: `src/frontend/src/components/schema/__tests__/SchemaSidebar.test.tsx`

**Interfaces:**
- Produces: `SchemaObjectDrawer` (default-styled Mantine `Drawer`) and the exported type `SchemaObjectSelection` — a discriminated union over `view | matview | function | sequence | type`, each `{ kind, schemaName, object }`.
- Consumes: object payloads straight off the `useSchema` tree; `SqlEditor` from `@/components/SqlEditor` for read-only definition rendering.

- [ ] **Step 1: Create the drawer component**

Create `src/frontend/src/components/schema/SchemaObjectDrawer.tsx`:

```tsx
import { Drawer, Group, Stack, Text } from "@mantine/core";
import type { ReactNode } from "react";
import { SqlEditor } from "@/components/SqlEditor";

interface Column {
  name: string;
  dataType: string;
  isNullable: boolean;
  isSensitive: boolean;
  isRestricted: boolean;
}
interface IndexInfo {
  name: string;
  columns: Array<string>;
  isUnique: boolean;
  isPrimary: boolean;
  method: string;
}
interface ViewMeta {
  name: string;
  columns: Array<Column>;
  definition: string | null;
}
interface MatviewMeta {
  name: string;
  columns: Array<Column>;
  indexes: Array<IndexInfo>;
  definition: string | null;
}
interface RoutineMeta {
  name: string;
  kind: string;
  returnType: string | null;
  language: string;
  signature: string;
  definition: string | null;
}
interface SequenceMeta {
  name: string;
  dataType: string;
  start: number | string;
  increment: number | string;
  minValue: number | string;
  maxValue: number | string;
  cycle: boolean;
  ownedByColumn: string | null;
}
interface TypeMeta {
  name: string;
  kind: string;
  enumLabels: Array<string> | null;
  attributes: Array<string> | null;
  baseType: string | null;
}

// Discriminated union of every object type that opens the detail drawer. Tables/views keep
// their inline treatment; only objects with metadata that doesn't fit one detail line appear here.
export type SchemaObjectSelection =
  | { kind: "view"; schemaName: string; object: ViewMeta }
  | { kind: "matview"; schemaName: string; object: MatviewMeta }
  | { kind: "function"; schemaName: string; object: RoutineMeta }
  | { kind: "sequence"; schemaName: string; object: SequenceMeta }
  | { kind: "type"; schemaName: string; object: TypeMeta };

const KIND_LABEL: Record<SchemaObjectSelection["kind"], string> = {
  view: "View",
  matview: "Materialized view",
  function: "Function",
  sequence: "Sequence",
  type: "Type",
};

// A label/value row in the metadata card. The label column is fixed-width so values align.
function Field({ label, value }: { label: string; value: ReactNode }) {
  return (
    <Group gap="md" wrap="nowrap" align="flex-start">
      <Text size="xs" c="dimmed" style={{ width: 92, flexShrink: 0 }}>
        {label}
      </Text>
      <Text size="sm" style={{ wordBreak: "break-word" }}>
        {value}
      </Text>
    </Group>
  );
}

// Read-only, syntax-highlighted SQL definition. Reuses the editor so highlighting matches the
// query editor; databaseId is null because completions are irrelevant for a static definition.
function DefinitionBlock({ sql }: { sql: string }) {
  return (
    <Stack gap={2}>
      <Text size="xs" c="dimmed">
        Definition
      </Text>
      <SqlEditor value={sql} databaseId={null} editable={false} readOnly lineNumbers={false} maxHeight="360px" />
    </Stack>
  );
}

function ColumnList({ columns }: { columns: Array<Column> }) {
  return (
    <Stack gap={2}>
      <Text size="xs" c="dimmed">
        Columns
      </Text>
      {columns.map((c) => (
        <Text key={c.name} size="sm" ff="monospace">
          {c.name}{" "}
          <Text span c="dimmed">
            {c.dataType}
          </Text>
        </Text>
      ))}
    </Stack>
  );
}

function DetailBody({ selection }: { selection: SchemaObjectSelection }) {
  if (selection.kind === "sequence") {
    const s = selection.object;
    return (
      <Stack gap="xs">
        <Field label="Type" value={s.dataType} />
        <Field label="Start" value={String(s.start)} />
        <Field label="Increment" value={String(s.increment)} />
        <Field label="Min" value={String(s.minValue)} />
        <Field label="Max" value={String(s.maxValue)} />
        <Field label="Cycle" value={s.cycle ? "yes" : "no"} />
        <Field label="Owned by" value={s.ownedByColumn ?? "—"} />
      </Stack>
    );
  }
  if (selection.kind === "type") {
    const t = selection.object;
    return (
      <Stack gap="xs">
        <Field label="Kind" value={t.kind} />
        {t.baseType ? <Field label="Base type" value={t.baseType} /> : null}
        {t.enumLabels ? <Field label="Labels" value={t.enumLabels.join(", ")} /> : null}
        {t.attributes ? (
          <Stack gap={2}>
            <Text size="xs" c="dimmed">
              Attributes
            </Text>
            {t.attributes.map((a) => (
              <Text key={a} size="sm" ff="monospace">
                {a}
              </Text>
            ))}
          </Stack>
        ) : null}
      </Stack>
    );
  }
  if (selection.kind === "function") {
    const r = selection.object;
    return (
      <Stack gap="xs">
        <Field label="Kind" value={r.kind} />
        <Field label="Language" value={r.language} />
        <Field label="Signature" value={r.signature || "—"} />
        <Field label="Returns" value={r.returnType ?? "—"} />
        {r.definition ? <DefinitionBlock sql={r.definition} /> : null}
      </Stack>
    );
  }
  // view | matview
  const o = selection.object;
  return (
    <Stack gap="sm">
      <ColumnList columns={o.columns} />
      {selection.kind === "matview" && selection.object.indexes.length > 0 ? (
        <Stack gap={2}>
          <Text size="xs" c="dimmed">
            Indexes
          </Text>
          {selection.object.indexes.map((i) => (
            <Text key={i.name} size="sm" ff="monospace">
              {i.name}{" "}
              <Text span c="dimmed">
                {i.columns.join(", ")}
              </Text>
            </Text>
          ))}
        </Stack>
      ) : null}
      {o.definition ? <DefinitionBlock sql={o.definition} /> : null}
    </Stack>
  );
}

export function SchemaObjectDrawer({
  selection,
  onClose,
}: {
  selection: SchemaObjectSelection | null;
  onClose: () => void;
}) {
  return (
    <Drawer
      opened={selection !== null}
      onClose={onClose}
      position="right"
      size="lg"
      title={
        selection ? (
          <Group gap={8} wrap="nowrap">
            <Text size="xs" c="dimmed" tt="uppercase" ff="monospace" style={{ letterSpacing: "0.06em" }}>
              {KIND_LABEL[selection.kind]}
            </Text>
            <Text fw={600}>
              {selection.schemaName}.{selection.object.name}
            </Text>
          </Group>
        ) : null
      }
    >
      {selection ? <DetailBody selection={selection} /> : null}
    </Drawer>
  );
}
```

- [ ] **Step 2: Write the failing drawer tests**

Add a new block at the end of the `describe("SchemaSidebar", ...)` in `SchemaSidebar.test.tsx` (before the final closing `});`):

```tsx
  it("opens the metadata drawer for a sequence", () => {
    seedExpanded(["schema:public", "schema:public:sequences"]);
    renderSidebar();

    fireEvent.click(screen.getByRole("button", { name: "View metadata" }));

    expect(screen.getByText(/^Sequence$/)).toBeInTheDocument();
    expect(screen.getByText("1000")).toBeInTheDocument(); // start
    expect(screen.getByText("5")).toBeInTheDocument(); // increment
  });

  it("shows composite attributes in the drawer for a type", () => {
    seedExpanded(["schema:public", "schema:public:types"]);
    renderSidebar();

    // order_status (enum) has no attributes; address (composite) does. Both rows have a button.
    const buttons = screen.getAllByRole("button", { name: "View metadata" });
    fireEvent.click(buttons[1]); // address

    expect(screen.getByText("street text")).toBeInTheDocument();
  });

  it("renders a view definition in the drawer", () => {
    seedExpanded(["schema:public", "schema:public:views"]);
    renderSidebar();

    fireEvent.click(screen.getAllByRole("button", { name: "View metadata" })[0]);

    expect(screen.getByText("Definition")).toBeInTheDocument();
  });
```

Also give the fixture definitions so the view-definition test is meaningful. In `fullTree()`, update the `views`, `materializedViews`, and `routines` entries to carry `definition`:

```tsx
        views: [
          { name: "active_orders", columns: [col("id", "integer", { isNullable: true })], definition: "SELECT id FROM orders WHERE active" },
          { name: "masked", columns: [col("x", "text", { isNullable: true, isSensitive: true })], definition: "SELECT x FROM secrets" },
        ],
        materializedViews: [
          {
            name: "order_totals",
            columns: [col("user_id", "integer", { isNullable: true })],
            indexes: [{ name: "mv_idx", columns: ["user_id"], isUnique: false, isPrimary: false, method: "btree" }],
            definition: "SELECT user_id FROM orders",
          },
        ],
        routines: [
          { name: "order_count", kind: "function", returnType: "bigint", language: "sql", signature: "uid integer", definition: "CREATE OR REPLACE FUNCTION order_count(uid integer) RETURNS bigint AS $$ $$" },
          { name: "refresh_it", kind: "procedure", returnType: null, language: "plpgsql", signature: "", definition: "CREATE OR REPLACE PROCEDURE refresh_it() AS $$ $$" },
        ],
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `cd src/frontend && npm run test -- SchemaSidebar`
Expected: FAIL — no button named "View metadata" exists yet.

- [ ] **Step 4: Wire the drawer + ⓘ button into `SchemaSidebar`**

4a. Add imports at the top of `SchemaSidebar.tsx`. Add `IconInfoCircle` to the existing `@tabler/icons-react` import, add `useState` to the existing `react` import, and add these two lines after the existing imports:

```tsx
import { SchemaObjectDrawer } from "@/components/schema/SchemaObjectDrawer";
import type { SchemaObjectSelection } from "@/components/schema/SchemaObjectDrawer";
```

4b. Add an `InfoButton` helper next to `AppendSelectButton` (after line ~376):

```tsx
// A trailing button that opens the metadata drawer for objects whose metadata doesn't fit a
// single inline detail line (views, matviews, functions, sequences, types).
function InfoButton({ onClick }: { onClick: () => void }) {
  return (
    <Tooltip label="View metadata" position="left" withArrow>
      <ActionIcon
        variant="subtle"
        color="gray"
        size="sm"
        onClick={onClick}
        aria-label="View metadata"
      >
        <IconInfoCircle size={15} />
      </ActionIcon>
    </Tooltip>
  );
}
```

4c. Add drawer selection state inside `SchemaSidebar`, right after the `expanded` state (line ~385):

```tsx
  const [selected, setSelected] = useState<SchemaObjectSelection | null>(null);
```

4d. Add the ⓘ button to each drawer-eligible row.

Views — give the view row both the info button and the existing append button by replacing its `trailing`:

```tsx
                          trailing={
                            <>
                              <InfoButton
                                onClick={() => setSelected({ kind: "view", schemaName: s.name, object: v })}
                              />
                              <AppendSelectButton
                                disabled={allSensitive}
                                onClick={() => onTableClick(s.name, v.name, v.columns)}
                              />
                            </>
                          }
```

Materialized views — add `trailing` to the matview `TreeRow` (after its `onToggle` prop, ~line 511):

```tsx
                          trailing={
                            <InfoButton
                              onClick={() => setSelected({ kind: "matview", schemaName: s.name, object: m })}
                            />
                          }
```

Functions — add `trailing` to the routine `TreeRow` (~line 532):

```tsx
                      trailing={
                        <InfoButton
                          onClick={() => setSelected({ kind: "function", schemaName: s.name, object: r })}
                        />
                      }
```

Sequences — add `trailing` to the sequence `TreeRow` (~line 544):

```tsx
                      trailing={
                        <InfoButton
                          onClick={() => setSelected({ kind: "sequence", schemaName: s.name, object: seq })}
                        />
                      }
```

Types — add `trailing` to the type `TreeRow` (~line 555):

```tsx
                      trailing={
                        <InfoButton
                          onClick={() => setSelected({ kind: "type", schemaName: s.name, object: ty })}
                        />
                      }
```

4e. Render the drawer. Replace the closing of the outer `<Stack>` (the `</Stack>` at line ~575) so the drawer is a sibling of the tree:

```tsx
      {isOpen("extensions") &&
        tree.extensions.map((e) => (
          <TreeRow
            key={e.name}
            name={e.name}
            detail={`${e.version} · ${e.schema}`}
            icon={<IconPuzzle size={14} color="var(--mantine-color-dimmed)" />}
            depth={1}
          />
        ))}

      <SchemaObjectDrawer selection={selected} onClose={() => setSelected(null)} />
    </Stack>
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `cd src/frontend && npm run test -- SchemaSidebar`
Expected: PASS.

- [ ] **Step 6: Verify the full suite, lint, and build**

Run: `cd src/frontend && npm run test && npm run lint && npm run build`
Expected: PASS on all three. (The `array-type` rule is satisfied — the new union and interfaces use `Array<T>`.)

- [ ] **Step 7: Commit**

```bash
git add src/frontend/src/components/schema/SchemaObjectDrawer.tsx src/frontend/src/components/schema/SchemaSidebar.tsx src/frontend/src/components/schema/__tests__/SchemaSidebar.test.tsx
git commit -m "Add schema object metadata drawer with per-row info trigger"
```

---

## Self-Review

**Spec coverage:**
- View/matview/function definitions → Task 1 (introspection) + Task 2 (contract) + Task 4 (drawer `DefinitionBlock`). ✓
- Sequence start/increment/min/max/cycle/owned-by → Task 4 `DetailBody` sequence branch. ✓
- Type composite attributes / domain base type → Task 4 `DetailBody` type branch. ✓
- Routine kind/language → Task 4 `DetailBody` function branch. ✓
- Index method inline → Task 3. ✓
- FK target inline → Task 3. ✓
- Extension schema inline → Task 3. ✓
- Right-side overlay drawer, ⓘ trigger, row-click still toggles expand → Task 4 (`InfoButton` is a separate trailing control; `onToggle` untouched). ✓
- `QueryPage` unchanged → Task 4 keeps all drawer state inside `SchemaSidebar`. ✓
- Additive backend, generated artifacts regenerated → Task 1 (default-null params) + Task 2. ✓
- Definition nullable / degrades gracefully → `string?`, `GetValueOrDefault`, `IsDBNull` guards in Task 1; drawer renders definition only when truthy in Task 4. ✓

**Placeholder scan:** No TBD/TODO; every code step shows complete code and every command has expected output. ✓

**Type consistency:** `SchemaObjectSelection` shape `{ kind, schemaName, object }` is defined in Task 4 Step 1 and consumed identically in Steps 4d. `Definition`/`definition` naming is consistent across Tasks 1, 2, 4. `InfoButton` aria-label `"View metadata"` matches the test queries in Task 4 Step 2. ✓

## Out of scope (carried from the spec)

- Table-level drawer, metadata editing, definitions for indexes/sequences/types/extensions, and view/matview column sensitivity (separate #137 follow-up).
