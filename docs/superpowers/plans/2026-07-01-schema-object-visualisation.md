# Schema Object Visualisation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Surface views, materialized views, functions/procedures, sequences, custom types/enums, indexes, and extensions in the schema browse sidebar, alongside the existing tables.

**Architecture:** PostgreSQL introspection switches from `information_schema` to `pg_catalog` so it can classify every relation by `relkind` and reach materialized views. New object metadata is returned as additive, strongly-typed collections on the existing `SchemaTree`. The React sidebar renders objects grouped by type. Only light metadata is carried — no definition text this phase. The ERD is untouched.

**Tech Stack:** .NET 10, Npgsql, ASP.NET Core Minimal APIs, React + TypeScript, Mantine, TanStack Query, openapi-typescript, xUnit + Aspire.Hosting.Testing (Testcontainers), Vitest.

## Global Constraints

- **Branch:** `feat/schema-object-visualisation` (already created; never commit to main).
- **Commit messages:** single subject line, no body.
- **TypeScript:** use `Array<T>`, never `T[]` (ESLint `@typescript-eslint/array-type`).
- **Preserve existing comments** unless factually wrong.
- **Postgres specifics stay in `PostgresTargetEngine`** — no Npgsql in Core/domain code.
- **Additive only:** existing `ColumnInfo`, `PrimaryKey`, `ForeignKey`, the ERD (`buildErdModel`), and the DDL export are unchanged.
- **Integration tests require the Aspire stack** (`api` resource health check fails in automated/local sessions). The local gate for backend tasks is `dotnet build`; the integration assertions in Task 5 are validated in CI. Vitest tests (Tasks 8, 9) run locally.
- **No new API routes** — `GET /api/schema/{databaseId}` already exists and is allow-listed in the AppHost YARP gateway; no gateway change needed.
- **No app-DB schema change** — no EF migration.
- `openapi.json` is emitted by `dotnet build src/SluiceBase.Api`; `schema.ts` by `npm run gen:api`. CI fails on drift in either.

---

## Task 1: Seed fixture objects for introspection tests

**Files:**
- Create: `src/AppHost/seed/blue/04-objects.sql`

**Interfaces:**
- Produces: a view `active_orders`, a materialized view `order_totals` (with index `idx_order_totals_user`), a function `order_count(integer)`, a procedure `touch_user(integer)`, a sequence `ticket_seq`, an enum type `order_status`, a composite type `address`, a table index `idx_orders_status`, and the `citext` extension — all in schema `public` of `blue-appdb`. Consumed by Task 5's assertions.

Seed files under `src/AppHost/seed/blue/` are bind-mounted into the Postgres container's `/docker-entrypoint-initdb.d` and run in filename order, after `01-init.sql` (which creates `users`, `orders`, `products`).

- [ ] **Step 1: Create the fixture SQL**

Create `src/AppHost/seed/blue/04-objects.sql`:

```sql
-- Extension (citext ships with the postgres contrib image)
CREATE EXTENSION IF NOT EXISTS citext;

-- Enum + composite custom types
CREATE TYPE order_status AS ENUM ('pending', 'shipped', 'delivered', 'cancelled');
CREATE TYPE address AS (street text, city text, postcode text);

-- Standalone sequence (not owned by a column)
CREATE SEQUENCE ticket_seq START 1000 INCREMENT 5;

-- View over an existing table
CREATE VIEW active_orders AS
SELECT id, user_id, total, status
FROM orders
WHERE status <> 'cancelled';

-- Materialized view with its own index
CREATE MATERIALIZED VIEW order_totals AS
SELECT user_id, sum(total) AS total_spent
FROM orders
GROUP BY user_id;

CREATE INDEX idx_order_totals_user ON order_totals (user_id);

-- Secondary index on a table
CREATE INDEX idx_orders_status ON orders (status);

-- Function and procedure
CREATE FUNCTION order_count(uid integer) RETURNS bigint
LANGUAGE sql AS $$ SELECT count(*) FROM orders WHERE user_id = uid $$;

CREATE PROCEDURE touch_user(uid integer)
LANGUAGE sql AS $$ UPDATE users SET created_at = now() WHERE id = uid $$;

-- Let the read role see the new relations (catalog visibility is unaffected either way)
GRANT SELECT ON active_orders TO reader_blue;
GRANT SELECT ON order_totals TO reader_blue;
```

- [ ] **Step 2: Verify the SQL parses (syntax sanity)**

Run: `grep -c "CREATE" src/AppHost/seed/blue/04-objects.sql`
Expected: `9` (one per CREATE statement).

- [ ] **Step 3: Commit**

```bash
git add src/AppHost/seed/blue/04-objects.sql
git commit -m "Seed views, matviews, routines, sequence, types, index, extension for blue test db"
```

---

## Task 2: Extend the domain model

**Files:**
- Modify: `src/SluiceBase.Core/Schemas/SchemaTree.cs`

**Interfaces:**
- Produces:
  - `SchemaTree(IReadOnlyList<SchemaInfo> Schemas, IReadOnlyList<ExtensionInfo> Extensions)`
  - `SchemaInfo(string Name, IReadOnlyList<TableInfo> Tables, IReadOnlyList<ViewInfo> Views, IReadOnlyList<MaterializedViewInfo> MaterializedViews, IReadOnlyList<RoutineInfo> Routines, IReadOnlyList<SequenceInfo> Sequences, IReadOnlyList<TypeInfo> Types)`
  - `TableInfo(string Name, IReadOnlyList<ColumnInfo> Columns, PrimaryKey? PrimaryKey, IReadOnlyList<ForeignKey> ForeignKeys, IReadOnlyList<IndexInfo> Indexes)`
  - `ViewInfo(string Name, IReadOnlyList<ColumnInfo> Columns)`
  - `MaterializedViewInfo(string Name, IReadOnlyList<ColumnInfo> Columns, IReadOnlyList<IndexInfo> Indexes)`
  - `RoutineInfo(string Name, string Kind, string? ReturnType, string Language, string Signature)` — `Kind` is `"function"` or `"procedure"`; `Signature` is the rendered argument list (this carries the parameter info as light metadata — no separate parameter records this phase).
  - `SequenceInfo(string Name, string DataType, long Start, long Increment, long MinValue, long MaxValue, bool Cycle, string? OwnedByColumn)`
  - `TypeInfo(string Name, string Kind, IReadOnlyList<string>? EnumLabels, IReadOnlyList<string>? Attributes, string? BaseType)` — `Kind` is `"enum"`, `"composite"`, `"domain"`, or `"range"`.
  - `IndexInfo(string Name, IReadOnlyList<string> Columns, bool IsUnique, bool IsPrimary, string Method)`
  - `ExtensionInfo(string Name, string Version, string Schema)`

- [ ] **Step 1: Replace the file contents**

Replace `src/SluiceBase.Core/Schemas/SchemaTree.cs` with:

```csharp
namespace SluiceBase.Core.Schemas;

public sealed record SchemaTree(
    IReadOnlyList<SchemaInfo> Schemas,
    IReadOnlyList<ExtensionInfo> Extensions);

public sealed record SchemaInfo(
    string Name,
    IReadOnlyList<TableInfo> Tables,
    IReadOnlyList<ViewInfo> Views,
    IReadOnlyList<MaterializedViewInfo> MaterializedViews,
    IReadOnlyList<RoutineInfo> Routines,
    IReadOnlyList<SequenceInfo> Sequences,
    IReadOnlyList<TypeInfo> Types);

public sealed record TableInfo(
    string Name,
    IReadOnlyList<ColumnInfo> Columns,
    PrimaryKey? PrimaryKey,
    IReadOnlyList<ForeignKey> ForeignKeys,
    IReadOnlyList<IndexInfo> Indexes);

public sealed record ColumnInfo(string Name, string DataType, bool IsNullable, bool IsSensitive = false, bool IsRestricted = false);

// A table's primary key: the ordered columns that compose it. Schema/table identity is
// implied by the owning TableInfo.
public sealed record PrimaryKey(IReadOnlyList<string> Columns);

// An outbound foreign key on the owning table. Columns are this table's columns; the
// referenced* fields point at the parent table (possibly in another schema).
public sealed record ForeignKey(
    string ConstraintName,
    IReadOnlyList<string> Columns,
    string ReferencedSchema,
    string ReferencedTable,
    IReadOnlyList<string> ReferencedColumns);

public sealed record ViewInfo(string Name, IReadOnlyList<ColumnInfo> Columns);

public sealed record MaterializedViewInfo(
    string Name,
    IReadOnlyList<ColumnInfo> Columns,
    IReadOnlyList<IndexInfo> Indexes);

// A function or procedure. Signature is the rendered argument list (e.g. "uid integer");
// it carries the parameter metadata directly, so no separate parameter records are needed.
public sealed record RoutineInfo(
    string Name,
    string Kind,
    string? ReturnType,
    string Language,
    string Signature);

public sealed record SequenceInfo(
    string Name,
    string DataType,
    long Start,
    long Increment,
    long MinValue,
    long MaxValue,
    bool Cycle,
    string? OwnedByColumn);

// A user-defined type. Kind selects which optional payload is populated: EnumLabels for
// enums, Attributes for composites, BaseType for domains; ranges carry none.
public sealed record TypeInfo(
    string Name,
    string Kind,
    IReadOnlyList<string>? EnumLabels,
    IReadOnlyList<string>? Attributes,
    string? BaseType);

public sealed record IndexInfo(
    string Name,
    IReadOnlyList<string> Columns,
    bool IsUnique,
    bool IsPrimary,
    string Method);

public sealed record ExtensionInfo(string Name, string Version, string Schema);
```

- [ ] **Step 2: Verify it does not yet build (call sites are stale)**

Run: `dotnet build src/SluiceBase.Core`
Expected: `SluiceBase.Core` builds green (it has no internal call sites). The API project will not build until Tasks 3–4; that is expected.

- [ ] **Step 3: Commit**

```bash
git add src/SluiceBase.Core/Schemas/SchemaTree.cs
git commit -m "Extend SchemaTree domain model with non-table object types"
```

---

## Task 3: Rewrite Postgres introspection

**Files:**
- Modify: `src/SluiceBase.Api/Targets/PostgresTargetEngine.cs` (replace the `GetSchemaAsync` method, lines 37–160)

**Interfaces:**
- Consumes: the records from Task 2.
- Produces: `GetSchemaAsync` returns a fully-populated `SchemaTree` (tables with indexes, views, matviews, routines, sequences, types, plus db-level extensions).

The existing `columnsSql` moves from `information_schema.columns` to `pg_catalog` so materialized views (absent from `information_schema`) are reachable and every relation can be classified by `relkind`. The `primaryKeysSql` and `foreignKeysSql` queries and their comments are unchanged.

- [ ] **Step 1: Replace the `GetSchemaAsync` method**

In `src/SluiceBase.Api/Targets/PostgresTargetEngine.cs`, replace the entire `GetSchemaAsync` method (from `public async Task<SchemaTree> GetSchemaAsync` through its closing brace before `ExportSchemaDdlAsync`) with:

```csharp
    public async Task<SchemaTree> GetSchemaAsync(string connectionString, CancellationToken ct)
    {
        // Columns come from pg_catalog rather than information_schema.columns: materialized
        // views are absent from information_schema entirely, and relkind lets us classify each
        // relation (table / view / matview) in one pass. pg_catalog is also not privilege-gated
        // for read-only roles the way information_schema is.
        const string columnsSql = """
                           SELECT n.nspname, c.relname, c.relkind,
                                  a.attname, format_type(a.atttypid, a.atttypmod) AS data_type,
                                  NOT a.attnotnull AS is_nullable
                           FROM pg_attribute a
                           JOIN pg_class c ON c.oid = a.attrelid
                           JOIN pg_namespace n ON n.oid = c.relnamespace
                           WHERE a.attnum > 0
                             AND NOT a.attisdropped
                             AND c.relkind IN ('r', 'p', 'f', 'v', 'm')
                             AND n.nspname NOT IN ('information_schema', 'pg_catalog', 'pg_toast')
                           ORDER BY n.nspname, c.relname, a.attnum;
                           """;

        // Constraints are read from pg_catalog, not information_schema: information_schema's
        // table_constraints / referential_constraints views only expose constraints on tables
        // where the current role has a privilege OTHER THAN SELECT, so the read-only credential
        // used for introspection sees none of them. pg_catalog is not privilege-gated this way.
        const string primaryKeysSql = """
                           SELECT n.nspname, c.relname, a.attname
                           FROM pg_constraint con
                           JOIN pg_class c ON c.oid = con.conrelid
                           JOIN pg_namespace n ON n.oid = c.relnamespace
                           JOIN pg_attribute a ON a.attrelid = c.oid AND a.attnum = ANY (con.conkey)
                           WHERE con.contype = 'p'
                             AND n.nspname NOT IN ('information_schema', 'pg_catalog', 'pg_toast')
                           ORDER BY n.nspname, c.relname, array_position(con.conkey, a.attnum);
                           """;

        // unnest(conkey, confkey) WITH ORDINALITY pairs each FK column with its referenced
        // column by position, so composite foreign keys map correctly.
        const string foreignKeysSql = """
                           SELECT
                               con.conname,
                               n.nspname, c.relname, att.attname,
                               rn.nspname AS ref_schema,
                               rc.relname AS ref_table,
                               ratt.attname AS ref_column
                           FROM pg_constraint con
                           JOIN pg_class c ON c.oid = con.conrelid
                           JOIN pg_namespace n ON n.oid = c.relnamespace
                           JOIN pg_class rc ON rc.oid = con.confrelid
                           JOIN pg_namespace rn ON rn.oid = rc.relnamespace
                           JOIN LATERAL unnest(con.conkey, con.confkey) WITH ORDINALITY AS k(conkey, confkey, ord) ON true
                           JOIN pg_attribute att ON att.attrelid = con.conrelid AND att.attnum = k.conkey
                           JOIN pg_attribute ratt ON ratt.attrelid = con.confrelid AND ratt.attnum = k.confkey
                           WHERE con.contype = 'f'
                             AND n.nspname NOT IN ('information_schema', 'pg_catalog', 'pg_toast')
                           ORDER BY con.conname, k.ord;
                           """;

        // Index columns come from indkey; expression indexes have attnum 0, so those slots
        // resolve to NULL and are rendered as "(expression)".
        const string indexesSql = """
                           SELECT n.nspname, t.relname, i.relname,
                                  ix.indisunique, ix.indisprimary, am.amname,
                                  a.attname
                           FROM pg_index ix
                           JOIN pg_class i ON i.oid = ix.indexrelid
                           JOIN pg_class t ON t.oid = ix.indrelid
                           JOIN pg_namespace n ON n.oid = t.relnamespace
                           JOIN pg_am am ON am.oid = i.relam
                           JOIN LATERAL unnest(ix.indkey) WITH ORDINALITY AS k(attnum, ord) ON true
                           LEFT JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = k.attnum
                           WHERE t.relkind IN ('r', 'p', 'm')
                             AND n.nspname NOT IN ('information_schema', 'pg_catalog', 'pg_toast')
                           ORDER BY n.nspname, t.relname, i.relname, k.ord;
                           """;

        const string routinesSql = """
                           SELECT n.nspname, p.proname, p.prokind, l.lanname,
                                  pg_get_function_result(p.oid) AS return_type,
                                  pg_get_function_arguments(p.oid) AS signature
                           FROM pg_proc p
                           JOIN pg_namespace n ON n.oid = p.pronamespace
                           JOIN pg_language l ON l.oid = p.prolang
                           WHERE p.prokind IN ('f', 'p')
                             AND n.nspname NOT IN ('information_schema', 'pg_catalog', 'pg_toast')
                           ORDER BY n.nspname, p.proname;
                           """;

        // deptype 'a' (auto) links a sequence to the column that owns it, if any.
        const string sequencesSql = """
                           SELECT s.schemaname, s.sequencename, s.data_type::text,
                                  s.start_value, s.increment_by, s.min_value, s.max_value, s.cycle,
                                  ownr.owned_by
                           FROM pg_sequences s
                           LEFT JOIN LATERAL (
                               SELECT tn.nspname || '.' || tc.relname || '.' || a.attname AS owned_by
                               FROM pg_depend d
                               JOIN pg_class sc ON sc.oid = d.objid AND sc.relkind = 'S'
                               JOIN pg_namespace sn ON sn.oid = sc.relnamespace
                               JOIN pg_class tc ON tc.oid = d.refobjid
                               JOIN pg_namespace tn ON tn.oid = tc.relnamespace
                               JOIN pg_attribute a ON a.attrelid = d.refobjid AND a.attnum = d.refobjsubid
                               WHERE sn.nspname = s.schemaname AND sc.relname = s.sequencename AND d.deptype = 'a'
                               LIMIT 1
                           ) ownr ON true
                           WHERE s.schemaname NOT IN ('information_schema', 'pg_catalog', 'pg_toast')
                           ORDER BY s.schemaname, s.sequencename;
                           """;

        // typtype: e=enum, c=composite, d=domain, r=range. The relkind <> 'c' guard drops the
        // row-type composites that back every table/view, keeping only standalone CREATE TYPEs.
        const string typesSql = """
                           SELECT n.nspname, t.typname, t.typtype,
                                  CASE WHEN t.typtype = 'e' THEN
                                      (SELECT array_agg(e.enumlabel ORDER BY e.enumsortorder)
                                       FROM pg_enum e WHERE e.enumtypid = t.oid)
                                  END AS enum_labels,
                                  CASE WHEN t.typtype = 'c' THEN
                                      (SELECT array_agg(a.attname || ' ' || format_type(a.atttypid, a.atttypmod) ORDER BY a.attnum)
                                       FROM pg_attribute a
                                       WHERE a.attrelid = t.typrelid AND a.attnum > 0 AND NOT a.attisdropped)
                                  END AS attributes,
                                  CASE WHEN t.typtype = 'd' THEN format_type(t.typbasetype, t.typtypmod) END AS base_type
                           FROM pg_type t
                           JOIN pg_namespace n ON n.oid = t.typnamespace
                           WHERE t.typtype IN ('e', 'c', 'd', 'r')
                             AND NOT (t.typtype = 'c'
                                      AND EXISTS (SELECT 1 FROM pg_class c WHERE c.oid = t.typrelid AND c.relkind <> 'c'))
                             AND n.nspname NOT IN ('information_schema', 'pg_catalog', 'pg_toast')
                           ORDER BY n.nspname, t.typname;
                           """;

        const string extensionsSql = """
                           SELECT e.extname, e.extversion, n.nspname
                           FROM pg_extension e
                           JOIN pg_namespace n ON n.oid = e.extnamespace
                           ORDER BY e.extname;
                           """;

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var connection = await dataSource.OpenConnectionAsync(ct);

        // Columns (with relkind for classification)
        var columnRows = new List<(string Schema, string Rel, char Kind, string Column, string DataType, bool IsNullable)>();
        await using (var command = new NpgsqlCommand(columnsSql, connection))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                columnRows.Add((
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetFieldValue<char>(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetBoolean(5)));
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

        // Indexes
        var indexRows = new List<(string Schema, string Rel, string Index, bool IsUnique, bool IsPrimary, string Method, string? Column)>();
        await using (var command = new NpgsqlCommand(indexesSql, connection))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                indexRows.Add((
                    reader.GetString(0), reader.GetString(1), reader.GetString(2),
                    reader.GetBoolean(3), reader.GetBoolean(4), reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6)));
            }
        }

        // Routines
        var routineRows = new List<(string Schema, string Name, char Kind, string Language, string? ReturnType, string Signature)>();
        await using (var command = new NpgsqlCommand(routinesSql, connection))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                routineRows.Add((
                    reader.GetString(0), reader.GetString(1), reader.GetFieldValue<char>(2),
                    reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.GetString(5)));
            }
        }

        // Sequences
        var sequenceRows = new List<(string Schema, string Name, string DataType, long Start, long Increment, long MinValue, long MaxValue, bool Cycle, string? OwnedBy)>();
        await using (var command = new NpgsqlCommand(sequencesSql, connection))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                sequenceRows.Add((
                    reader.GetString(0), reader.GetString(1), reader.GetString(2),
                    reader.GetInt64(3), reader.GetInt64(4), reader.GetInt64(5), reader.GetInt64(6),
                    reader.GetBoolean(7),
                    reader.IsDBNull(8) ? null : reader.GetString(8)));
            }
        }

        // Types
        var typeRows = new List<(string Schema, string Name, char TypType, string[]? EnumLabels, string[]? Attributes, string? BaseType)>();
        await using (var command = new NpgsqlCommand(typesSql, connection))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                typeRows.Add((
                    reader.GetString(0), reader.GetString(1), reader.GetFieldValue<char>(2),
                    reader.IsDBNull(3) ? null : reader.GetFieldValue<string[]>(3),
                    reader.IsDBNull(4) ? null : reader.GetFieldValue<string[]>(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5)));
            }
        }

        // Extensions (database-level)
        var extensions = new List<ExtensionInfo>();
        await using (var command = new NpgsqlCommand(extensionsSql, connection))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                extensions.Add(new ExtensionInfo(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
            }
        }

        var primaryKeyByTable = pkRows
            .GroupBy(r => (r.Schema, r.Table))
            .ToDictionary(g => g.Key, g => new PrimaryKey([.. g.Select(r => r.Column)]));

        var foreignKeysByTable = fkRows
            .GroupBy(r => r.Constraint)
            .Select(g => (
                Owner: (g.First().Schema, g.First().Table),
                ForeignKey: new ForeignKey(
                    g.Key,
                    [.. g.Select(r => r.Column)],
                    g.First().RefSchema,
                    g.First().RefTable,
                    [.. g.Select(r => r.RefColumn)])))
            .GroupBy(x => x.Owner)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<ForeignKey>)[.. g.Select(x => x.ForeignKey)]);

        var indexesByRel = indexRows
            .GroupBy(r => (r.Schema, r.Rel))
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<IndexInfo>)[.. g
                    .GroupBy(r => r.Index)
                    .Select(ig => new IndexInfo(
                        ig.Key,
                        [.. ig.Select(x => x.Column ?? "(expression)")],
                        ig.First().IsUnique,
                        ig.First().IsPrimary,
                        ig.First().Method))]);

        var routinesBySchema = routineRows
            .GroupBy(r => r.Schema)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<RoutineInfo>)[.. g.Select(r => new RoutineInfo(
                    r.Name,
                    r.Kind == 'p' ? "procedure" : "function",
                    r.ReturnType,
                    r.Language,
                    r.Signature))]);

        var sequencesBySchema = sequenceRows
            .GroupBy(r => r.Schema)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<SequenceInfo>)[.. g.Select(r => new SequenceInfo(
                    r.Name, r.DataType, r.Start, r.Increment, r.MinValue, r.MaxValue, r.Cycle, r.OwnedBy))]);

        var typesBySchema = typeRows
            .GroupBy(r => r.Schema)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<TypeInfo>)[.. g.Select(r => new TypeInfo(
                    r.Name,
                    r.TypType switch { 'e' => "enum", 'c' => "composite", 'd' => "domain", _ => "range" },
                    r.EnumLabels,
                    r.Attributes,
                    r.BaseType))]);

        // Every schema that owns any object, whether or not it has relations with columns.
        var schemaNames = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var r in columnRows) schemaNames.Add(r.Schema);
        foreach (var r in routineRows) schemaNames.Add(r.Schema);
        foreach (var r in sequenceRows) schemaNames.Add(r.Schema);
        foreach (var r in typeRows) schemaNames.Add(r.Schema);

        var columnsBySchema = columnRows
            .GroupBy(r => r.Schema)
            .ToDictionary(g => g.Key, g => g.ToList());

        var schemas = new List<SchemaInfo>();
        foreach (var schemaName in schemaNames)
        {
            var tables = new List<TableInfo>();
            var views = new List<ViewInfo>();
            var matViews = new List<MaterializedViewInfo>();

            if (columnsBySchema.TryGetValue(schemaName, out var schemaColumns))
            {
                foreach (var rel in schemaColumns.GroupBy(r => r.Rel))
                {
                    var columns = rel.Select(c => new ColumnInfo(c.Column, c.DataType, c.IsNullable)).ToList();
                    var kind = rel.First().Kind;
                    var indexes = indexesByRel.GetValueOrDefault((schemaName, rel.Key), []);

                    switch (kind)
                    {
                        case 'v':
                            views.Add(new ViewInfo(rel.Key, columns));
                            break;
                        case 'm':
                            matViews.Add(new MaterializedViewInfo(rel.Key, columns, indexes));
                            break;
                        default: // 'r', 'p', 'f'
                            tables.Add(new TableInfo(
                                rel.Key,
                                columns,
                                primaryKeyByTable.GetValueOrDefault((schemaName, rel.Key)),
                                foreignKeysByTable.GetValueOrDefault((schemaName, rel.Key), []),
                                indexes));
                            break;
                    }
                }
            }

            schemas.Add(new SchemaInfo(
                schemaName,
                tables,
                views,
                matViews,
                routinesBySchema.GetValueOrDefault(schemaName, []),
                sequencesBySchema.GetValueOrDefault(schemaName, []),
                typesBySchema.GetValueOrDefault(schemaName, [])));
        }

        return new SchemaTree(schemas, extensions);
    }
```

- [ ] **Step 2: Verify the engine compiles**

Run: `dotnet build src/SluiceBase.Api`
Expected: `PostgresTargetEngine` compiles. Any remaining error will be in `SchemaService` (fixed in Task 4).

- [ ] **Step 3: Commit**

```bash
git add src/SluiceBase.Api/Targets/PostgresTargetEngine.cs
git commit -m "Introspect views, matviews, routines, sequences, types, indexes, extensions in Postgres engine"
```

---

## Task 4: Carry new collections through schema annotation

**Files:**
- Modify: `src/SluiceBase.Api/Services/ISchemaService.cs` (the annotation branch of `GetAnnotatedSchemaAsync`)

**Interfaces:**
- Consumes: the enriched `SchemaTree` from Task 3.
- Produces: annotated `SchemaTree` where table columns are marked sensitive/restricted and all new collections (views, matviews, routines, sequences, types, extensions, table indexes) pass through unchanged.

The no-sensitive-columns early return already returns the full tree untouched. Only the annotation branch rebuilds the tree and must be updated so it does not drop the new data.

- [ ] **Step 1: Update the annotation reconstruction**

In `src/SluiceBase.Api/Services/ISchemaService.cs`, replace the `annotatedSchemas` assignment and the `return new SchemaResult(SchemaOutcome.Ok, new SchemaTree(annotatedSchemas), null);` line with:

```csharp
            var annotatedSchemas = tree.Schemas.Select(s =>
                new SchemaInfo(s.Name,
                    s.Tables.Select(t =>
                        new TableInfo(t.Name,
                            t.Columns.Select(c =>
                            {
                                var key = (s.Name.ToLowerInvariant(), t.Name.ToLowerInvariant(), c.Name.ToLowerInvariant());
                                return new ColumnInfo(
                                    c.Name, c.DataType, c.IsNullable,
                                    sensitiveKeys.Contains(key),
                                    restrictedKeys.Contains(key));
                            }).ToList(),
                            t.PrimaryKey,
                            t.ForeignKeys,
                            t.Indexes
                        )).ToList(),
                    s.Views,
                    s.MaterializedViews,
                    s.Routines,
                    s.Sequences,
                    s.Types
                )).ToList();

            return new SchemaResult(SchemaOutcome.Ok, new SchemaTree(annotatedSchemas, tree.Extensions), null);
```

- [ ] **Step 2: Verify the whole solution builds**

Run: `dotnet build`
Expected: BUILD SUCCEEDED across all projects.

- [ ] **Step 3: Commit**

```bash
git add src/SluiceBase.Api/Services/ISchemaService.cs
git commit -m "Pass new schema object collections through sensitive-column annotation"
```

---

## Task 5: Integration tests for object introspection

**Files:**
- Modify: `tests/IntegrationTests/TargetEngineTests.cs`

**Interfaces:**
- Consumes: `PostgresTargetEngine.GetSchemaAsync`, the `blue-appdb` fixtures from Task 1.

> **CI note:** these tests need the Aspire stack (Testcontainers Postgres) and are validated in CI. Locally, `dotnet build tests/IntegrationTests` is the gate; do not expect the assertions to run without the stack.

- [ ] **Step 1: Add the tests**

In `tests/IntegrationTests/TargetEngineTests.cs`, add these methods to the `TargetEngineTests` class:

```csharp
    [Fact]
    public async Task TargetEngine_Postgres_GetSchema_ClassifiesViewsAndMatviews()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = await factory.InitialisedApp.GetConnectionStringAsync("blue-appdb", ct);
        Assert.NotNull(connectionString);

        var tree = await _targetEngine.GetSchemaAsync(connectionString, ct);
        var pub = tree.Schemas.Single(s => s.Name == "public");

        // Regression: tables still present and views/matviews are NOT in the tables list.
        Assert.Contains(pub.Tables, t => t.Name == "orders");
        Assert.DoesNotContain(pub.Tables, t => t.Name == "active_orders");
        Assert.DoesNotContain(pub.Tables, t => t.Name == "order_totals");

        var view = Assert.Single(pub.Views, v => v.Name == "active_orders");
        Assert.NotEmpty(view.Columns);

        var matview = Assert.Single(pub.MaterializedViews, m => m.Name == "order_totals");
        Assert.Contains(matview.Indexes, i => i.Name == "idx_order_totals_user");
    }

    [Fact]
    public async Task TargetEngine_Postgres_GetSchema_ReturnsTableIndexes()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = await factory.InitialisedApp.GetConnectionStringAsync("blue-appdb", ct);
        Assert.NotNull(connectionString);

        var tree = await _targetEngine.GetSchemaAsync(connectionString, ct);
        var orders = tree.Schemas.Single(s => s.Name == "public").Tables.Single(t => t.Name == "orders");

        Assert.Contains(orders.Indexes, i => i.Name == "idx_orders_status");
        Assert.Contains(orders.Indexes, i => i.IsPrimary);
    }

    [Fact]
    public async Task TargetEngine_Postgres_GetSchema_ReturnsRoutines()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = await factory.InitialisedApp.GetConnectionStringAsync("blue-appdb", ct);
        Assert.NotNull(connectionString);

        var tree = await _targetEngine.GetSchemaAsync(connectionString, ct);
        var routines = tree.Schemas.Single(s => s.Name == "public").Routines;

        var fn = Assert.Single(routines, r => r.Name == "order_count");
        Assert.Equal("function", fn.Kind);
        Assert.Equal("bigint", fn.ReturnType);

        Assert.Single(routines, r => r.Name == "touch_user" && r.Kind == "procedure");
    }

    [Fact]
    public async Task TargetEngine_Postgres_GetSchema_ReturnsSequencesTypesAndExtensions()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = await factory.InitialisedApp.GetConnectionStringAsync("blue-appdb", ct);
        Assert.NotNull(connectionString);

        var tree = await _targetEngine.GetSchemaAsync(connectionString, ct);
        var pub = tree.Schemas.Single(s => s.Name == "public");

        Assert.Single(pub.Sequences, s => s.Name == "ticket_seq" && s.Increment == 5);

        var enumType = Assert.Single(pub.Types, t => t.Name == "order_status");
        Assert.Equal("enum", enumType.Kind);
        Assert.NotNull(enumType.EnumLabels);
        Assert.Contains("shipped", enumType.EnumLabels!);
        Assert.Single(pub.Types, t => t.Name == "address" && t.Kind == "composite");

        Assert.Contains(tree.Extensions, e => e.Name == "citext");
    }
```

- [ ] **Step 2: Verify the test project builds**

Run: `dotnet build tests/IntegrationTests`
Expected: BUILD SUCCEEDED. (Execution happens in CI per the note above.)

- [ ] **Step 3: Commit**

```bash
git add tests/IntegrationTests/TargetEngineTests.cs
git commit -m "Add introspection integration tests for new schema object types"
```

---

## Task 6: Regenerate the OpenAPI document

**Files:**
- Modify: `src/SluiceBase.Api/openapi.json` (generated)

**Interfaces:**
- Produces: the enriched `SchemaTree`/`SchemaInfo`/`TableInfo` plus new `ViewInfo`, `MaterializedViewInfo`, `RoutineInfo`, `SequenceInfo`, `TypeInfo`, `IndexInfo`, `ExtensionInfo` component schemas — consumed by Task 7.

- [ ] **Step 1: Rebuild to regenerate the document**

Run: `dotnet build src/SluiceBase.Api/SluiceBase.Api.csproj --configuration Debug`
Expected: BUILD SUCCEEDED; `src/SluiceBase.Api/openapi.json` is updated on disk.

- [ ] **Step 2: Confirm the new schemas landed**

Run: `grep -c "MaterializedViewInfo\|RoutineInfo\|SequenceInfo\|ExtensionInfo\|IndexInfo" src/SluiceBase.Api/openapi.json`
Expected: a non-zero count (at least 5).

- [ ] **Step 3: Commit**

```bash
git add src/SluiceBase.Api/openapi.json
git commit -m "Regenerate OpenAPI document for schema object types"
```

---

## Task 7: Regenerate the frontend API types

**Files:**
- Modify: `src/frontend/src/api/schema.ts` (generated)

**Interfaces:**
- Consumes: `openapi.json` from Task 6.
- Produces: TypeScript types under `paths["/api/schema/{databaseId}"]...` carrying `views`, `materializedViews`, `routines`, `sequences`, `types`, table `indexes`, and top-level `extensions`.

- [ ] **Step 1: Regenerate**

Run: `cd src/frontend && npm run gen:api`
Expected: `src/api/schema.ts` rewritten with no errors.

- [ ] **Step 2: Confirm the new fields exist**

Run: `grep -c "MaterializedViewInfo\|RoutineInfo\|ExtensionInfo" src/frontend/src/api/schema.ts`
Expected: non-zero.

- [ ] **Step 3: Commit**

```bash
git add src/frontend/src/api/schema.ts
git commit -m "Regenerate frontend API types for schema object types"
```

---

## Task 8: Keep views in SQL autocomplete

**Files:**
- Modify: `src/frontend/src/api/hooks.ts` (`schemaToCompletions`, around line 333)
- Test: `src/frontend/src/api/__tests__/schema-hooks.test.ts`

**Interfaces:**
- Consumes: the enriched schema tree type.
- Produces: `schemaToCompletions` returns a `Record<string, Array<string>>` keyed by `schema.name` including both tables **and** views (views left the `tables` list when introspection moved to `pg_catalog`).

- [ ] **Step 1: Write the failing test**

In `src/frontend/src/api/__tests__/schema-hooks.test.ts`, add inside the existing top-level `describe` (or as a new `describe`):

```ts
describe("schemaToCompletions with views", () => {
  it("includes view columns alongside table columns", async () => {
    const { schemaToCompletions } = await import("@/api/hooks");
    const tree = {
      schemas: [
        {
          name: "public",
          tables: [{ name: "orders", columns: [{ name: "id", isRestricted: false }] }],
          views: [{ name: "active_orders", columns: [{ name: "id", isRestricted: false }] }],
        },
      ],
    };

    const result = schemaToCompletions(tree);

    expect(result["public.orders"]).toEqual(["id"]);
    expect(result["public.active_orders"]).toEqual(["id"]);
  });
});
```

- [ ] **Step 2: Run it and watch it fail**

Run: `cd src/frontend && npx vitest run src/api/__tests__/schema-hooks.test.ts -t "includes view columns"`
Expected: FAIL — `result["public.active_orders"]` is `undefined`.

- [ ] **Step 3: Update `schemaToCompletions`**

In `src/frontend/src/api/hooks.ts`, replace the `schemaToCompletions` function with:

```ts
export function schemaToCompletions(
  tree: {
    schemas: Array<{
      name: string;
      tables: Array<{
        name: string;
        columns: Array<{ name: string; isRestricted: boolean }>;
      }>;
      views?: Array<{
        name: string;
        columns: Array<{ name: string; isRestricted: boolean }>;
      }>;
    }>;
  },
): Record<string, Array<string>> {
  const result: Record<string, Array<string>> = {};
  for (const schema of tree.schemas) {
    const relations = [...schema.tables, ...(schema.views ?? [])];
    for (const relation of relations) {
      result[`${schema.name}.${relation.name}`] = relation.columns
        .filter((c) => !c.isRestricted)
        .map((c) => c.name);
    }
  }
  return result;
}
```

- [ ] **Step 4: Run the test and the full suite**

Run: `cd src/frontend && npx vitest run src/api/__tests__/schema-hooks.test.ts`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/frontend/src/api/hooks.ts src/frontend/src/api/__tests__/schema-hooks.test.ts
git commit -m "Include views in SQL autocomplete completions"
```

---

## Task 9: Render objects grouped by type in the sidebar

**Files:**
- Create: `src/frontend/src/components/schema/SchemaSidebar.tsx`
- Create: `src/frontend/src/components/schema/__tests__/SchemaSidebar.test.tsx`
- Modify: `src/frontend/src/routes/_authed/query/index.tsx` (remove the inline `SchemaSidebar` + `OverflowTooltip`, import the new component)

**Interfaces:**
- Consumes: `useSchema` return type, the `TableClickHandler` signature `(schemaName: string, tableName: string, columns: Array<{ name: string; isSensitive: boolean; isRestricted: boolean }>) => void`.
- Produces: `export function SchemaSidebar({ schema, onTableClick }: { schema: ReturnType<typeof useSchema>; onTableClick: TableClickHandler })`.

The current `SchemaSidebar` and its `OverflowTooltip` helper live inline in `query/index.tsx` and only serve the sidebar; move both into the new focused component file and extend the render to group objects by type. Tables and views wire `onTableClick` (both are selectable); other object types are display-only. Icons: `IconTable` (tables), `IconEye` (views), `IconStack2` (materialized views), `IconMathFunction` (routines), `IconListNumbers` (sequences), `IconBraces` (types), `IconKey` (indexes), `IconPuzzle` (extensions).

- [ ] **Step 1: Write the failing render test**

Create `src/frontend/src/components/schema/__tests__/SchemaSidebar.test.tsx`:

```tsx
import { render, screen, fireEvent } from "@testing-library/react";
import { MantineProvider } from "@mantine/core";
import { describe, expect, it } from "vitest";
import { SchemaSidebar } from "@/components/schema/SchemaSidebar";

function makeSchema() {
  return {
    isLoading: false,
    isError: false,
    data: {
      extensions: [{ name: "citext", version: "1.6", schema: "public" }],
      schemas: [
        {
          name: "public",
          tables: [
            {
              name: "orders",
              columns: [{ name: "id", dataType: "integer", isNullable: false, isSensitive: false, isRestricted: false }],
              primaryKey: { columns: ["id"] },
              foreignKeys: [],
              indexes: [{ name: "idx_orders_status", columns: ["status"], isUnique: false, isPrimary: false, method: "btree" }],
            },
          ],
          views: [{ name: "active_orders", columns: [{ name: "id", dataType: "integer", isNullable: false, isSensitive: false, isRestricted: false }] }],
          materializedViews: [{ name: "order_totals", columns: [], indexes: [] }],
          routines: [{ name: "order_count", kind: "function", returnType: "bigint", language: "sql", signature: "uid integer" }],
          sequences: [{ name: "ticket_seq", dataType: "bigint", start: 1000, increment: 5, minValue: 1, maxValue: 9223372036854775807, cycle: false, ownedByColumn: null }],
          types: [{ name: "order_status", kind: "enum", enumLabels: ["pending", "shipped"], attributes: null, baseType: null }],
        },
      ],
    },
  } as unknown as Parameters<typeof SchemaSidebar>[0]["schema"];
}

function renderSidebar() {
  render(
    <MantineProvider>
      <SchemaSidebar schema={makeSchema()} onTableClick={() => {}} />
    </MantineProvider>,
  );
}

describe("SchemaSidebar", () => {
  it("shows grouped object folders after expanding the schema", () => {
    renderSidebar();
    fireEvent.click(screen.getByText("public"));

    expect(screen.getByText(/Tables/)).toBeInTheDocument();
    expect(screen.getByText(/Views/)).toBeInTheDocument();
    expect(screen.getByText(/Materialized Views/)).toBeInTheDocument();
    expect(screen.getByText(/Functions/)).toBeInTheDocument();
    expect(screen.getByText(/Sequences/)).toBeInTheDocument();
    expect(screen.getByText(/Types/)).toBeInTheDocument();
  });

  it("lists extensions at the database level", () => {
    renderSidebar();
    expect(screen.getByText(/Extensions/)).toBeInTheDocument();
    expect(screen.getByText("citext")).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run it and watch it fail**

Run: `cd src/frontend && npx vitest run src/components/schema/__tests__/SchemaSidebar.test.tsx`
Expected: FAIL — module `@/components/schema/SchemaSidebar` does not exist.

- [ ] **Step 3: Create the component**

Create `src/frontend/src/components/schema/SchemaSidebar.tsx`:

```tsx
import {
  ActionIcon,
  Alert,
  Box,
  Button,
  Code,
  Flex,
  Group,
  NavLink,
  Stack,
  Skeleton,
  Text,
  Tooltip,
} from "@mantine/core";
import {
  IconBraces,
  IconChevronDown,
  IconChevronRight,
  IconDatabase,
  IconEye,
  IconKey,
  IconListNumbers,
  IconLock,
  IconMathFunction,
  IconPlaylistAdd,
  IconPuzzle,
  IconShieldLock,
  IconStack2,
  IconTable,
} from "@tabler/icons-react";
import { cloneElement, useRef, useState } from "react";
import type { CSSProperties, MouseEvent, ReactElement, ReactNode, Ref } from "react";
import type { useSchema } from "@/api/hooks";
import { useSessionState } from "@/utils/useSessionState";

export type TableClickHandler = (
  schemaName: string,
  tableName: string,
  columns: Array<{ name: string; isSensitive: boolean; isRestricted: boolean }>,
) => void;

// Shows a floating tooltip with the full label, but only while the wrapped element is wider
// than the visible (scrollable) panel area. The names sit in a max-content container, so they
// never self-truncate — overflow has to be measured against the surrounding scroll viewport.
function OverflowTooltip({
  label,
  children,
}: {
  label: string;
  children: ReactElement<{
    ref?: Ref<HTMLElement>;
    onMouseEnter?: (event: MouseEvent<HTMLElement>) => void;
  }>;
}) {
  const ref = useRef<HTMLElement>(null);
  const [disabled, setDisabled] = useState(true);

  const handleMouseEnter = (event: MouseEvent<HTMLElement>) => {
    const el = ref.current;
    if (el) {
      const scroller = el.closest<HTMLElement>("[data-schema-scroll]");
      const available = scroller?.clientWidth ?? el.clientWidth;
      setDisabled(Math.ceil(el.getBoundingClientRect().width) <= available);
    }
    children.props.onMouseEnter?.(event);
  };

  return (
    <Tooltip.Floating label={label} disabled={disabled}>
      {cloneElement(children, { ref, onMouseEnter: handleMouseEnter })}
    </Tooltip.Floating>
  );
}

// Pins the chevron / action controls to the right edge of the scroll viewport so they stay
// visible at the panel width while long names scroll underneath them.
const stickyRight: CSSProperties = {
  position: "sticky",
  right: 0,
  marginLeft: "auto",
  flexShrink: 0,
  alignItems: "center",
  background: "var(--mantine-color-body)",
};

function Row({
  label,
  icon,
  indent,
  onClick,
  right,
}: {
  label: string;
  icon: ReactNode;
  indent?: string | number;
  onClick?: () => void;
  right?: ReactNode;
}) {
  return (
    <Flex wrap="nowrap" align="center" w="100%">
      <OverflowTooltip label={label}>
        <NavLink
          label={label}
          leftSection={icon}
          onClick={onClick}
          pl={indent}
          active={false}
          style={{ width: "max-content", flexShrink: 0 }}
          styles={{ label: { whiteSpace: "nowrap" } }}
        />
      </OverflowTooltip>
      {right ? (
        <Group gap={2} wrap="nowrap" pl={4} style={stickyRight}>
          {right}
        </Group>
      ) : null}
    </Flex>
  );
}

// A collapsible "folder" grouping objects of one kind under a schema. Rendered only when the
// group is non-empty so schemas stay tidy.
function Group_({
  id,
  title,
  count,
  expanded,
  onToggle,
  children,
}: {
  id: string;
  title: string;
  count: number;
  expanded: Array<string>;
  onToggle: (id: string) => void;
  children: ReactNode;
}) {
  if (count === 0) return null;
  const isOpen = expanded.includes(id);
  return (
    <div>
      <Flex wrap="nowrap" align="center" w="100%">
        <NavLink
          label={`${title} (${count})`}
          leftSection={isOpen ? <IconChevronDown size={12} /> : <IconChevronRight size={12} />}
          onClick={() => onToggle(id)}
          pl="lg"
          active={false}
          style={{ width: "max-content", flexShrink: 0 }}
          styles={{ label: { whiteSpace: "nowrap", fontWeight: 600 } }}
        />
      </Flex>
      {isOpen ? <Box>{children}</Box> : null}
    </div>
  );
}

function ColumnList({
  columns,
}: {
  columns: Array<{ name: string; dataType: string; isNullable: boolean; isSensitive: boolean; isRestricted: boolean }>;
}) {
  return (
    <Stack gap={0} pl="calc(var(--mantine-spacing-xl) + var(--mantine-spacing-lg))">
      {columns.map((c) => (
        <Group
          key={c.name}
          gap="xs"
          px="xs"
          py={2}
          wrap="nowrap"
          style={c.isRestricted ? { opacity: 0.45 } : undefined}
        >
          <OverflowTooltip label={`${c.name} · ${c.dataType}`}>
            <Text size="xs" style={{ minWidth: 0 }}>
              {c.name}
            </Text>
          </OverflowTooltip>
          <Code fz="xs">{c.dataType}</Code>
          {c.isNullable && (
            <Text size="xs" c="dimmed">
              null
            </Text>
          )}
          {c.isRestricted ? (
            <Tooltip label="Restricted — you cannot access this column" withArrow>
              <IconLock size={10} color="var(--mantine-color-red-6)" />
            </Tooltip>
          ) : c.isSensitive ? (
            <Tooltip label="Sensitive — excluded from generated queries" withArrow>
              <IconShieldLock size={10} color="var(--mantine-color-yellow-6)" />
            </Tooltip>
          ) : null}
        </Group>
      ))}
    </Stack>
  );
}

export function SchemaSidebar({
  schema,
  onTableClick,
}: {
  schema: ReturnType<typeof useSchema>;
  onTableClick: TableClickHandler;
}) {
  const [expanded, setExpanded] = useSessionState<Array<string>>("sluice:query:expanded", []);

  function toggle(id: string) {
    setExpanded((prev) => (prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id]));
  }

  if (schema.isLoading) {
    return (
      <Stack gap="xs">
        {[1, 2, 3].map((i) => (
          <Skeleton key={i} h={24} radius="sm" />
        ))}
      </Stack>
    );
  }

  if (schema.isError) {
    return (
      <Alert color="red" title="Schema load failed" mt="xs">
        Could not connect to the server.
      </Alert>
    );
  }

  if (!schema.data) {
    return (
      <Text size="sm" c="dimmed" p="xs">
        Select a database to browse its schema.
      </Text>
    );
  }

  const tree = schema.data;

  return (
    <Stack gap={0}>
      {tree.schemas.map((s) => {
        const schemaId = `schema:${s.name}`;
        const schemaOpen = expanded.includes(schemaId);
        return (
          <div key={s.name}>
            <Flex wrap="nowrap" align="center" w="100%">
              <NavLink
                label={s.name}
                leftSection={<IconDatabase size={14} />}
                onClick={() => toggle(schemaId)}
                active={false}
                style={{ width: "max-content", flexShrink: 0 }}
                styles={{ label: { whiteSpace: "nowrap" } }}
              />
              <Box
                px={4}
                onClick={() => toggle(schemaId)}
                style={{ ...stickyRight, display: "flex", cursor: "pointer" }}
              >
                {schemaOpen ? <IconChevronDown size={12} /> : <IconChevronRight size={12} />}
              </Box>
            </Flex>

            {schemaOpen && (
              <>
                <Group_ id={`${schemaId}:tables`} title="Tables" count={s.tables.length} expanded={expanded} onToggle={toggle}>
                  {s.tables.map((t) => {
                    const id = `table:${s.name}.${t.name}`;
                    const open = expanded.includes(id);
                    return (
                      <div key={t.name}>
                        <Row
                          label={t.name}
                          icon={<IconTable size={14} />}
                          indent="xl"
                          onClick={() => toggle(id)}
                          right={
                            <>
                              <ActionIcon variant="subtle" color="gray" size="sm" onClick={() => toggle(id)} aria-label={open ? "Collapse" : "Expand"}>
                                {open ? <IconChevronDown size={12} /> : <IconChevronRight size={12} />}
                              </ActionIcon>
                              <Tooltip label="Append SELECT query" position="right" withArrow>
                                <Button onClick={() => onTableClick(s.name, t.name, t.columns)} size="xs" variant="subtle" disabled={t.columns.every((c) => c.isSensitive)}>
                                  <IconPlaylistAdd />
                                </Button>
                              </Tooltip>
                            </>
                          }
                        />
                        {open && (
                          <>
                            <ColumnList columns={t.columns} />
                            {t.indexes.map((ix) => (
                              <Row key={ix.name} label={`${ix.name} (${ix.columns.join(", ")})`} icon={<IconKey size={12} />} indent="calc(var(--mantine-spacing-xl) + var(--mantine-spacing-md))" />
                            ))}
                          </>
                        )}
                      </div>
                    );
                  })}
                </Group_>

                <Group_ id={`${schemaId}:views`} title="Views" count={s.views.length} expanded={expanded} onToggle={toggle}>
                  {s.views.map((v) => {
                    const id = `view:${s.name}.${v.name}`;
                    const open = expanded.includes(id);
                    return (
                      <div key={v.name}>
                        <Row
                          label={v.name}
                          icon={<IconEye size={14} />}
                          indent="xl"
                          onClick={() => toggle(id)}
                          right={
                            <Tooltip label="Append SELECT query" position="right" withArrow>
                              <Button onClick={() => onTableClick(s.name, v.name, v.columns)} size="xs" variant="subtle" disabled={v.columns.every((c) => c.isSensitive)}>
                                <IconPlaylistAdd />
                              </Button>
                            </Tooltip>
                          }
                        />
                        {open && <ColumnList columns={v.columns} />}
                      </div>
                    );
                  })}
                </Group_>

                <Group_ id={`${schemaId}:matviews`} title="Materialized Views" count={s.materializedViews.length} expanded={expanded} onToggle={toggle}>
                  {s.materializedViews.map((m) => {
                    const id = `matview:${s.name}.${m.name}`;
                    const open = expanded.includes(id);
                    return (
                      <div key={m.name}>
                        <Row label={m.name} icon={<IconStack2 size={14} />} indent="xl" onClick={() => toggle(id)} />
                        {open && <ColumnList columns={m.columns} />}
                      </div>
                    );
                  })}
                </Group_>

                <Group_ id={`${schemaId}:functions`} title="Functions" count={s.routines.length} expanded={expanded} onToggle={toggle}>
                  {s.routines.map((r) => (
                    <Row
                      key={`${r.name}(${r.signature})`}
                      label={`${r.name}(${r.signature})${r.returnType ? ` → ${r.returnType}` : ""}`}
                      icon={<IconMathFunction size={14} />}
                      indent="xl"
                    />
                  ))}
                </Group_>

                <Group_ id={`${schemaId}:sequences`} title="Sequences" count={s.sequences.length} expanded={expanded} onToggle={toggle}>
                  {s.sequences.map((seq) => (
                    <Row key={seq.name} label={`${seq.name} (${seq.dataType})`} icon={<IconListNumbers size={14} />} indent="xl" />
                  ))}
                </Group_>

                <Group_ id={`${schemaId}:types`} title="Types" count={s.types.length} expanded={expanded} onToggle={toggle}>
                  {s.types.map((ty) => (
                    <Row
                      key={ty.name}
                      label={`${ty.name} {${ty.kind}}${ty.enumLabels ? `: ${ty.enumLabels.join(", ")}` : ""}`}
                      icon={<IconBraces size={14} />}
                      indent="xl"
                    />
                  ))}
                </Group_>
              </>
            )}
          </div>
        );
      })}

      {tree.extensions.length > 0 && (
        <Group_ id="extensions" title="Extensions" count={tree.extensions.length} expanded={expanded} onToggle={toggle}>
          {tree.extensions.map((e) => (
            <Row key={e.name} label={`${e.name} ${e.version}`} icon={<IconPuzzle size={14} />} indent="xl" />
          ))}
        </Group_>
      )}
    </Stack>
  );
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd src/frontend && npx vitest run src/components/schema/__tests__/SchemaSidebar.test.tsx`
Expected: PASS.

- [ ] **Step 5: Wire the new component into the query page**

In `src/frontend/src/routes/_authed/query/index.tsx`:
1. Delete the inline `SchemaSidebar` function (lines ~410–592), the `OverflowTooltip` function (lines ~380–408), and the `TableClickHandler` type alias (lines ~371–375).
2. Remove now-unused imports (`NavLink`, `Tooltip`, `IconChevronDown`, `IconChevronRight`, `IconDatabase`, `IconLock`, `IconPlaylistAdd`, `IconShieldLock`, `IconTable`, `cloneElement`, `useState`, and the `CSSProperties`, `MouseEvent`, `ReactElement`, `Ref` type imports) — keep any still referenced by `QueryPage`/`QueryResults`.
3. Add the import:

```tsx
import { SchemaSidebar } from "@/components/schema/SchemaSidebar";
```

The existing usage `<SchemaSidebar schema={schema} onTableClick={handleTableClick} />` and `handleTableClick` stay unchanged.

- [ ] **Step 6: Lint, test, and build the frontend**

Run: `cd src/frontend && npm run lint && npm run test && npm run build`
Expected: all green. (`npm run build` catches any leftover unused import from Step 5.)

- [ ] **Step 7: Commit**

```bash
git add src/frontend/src/components/schema/SchemaSidebar.tsx \
        src/frontend/src/components/schema/__tests__/SchemaSidebar.test.tsx \
        src/frontend/src/routes/_authed/query/index.tsx
git commit -m "Render schema objects grouped by type in the browse sidebar"
```

---

## Self-Review Notes

**Spec coverage:**
- Object set (views, matviews, routines, sequences, types, indexes, extensions) → Tasks 2, 3.
- Additive typed collections → Task 2.
- `information_schema` → `pg_catalog` switch + regression guard → Tasks 3, 5.
- Annotation pass-through → Task 4.
- Enriched `GET /api/schema`, no new endpoints → Tasks 3, 6.
- Sidebar grouped-by-type, indexes under tables, extensions at db level, views selectable, ERD untouched → Task 9 (ERD files never modified).
- Autocomplete keeps views → Task 8 (ripple from the introspection switch).
- Integration + frontend tests → Tasks 5, 8, 9.
- Codegen drift gates → Tasks 6, 7.

**Out of scope (unchanged):** definition text, ERD, triggers, check/unique constraints, partitioned/foreign tables as distinct kinds, non-Postgres engines, MCP surface.

**Type consistency:** record shapes in Task 2 match constructor calls in Tasks 3–4; frontend field names (`materializedViews`, `routines`, `enumLabels`, `ownedByColumn`, table `indexes`, top-level `extensions`) are the camelCase forms openapi-typescript emits from the C# record properties.
