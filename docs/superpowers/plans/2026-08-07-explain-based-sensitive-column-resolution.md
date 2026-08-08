# EXPLAIN-Based Sensitive-Column Resolution Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the sensitive-column guard's bare-name tokenizer matching with PostgreSQL-planner-resolved column attribution (`EXPLAIN (VERBOSE, FORMAT JSON, COSTS OFF)`), eliminating over-blocking and wrong-table mis-attribution, and close the string-literal serializer vector (`query_to_xml`/`table_to_xml`) with a denylist.

**Architecture:** A new `ResolveReferencedColumnsAsync` method on `ITargetEngine` runs a plan-only EXPLAIN and returns the fully-qualified base-table columns the statement touches, extracted from the plan JSON by `PostgresPlanColumnExtractor`. `SensitiveColumnGuard` intersects that resolved set with the sensitive columns (minus per-user bypass), splitting multi-statement SQL with `SqlStatementSplitter` and EXPLAINing each. When EXPLAIN can't resolve a statement, the guard falls back to the existing `SqlColumnChecker` tokenizer (never under-blocks). A `SerializationFunctionDenylist` blocks opaque XML-export functions up front. A new optional `PolicyBlockReason` threads a denylist block through the 403 / MCP / query-log paths.

**Tech Stack:** .NET 10, Npgsql, EF Core, xUnit + Aspire.Hosting.Testing (integration), React + TypeScript + Mantine + Vitest (frontend).

## Global Constraints

- Work on branch `feat/explain-based-sensitive-column-resolution` (already checked out). Never commit to `main`.
- Commit messages: single subject line, no body paragraph.
- PR description: `## Summary` with bullets only; no Test Plan section.
- Preserve existing comments; only remove a comment if factually wrong or referencing something removed.
- TypeScript: use `Array<T>`, never `T[]` (ESLint `@typescript-eslint/array-type`).
- All database-specific (Npgsql) code stays behind `ITargetEngine`. `SensitiveColumnGuard` must not reference Npgsql.
- EXPLAIN for resolution runs **without `ANALYZE`** (plan only, no execution), inside a read-only, always-rolled-back transaction — mirror the existing `ExplainAsync` pattern in `PostgresTargetEngine.cs:569-597`.
- On any resolution failure, fall back to `SqlColumnChecker` (the tokenizer). The guard must never under-block.
- Run `npm run lint` (in `src/frontend`) after any frontend task — ESLint gates `react-hooks` / set-state-in-effect that vitest won't catch.

## File Structure

**Create:**
- `src/SluiceBase.Api/Queries/PostgresPlanColumnExtractor.cs` — pure plan-JSON → `Array<ColumnRef>` walker.
- `src/SluiceBase.Api/Queries/SqlStatementSplitter.cs` — split SQL on top-level `;`, comment/string/dollar-quote aware.
- `src/SluiceBase.Api/Queries/SerializationFunctionDenylist.cs` — XML-export function detection.
- `tests/SluiceBase.Api.Tests/PostgresPlanColumnExtractorTests.cs` — unit.
- `tests/SluiceBase.Api.Tests/SqlStatementSplitterTests.cs` — unit.
- `tests/SluiceBase.Api.Tests/SerializationFunctionDenylistTests.cs` — unit.
- `tests/IntegrationTests/SensitiveColumnResolutionTests.cs` — integration (blue-appdb) correctness + brute force + denylist e2e + fallback.
- `tests/IntegrationTests/Supports/ResolutionSchemaFixture.cs` — seeds `users`/`contacts`/`orders`/`v_users` into a throwaway schema.

**Modify:**
- `src/SluiceBase.Core/Targets/ITargetEngine.cs` — add method + `ColumnRef` record.
- `src/SluiceBase.Api/Targets/PostgresTargetEngine.cs` — implement `ResolveReferencedColumnsAsync`.
- `src/SluiceBase.Api/Queries/SensitiveColumnGuard.cs` — rewrite; add `PolicyBlockReason`.
- `src/SluiceBase.Api/Services/IQueryService.cs` — `AccessResult.BlockReason`; blocked condition; carry reason.
- `src/SluiceBase.Api/Endpoints/QueryEndpoints.cs` — `reason` extension on both 403 branches.
- `src/SluiceBase.Api/Endpoints/UpdateEndpoints.cs` — policy-block handling on the preview 403.
- `src/SluiceBase.Api/Mcp/SensitiveColumnBlockPayload.cs` + `src/SluiceBase.Api/Mcp/Tools/DatabaseTools.cs` — reason.
- `src/frontend/src/components/query/ResultGrid.tsx` + `src/frontend/src/components/query/PlanView.tsx` — render reason when `columns` empty.

**Note on test project internals:** both `tests/SluiceBase.Api.Tests` (unit; see `SensitiveColumnBlockPayloadTests.cs`) and `tests/IntegrationTests` (see `SqlColumnCheckerTests.cs`) already have `InternalsVisibleTo` access to `SluiceBase.Api` internals. Integration tests that touch the target DB use the `SluiceBaseStackFactory` Aspire fixture and the `blue-appdb` connection string (see `QueryTestSetup.cs`).

---

### Task 1: Bring up the stack, seed the demo/test schema, capture real plan fixtures

This grounds the extractor tests in real PostgreSQL output and produces the live-demo data. No production code; deliverable is confirmed field names + committed JSON fixtures.

**Files:**
- Create: `tests/SluiceBase.Api.Tests/Fixtures/plan-*.json` (captured plans).
- Create: `/tmp/seed-resolution.sql` (scratch DDL, not committed).

**Interfaces:**
- Produces: canonical `EXPLAIN (VERBOSE, FORMAT JSON, COSTS OFF)` JSON shapes (exact property names: `Node Type`, `Schema`, `Relation Name`, `Alias`, `Output`, `Filter`, `Hash Cond`, `Sort Key`, `Plans`) consumed by Task 2's extractor and its tests.

- [ ] **Step 1: Start the Aspire stack.** Use the `aspire` skill (or `aspire run` from the AppHost directory). Confirm the app is reachable at `https://localhost:5443` and the dashboard at `:17072` (see the local-gateway memory).

- [ ] **Step 2: Get the blue-appdb connection string and seed the schema.** Write `/tmp/seed-resolution.sql`:

```sql
CREATE TABLE IF NOT EXISTS users (
  id serial PRIMARY KEY, name text, email text, ssn text);
CREATE TABLE IF NOT EXISTS contacts (
  id serial PRIMARY KEY, email text, phone text);
CREATE TABLE IF NOT EXISTS orders (
  id serial PRIMARY KEY, customer_id int, amount numeric);
CREATE OR REPLACE VIEW v_users AS
  SELECT id, name AS display_name, ssn AS national_id FROM users;
INSERT INTO users(name,email,ssn) VALUES ('a','a@x.com','111') ON CONFLICT DO NOTHING;
INSERT INTO contacts(email,phone) VALUES ('c@x.com','555') ON CONFLICT DO NOTHING;
INSERT INTO orders(customer_id,amount) VALUES (1, 9.99) ON CONFLICT DO NOTHING;
```

Apply it (psql or `dotnet run` one-off) against the blue-appdb database.

- [ ] **Step 3: Capture representative plans.** For each query below, run `EXPLAIN (VERBOSE, FORMAT JSON, COSTS OFF) <query>` and save the JSON to `tests/SluiceBase.Api.Tests/Fixtures/`:
  - `plan-simple-select.json`: `SELECT email FROM contacts`
  - `plan-where-only.json`: `SELECT id FROM users WHERE ssn IS NOT NULL`
  - `plan-join.json`: `SELECT u.name, o.amount FROM users u JOIN orders o ON o.customer_id = u.id WHERE u.ssn IS NOT NULL`
  - `plan-star.json`: `SELECT * FROM users`
  - `plan-view.json`: `SELECT national_id FROM v_users`
  - `plan-tojsonb.json`: `SELECT to_jsonb(u) FROM users u`
  - `plan-xmlelement.json`: `SELECT xmlelement(name r, u.*) FROM users u`

- [ ] **Step 4: Record the whole-row shape.** Inspect `plan-tojsonb.json` and `plan-xmlelement.json`. Note whether the scan-node `Output` enumerates columns (`users.id, users.name, ...`) or shows a whole-row var (`users.*` / `to_jsonb(users.*)`). Write the observed shape as a comment at the top of the Task 2 test file. Task 2's extractor must handle whatever these show.

- [ ] **Step 5: Commit the fixtures.**

```bash
git add tests/SluiceBase.Api.Tests/Fixtures/plan-*.json
git commit -m "Add captured EXPLAIN VERBOSE plan fixtures for resolver tests"
```

---

### Task 2: `ColumnRef` + `PostgresPlanColumnExtractor` (pure plan-JSON walker)

**Files:**
- Modify: `src/SluiceBase.Core/Targets/ITargetEngine.cs` (add `ColumnRef` record only).
- Create: `src/SluiceBase.Api/Queries/PostgresPlanColumnExtractor.cs`
- Test: `tests/SluiceBase.Api.Tests/PostgresPlanColumnExtractorTests.cs`

**Interfaces:**
- Produces: `public sealed record ColumnRef(string Schema, string Table, string Column);` (Column may be `"*"` = whole-relation). `internal static class PostgresPlanColumnExtractor { public static IReadOnlyList<ColumnRef> Extract(string planJson); }`
- Consumes: fixtures from Task 1.

- [ ] **Step 1: Add the `ColumnRef` record.** At the bottom of `src/SluiceBase.Core/Targets/ITargetEngine.cs`, next to `ConnectivityResult`:

```csharp
public sealed record ColumnRef(string Schema, string Table, string Column);
```

- [ ] **Step 2: Write failing unit tests.** Create `tests/SluiceBase.Api.Tests/PostgresPlanColumnExtractorTests.cs`. Use inline JSON that matches the captured fixtures (reconcile field names with Task 1 output before finalizing):

```csharp
using SluiceBase.Api.Queries;
using SluiceBase.Core.Targets;

namespace SluiceBase.Api.Tests;

public class PostgresPlanColumnExtractorTests
{
    [Fact]
    public void Extract_SimpleSelect_ResolvesRelationFromScanNode()
    {
        const string json = """
        [{"Plan":{"Node Type":"Seq Scan","Schema":"public","Relation Name":"contacts",
          "Alias":"contacts","Output":["contacts.id","contacts.email"]}}]
        """;
        var cols = PostgresPlanColumnExtractor.Extract(json);
        Assert.Contains(new ColumnRef("public", "contacts", "email"), cols);
        Assert.DoesNotContain(cols, c => c.Table == "users");
    }

    [Fact]
    public void Extract_WhereOnlyColumn_FromFilterExpression()
    {
        const string json = """
        [{"Plan":{"Node Type":"Seq Scan","Schema":"public","Relation Name":"users",
          "Alias":"users","Output":["users.id"],"Filter":"(users.ssn IS NOT NULL)"}}]
        """;
        var cols = PostgresPlanColumnExtractor.Extract(json);
        Assert.Contains(new ColumnRef("public", "users", "ssn"), cols);
    }

    [Fact]
    public void Extract_Join_AttributesEachColumnToItsOwnRelation()
    {
        const string json = """
        [{"Plan":{"Node Type":"Hash Join","Output":["c.name","o.amount"],
          "Hash Cond":"(o.customer_id = c.id)","Plans":[
          {"Node Type":"Seq Scan","Schema":"public","Relation Name":"orders","Alias":"o",
            "Output":["o.amount","o.customer_id"]},
          {"Node Type":"Hash","Output":["c.name","c.id"],"Plans":[
            {"Node Type":"Seq Scan","Schema":"public","Relation Name":"customers","Alias":"c",
              "Output":["c.id","c.name"],"Filter":"(c.ssn IS NOT NULL)"}]}]}}]
        """;
        var cols = PostgresPlanColumnExtractor.Extract(json);
        Assert.Contains(new ColumnRef("public", "customers", "ssn"), cols);
        Assert.Contains(new ColumnRef("public", "orders", "customer_id"), cols);
        Assert.DoesNotContain(cols, c => c.Table == "orders" && c.Column == "ssn");
    }

    [Fact]
    public void Extract_QualifiedStar_EmitsWholeRelationMarker()
    {
        const string json = """
        [{"Plan":{"Node Type":"Seq Scan","Schema":"public","Relation Name":"users",
          "Alias":"u","Output":["to_jsonb(u.*)"]}}]
        """;
        var cols = PostgresPlanColumnExtractor.Extract(json);
        Assert.Contains(new ColumnRef("public", "users", "*"), cols);
    }

    [Fact]
    public void Extract_SortAndGroupKeys_AreArrays()
    {
        const string json = """
        [{"Plan":{"Node Type":"Sort","Sort Key":["u.ssn"],"Output":["u.id"],"Plans":[
          {"Node Type":"Seq Scan","Schema":"public","Relation Name":"users","Alias":"u",
            "Output":["u.id","u.ssn"]}]}}]
        """;
        var cols = PostgresPlanColumnExtractor.Extract(json);
        Assert.Contains(new ColumnRef("public", "users", "ssn"), cols);
    }
}
```

- [ ] **Step 3: Run tests to confirm they fail.**

Run: `dotnet test tests/SluiceBase.Api.Tests --filter PostgresPlanColumnExtractorTests`
Expected: FAIL — `PostgresPlanColumnExtractor` does not exist.

- [ ] **Step 4: Implement the extractor.** Create `src/SluiceBase.Api/Queries/PostgresPlanColumnExtractor.cs`:

```csharp
using System.Text.Json;
using System.Text.RegularExpressions;
using SluiceBase.Core.Targets;

namespace SluiceBase.Api.Queries;

// Walks the JSON produced by `EXPLAIN (VERBOSE, FORMAT JSON, COSTS OFF)` and returns the
// base-table columns the statement touches, fully qualified. Attribution comes from each
// scan node's real Schema + Relation Name, resolved through the node's Alias — so columns
// are never mis-attributed by bare name. A `<alias>.*` reference (whole-row var) yields a
// ColumnRef with Column == "*", meaning "every column of this relation".
internal static partial class PostgresPlanColumnExtractor
{
    // Property names whose string/array values carry alias-qualified column references.
    private static readonly string[] ExpressionFields =
    [
        "Output", "Filter", "Index Cond", "Recheck Cond", "Hash Cond", "Merge Cond",
        "Join Filter", "Sort Key", "Group Key", "Presorted Key", "Cache Key",
        "TID Cond", "One-Time Filter", "Order By",
    ];

    [GeneratedRegex("""(?<alias>"(?:[^"]|"")*"|[A-Za-z_][A-Za-z0-9_$]*)\.(?<col>"(?:[^"]|"")*"|\*|[A-Za-z_][A-Za-z0-9_$]*)""")]
    private static partial Regex QualifiedRef();

    public static IReadOnlyList<ColumnRef> Extract(string planJson)
    {
        using var doc = JsonDocument.Parse(planJson);
        var root = doc.RootElement[0].GetProperty("Plan");

        // Pass 1: alias -> (schema, relation) for every scan node in the tree.
        var aliasMap = new Dictionary<string, (string Schema, string Relation)>(StringComparer.Ordinal);
        CollectAliases(root, aliasMap);

        // Pass 2: extract qualified refs from every expression-bearing field, resolve via aliases.
        var hits = new HashSet<ColumnRef>();
        CollectColumns(root, aliasMap, hits);
        return [.. hits];
    }

    private static void CollectAliases(JsonElement node, Dictionary<string, (string, string)> map)
    {
        if (node.TryGetProperty("Relation Name", out var rel) &&
            node.TryGetProperty("Schema", out var schema))
        {
            var alias = node.TryGetProperty("Alias", out var a) ? a.GetString() : rel.GetString();
            if (!string.IsNullOrEmpty(alias))
            {
                map[alias!] = (schema.GetString() ?? "", rel.GetString() ?? "");
            }
        }

        if (node.TryGetProperty("Plans", out var plans) && plans.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in plans.EnumerateArray())
            {
                CollectAliases(child, map);
            }
        }
    }

    private static void CollectColumns(
        JsonElement node, Dictionary<string, (string Schema, string Relation)> map, HashSet<ColumnRef> hits)
    {
        foreach (var field in ExpressionFields)
        {
            if (!node.TryGetProperty(field, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                ScanExpression(value.GetString()!, map, hits);
            }
            else if (value.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in value.EnumerateArray())
                {
                    if (el.ValueKind == JsonValueKind.String)
                    {
                        ScanExpression(el.GetString()!, map, hits);
                    }
                }
            }
        }

        if (node.TryGetProperty("Plans", out var plans) && plans.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in plans.EnumerateArray())
            {
                CollectColumns(child, map, hits);
            }
        }
    }

    private static void ScanExpression(
        string expr, Dictionary<string, (string Schema, string Relation)> map, HashSet<ColumnRef> hits)
    {
        foreach (Match m in QualifiedRef().Matches(expr))
        {
            var alias = Unquote(m.Groups["alias"].Value);
            if (!map.TryGetValue(alias, out var rel))
            {
                continue; // not a base-relation alias (e.g. a function or CTE name)
            }

            var col = m.Groups["col"].Value == "*" ? "*" : Unquote(m.Groups["col"].Value);
            hits.Add(new ColumnRef(rel.Schema, rel.Relation, col));
        }
    }

    private static string Unquote(string token) =>
        token.Length >= 2 && token[0] == '"' && token[^1] == '"'
            ? token[1..^1].Replace("\"\"", "\"")
            : token;
}
```

- [ ] **Step 5: Run tests to confirm they pass.**

Run: `dotnet test tests/SluiceBase.Api.Tests --filter PostgresPlanColumnExtractorTests`
Expected: PASS. If a test fails on field names, reconcile the inline JSON with the captured fixtures from Task 1 and adjust `ExpressionFields`/regex — not the assertions.

- [ ] **Step 6: Commit.**

```bash
git add src/SluiceBase.Core/Targets/ITargetEngine.cs src/SluiceBase.Api/Queries/PostgresPlanColumnExtractor.cs tests/SluiceBase.Api.Tests/PostgresPlanColumnExtractorTests.cs
git commit -m "Add PostgresPlanColumnExtractor for EXPLAIN-based column resolution"
```

---

### Task 3: `ITargetEngine.ResolveReferencedColumnsAsync` + Postgres implementation

**Files:**
- Modify: `src/SluiceBase.Core/Targets/ITargetEngine.cs` (add method to interface).
- Modify: `src/SluiceBase.Api/Targets/PostgresTargetEngine.cs` (implement).
- Test: `tests/IntegrationTests/SensitiveColumnResolutionTests.cs` (first test only; grows in Task 8).
- Create: `tests/IntegrationTests/Supports/ResolutionSchemaFixture.cs`

**Interfaces:**
- Consumes: `ColumnRef` (Task 2), `PostgresPlanColumnExtractor.Extract` (Task 2).
- Produces: `Task<IReadOnlyList<ColumnRef>> ResolveReferencedColumnsAsync(string connectionString, string sql, CancellationToken ct);` on `ITargetEngine`.

- [ ] **Step 1: Add the interface method.** In `ITargetEngine.cs`, after `ExplainAsync`:

```csharp
    // Resolves the base-table columns a single statement reads/writes, as the PostgreSQL
    // planner sees them (EXPLAIN, plan only). Throws if the statement cannot be planned.
    Task<IReadOnlyList<ColumnRef>> ResolveReferencedColumnsAsync(
        string connectionString,
        string sql,
        CancellationToken ct);
```

- [ ] **Step 2: Create the seed fixture helper.** Create `tests/IntegrationTests/Supports/ResolutionSchemaFixture.cs`:

```csharp
using Npgsql;

namespace IntegrationTests.Supports;

// Seeds an isolated schema with the relations the resolution tests query, so runs do not
// collide with other tests sharing blue-appdb. Returns the schema name; callers qualify
// table names or SET search_path.
internal static class ResolutionSchemaFixture
{
    public static async Task<string> CreateAsync(string connectionString, CancellationToken ct)
    {
        var schema = $"res_{Guid.NewGuid():N}"[..12];
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand($"""
            CREATE SCHEMA "{schema}";
            CREATE TABLE "{schema}".users (id serial PRIMARY KEY, name text, email text, ssn text);
            CREATE TABLE "{schema}".contacts (id serial PRIMARY KEY, email text, phone text);
            CREATE TABLE "{schema}".orders (id serial PRIMARY KEY, customer_id int, amount numeric);
            CREATE VIEW "{schema}".v_users AS
              SELECT id, name AS display_name, ssn AS national_id FROM "{schema}".users;
            INSERT INTO "{schema}".users(name,email,ssn) VALUES ('a','a@x.com','111');
            INSERT INTO "{schema}".contacts(email,phone) VALUES ('c@x.com','555');
            INSERT INTO "{schema}".orders(customer_id,amount) VALUES (1, 9.99);
            """, conn);
        await cmd.ExecuteNonQueryAsync(ct);
        return schema;
    }
}
```

- [ ] **Step 3: Write a failing integration test.** Create `tests/IntegrationTests/SensitiveColumnResolutionTests.cs`:

```csharp
using Aspire.Hosting.Testing;
using IntegrationTests.Supports;
using SluiceBase.Api.Targets;
using SluiceBase.Core.Targets;

namespace IntegrationTests;

public class SensitiveColumnResolutionTests(SluiceBaseStackFactory factory)
{
    private async Task<(string conn, string schema)> SeedAsync(CancellationToken ct)
    {
        var conn = (await factory.InitialisedApp.GetConnectionStringAsync("blue-appdb", ct))!;
        var schema = await ResolutionSchemaFixture.CreateAsync(conn, ct);
        return (conn, schema);
    }

    [Fact]
    public async Task Resolve_JoinWithWhere_AttributesColumnsToCorrectRelations()
    {
        var ct = TestContext.Current.CancellationToken;
        var (conn, schema) = await SeedAsync(ct);
        var engine = new PostgresTargetEngine();

        var cols = await engine.ResolveReferencedColumnsAsync(conn,
            $"""SELECT u.name FROM "{schema}".users u JOIN "{schema}".orders o ON o.customer_id = u.id WHERE u.ssn IS NOT NULL""",
            ct);

        Assert.Contains(cols, c => c.Schema == schema && c.Table == "users" && c.Column == "ssn");
        Assert.Contains(cols, c => c.Schema == schema && c.Table == "orders" && c.Column == "customer_id");
        Assert.DoesNotContain(cols, c => c.Table == "orders" && c.Column == "ssn");
    }

    [Fact]
    public async Task Resolve_View_SeesThroughToBaseColumns()
    {
        var ct = TestContext.Current.CancellationToken;
        var (conn, schema) = await SeedAsync(ct);
        var engine = new PostgresTargetEngine();

        var cols = await engine.ResolveReferencedColumnsAsync(conn,
            $"""SELECT national_id FROM "{schema}".v_users""", ct);

        Assert.Contains(cols, c => c.Schema == schema && c.Table == "users" && c.Column == "ssn");
    }
}
```

- [ ] **Step 4: Run to confirm it fails.**

Run: `dotnet test tests/IntegrationTests --filter SensitiveColumnResolutionTests`
Expected: FAIL — `ResolveReferencedColumnsAsync` not implemented (compile error), then implement.

- [ ] **Step 5: Implement in `PostgresTargetEngine`.** Add after `ExplainAsync` (mirrors its transaction pattern):

```csharp
    public async Task<IReadOnlyList<ColumnRef>> ResolveReferencedColumnsAsync(
        string connectionString, string sql, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        // Plan only (no ANALYZE): resolves names/aliases/views/`*` without executing the
        // statement, so nothing mutates even for writes. Read-only + rollback belt-and-suspenders.
        await using (var setReadOnly = new NpgsqlCommand("SET TRANSACTION READ ONLY", conn, tx))
        {
            await setReadOnly.ExecuteNonQueryAsync(ct);
        }

        string planJson;
        await using (var cmd = new NpgsqlCommand($"EXPLAIN (VERBOSE, FORMAT JSON, COSTS OFF) {sql}", conn, tx))
        {
            var result = await cmd.ExecuteScalarAsync(ct);
            planJson = result as string ?? result?.ToString() ?? "[]";
        }

        await tx.RollbackAsync(ct);
        return PostgresPlanColumnExtractor.Extract(planJson);
    }
```

- [ ] **Step 6: Run to confirm pass.**

Run: `dotnet test tests/IntegrationTests --filter SensitiveColumnResolutionTests`
Expected: PASS (both tests).

- [ ] **Step 7: Commit.**

```bash
git add src/SluiceBase.Core/Targets/ITargetEngine.cs src/SluiceBase.Api/Targets/PostgresTargetEngine.cs tests/IntegrationTests/SensitiveColumnResolutionTests.cs tests/IntegrationTests/Supports/ResolutionSchemaFixture.cs
git commit -m "Implement EXPLAIN VERBOSE column resolution on PostgresTargetEngine"
```

---

### Task 4: `SqlStatementSplitter`

**Files:**
- Create: `src/SluiceBase.Api/Queries/SqlStatementSplitter.cs`
- Test: `tests/SluiceBase.Api.Tests/SqlStatementSplitterTests.cs`

**Interfaces:**
- Produces: `internal static class SqlStatementSplitter { public static IReadOnlyList<string> Split(string sql); }` — returns non-empty, trimmed statements, splitting only on top-level `;`, ignoring `;` inside `'...'`, `"..."`, `$tag$...$tag$`, `-- ...`, and `/* ... */` (nesting).

- [ ] **Step 1: Write failing tests.** Create `tests/SluiceBase.Api.Tests/SqlStatementSplitterTests.cs`:

```csharp
using SluiceBase.Api.Queries;

namespace SluiceBase.Api.Tests;

public class SqlStatementSplitterTests
{
    [Fact]
    public void Split_TwoStatements_ReturnsBoth()
    {
        var parts = SqlStatementSplitter.Split("SELECT 1; SELECT 2;");
        Assert.Equal(2, parts.Count);
        Assert.Equal("SELECT 1", parts[0]);
        Assert.Equal("SELECT 2", parts[1]);
    }

    [Fact]
    public void Split_SemicolonInStringLiteral_NotASplit()
    {
        var parts = SqlStatementSplitter.Split("SELECT 'a;b' FROM t");
        Assert.Single(parts);
    }

    [Fact]
    public void Split_SemicolonInDollarQuote_NotASplit()
    {
        var parts = SqlStatementSplitter.Split("SELECT $$a;b$$ FROM t; SELECT 2");
        Assert.Equal(2, parts.Count);
    }

    [Fact]
    public void Split_SemicolonInLineComment_NotASplit()
    {
        var parts = SqlStatementSplitter.Split("SELECT 1 -- x;y\n; SELECT 2");
        Assert.Equal(2, parts.Count);
    }

    [Fact]
    public void Split_TrailingWhitespaceAndEmpties_Dropped()
    {
        var parts = SqlStatementSplitter.Split("SELECT 1;;  ;");
        Assert.Single(parts);
    }
}
```

- [ ] **Step 2: Run to confirm fail.** Run: `dotnet test tests/SluiceBase.Api.Tests --filter SqlStatementSplitterTests` — Expected: FAIL (type missing).

- [ ] **Step 3: Implement.** Create `src/SluiceBase.Api/Queries/SqlStatementSplitter.cs`. Reuse the same skip logic style as `SqlTokenizer` (line/block comments, single/double quotes, dollar quotes), tracking split points at top-level `;`:

```csharp
namespace SluiceBase.Api.Queries;

// Splits a SQL batch into individual statements on top-level semicolons, skipping any `;`
// inside line comments, block comments (nesting), single-quoted strings, double-quoted
// identifiers, and dollar-quoted strings — the same lexical constructs SqlTokenizer skips.
internal static class SqlStatementSplitter
{
    public static IReadOnlyList<string> Split(string sql)
    {
        var statements = new List<string>();
        var start = 0;
        var pos = 0;
        var len = sql.Length;

        void Emit(int end)
        {
            var stmt = sql[start..end].Trim();
            if (stmt.Length > 0)
            {
                statements.Add(stmt);
            }
        }

        while (pos < len)
        {
            var c = sql[pos];

            if (c == '-' && pos + 1 < len && sql[pos + 1] == '-')
            {
                pos += 2;
                while (pos < len && sql[pos] != '\n') { pos++; }
                continue;
            }
            if (c == '/' && pos + 1 < len && sql[pos + 1] == '*')
            {
                pos += 2;
                var depth = 1;
                while (pos + 1 < len && depth > 0)
                {
                    if (sql[pos] == '/' && sql[pos + 1] == '*') { depth++; pos += 2; }
                    else if (sql[pos] == '*' && sql[pos + 1] == '/') { depth--; pos += 2; }
                    else { pos++; }
                }
                continue;
            }
            if (c == '$')
            {
                var tagEnd = pos + 1;
                while (tagEnd < len && (sql[tagEnd] == '_' || char.IsLetterOrDigit(sql[tagEnd]))) { tagEnd++; }
                if (tagEnd < len && sql[tagEnd] == '$')
                {
                    var tag = sql[pos..(tagEnd + 1)];
                    pos = tagEnd + 1;
                    var close = sql.IndexOf(tag, pos, StringComparison.Ordinal);
                    pos = close >= 0 ? close + tag.Length : len;
                }
                else { pos++; }
                continue;
            }
            if (c == '\'' || c == '"')
            {
                var quote = c;
                pos++;
                while (pos < len)
                {
                    if (sql[pos] == quote)
                    {
                        pos++;
                        if (pos < len && sql[pos] == quote) { pos++; } else { break; }
                    }
                    else { pos++; }
                }
                continue;
            }
            if (c == ';')
            {
                Emit(pos);
                pos++;
                start = pos;
                continue;
            }
            pos++;
        }

        Emit(len);
        return statements;
    }
}
```

- [ ] **Step 4: Run to confirm pass.** Run: `dotnet test tests/SluiceBase.Api.Tests --filter SqlStatementSplitterTests` — Expected: PASS.

- [ ] **Step 5: Commit.**

```bash
git add src/SluiceBase.Api/Queries/SqlStatementSplitter.cs tests/SluiceBase.Api.Tests/SqlStatementSplitterTests.cs
git commit -m "Add SqlStatementSplitter for multi-statement resolution"
```

---

### Task 5: `SerializationFunctionDenylist`

**Files:**
- Create: `src/SluiceBase.Api/Queries/SerializationFunctionDenylist.cs`
- Test: `tests/SluiceBase.Api.Tests/SerializationFunctionDenylistTests.cs`

**Interfaces:**
- Consumes: `SqlTokenizer.Tokenize` (existing).
- Produces: `internal static class SerializationFunctionDenylist { public static string? FindFirst(string sql); }` — returns the lowercase name of the first denylisted function present as an identifier token, else `null`.

- [ ] **Step 1: Write failing tests.** Create `tests/SluiceBase.Api.Tests/SerializationFunctionDenylistTests.cs`:

```csharp
using SluiceBase.Api.Queries;

namespace SluiceBase.Api.Tests;

public class SerializationFunctionDenylistTests
{
    [Theory]
    [InlineData("SELECT query_to_xml('SELECT ssn FROM users', true, false, '')", "query_to_xml")]
    [InlineData("SELECT table_to_xml('users', true, false, '')", "table_to_xml")]
    [InlineData("SELECT SCHEMA_TO_XML('public', true, false, '')", "schema_to_xml")]
    [InlineData("SELECT database_to_xml(true, false, '')", "database_to_xml")]
    public void FindFirst_DenylistedFunction_ReturnsName(string sql, string expected)
    {
        Assert.Equal(expected, SerializationFunctionDenylist.FindFirst(sql));
    }

    [Fact]
    public void FindFirst_PlainSelect_ReturnsNull()
    {
        Assert.Null(SerializationFunctionDenylist.FindFirst("SELECT email FROM users"));
    }

    [Fact]
    public void FindFirst_NameOnlyInsideStringLiteral_ReturnsNull()
    {
        // The tokenizer skips string contents, so a name mentioned in a literal is not a call.
        Assert.Null(SerializationFunctionDenylist.FindFirst("SELECT 'query_to_xml is scary' FROM users"));
    }
}
```

- [ ] **Step 2: Run to confirm fail.** Run: `dotnet test tests/SluiceBase.Api.Tests --filter SerializationFunctionDenylistTests` — Expected: FAIL.

- [ ] **Step 3: Implement.** Create `src/SluiceBase.Api/Queries/SerializationFunctionDenylist.cs`:

```csharp
namespace SluiceBase.Api.Queries;

// PostgreSQL XML-export functions take the target relation/query as a string-literal or
// regclass argument, so the referenced columns never appear as parseable identifiers —
// EXPLAIN sees a bare Function Scan and the tokenizer skips the string. They are blocked by
// name instead of resolved. Detection uses SqlTokenizer identifiers, so a name that appears
// only inside a string literal or comment does not trip the block.
internal static class SerializationFunctionDenylist
{
    private static readonly HashSet<string> Denied = new(StringComparer.OrdinalIgnoreCase)
    {
        "table_to_xml", "table_to_xmlschema", "table_to_xml_and_xmlschema",
        "query_to_xml", "query_to_xmlschema", "query_to_xml_and_xmlschema",
        "cursor_to_xml", "cursor_to_xmlschema",
        "schema_to_xml", "schema_to_xmlschema", "schema_to_xml_and_xmlschema",
        "database_to_xml", "database_to_xmlschema", "database_to_xml_and_xmlschema",
    };

    public static string? FindFirst(string sql)
    {
        var tokens = SqlTokenizer.Tokenize(sql);
        foreach (var id in tokens.Identifiers)
        {
            if (Denied.TryGetValue(id, out _))
            {
                return id.ToLowerInvariant();
            }
        }
        return null;
    }
}
```

- [ ] **Step 4: Run to confirm pass.** Run: `dotnet test tests/SluiceBase.Api.Tests --filter SerializationFunctionDenylistTests` — Expected: PASS.

- [ ] **Step 5: Commit.**

```bash
git add src/SluiceBase.Api/Queries/SerializationFunctionDenylist.cs tests/SluiceBase.Api.Tests/SerializationFunctionDenylistTests.cs
git commit -m "Add SerializationFunctionDenylist for XML-export exfiltration vectors"
```

---

### Task 6: Rewrite `SensitiveColumnGuard` to use resolution + denylist + fallback

**Files:**
- Modify: `src/SluiceBase.Api/Queries/SensitiveColumnGuard.cs`
- Test: `tests/IntegrationTests/SensitiveColumnResolutionTests.cs` (add guard-level tests)

**Interfaces:**
- Consumes: `IServerConnectionFactory.GetConnectionStringAsync` (`SluiceBase.Api.Servers`), `CredentialKind.Read`, `ITargetEngineRegistry.Resolve`, `ITargetEngine.ResolveReferencedColumnsAsync`, `SqlStatementSplitter.Split`, `SerializationFunctionDenylist.FindFirst`, `SqlColumnChecker.FindBlockedColumns` (existing fallback).
- Produces: `SensitiveColumnDecision(IReadOnlyList<SensitiveColumnHit> BlockedHits, IReadOnlyList<string> Touched, string? PolicyBlockReason = null)`. A query is blocked when `BlockedHits.Count > 0 || PolicyBlockReason is not null`.

- [ ] **Step 1: Write failing guard tests.** Append to `SensitiveColumnResolutionTests.cs`. These drive the guard through the query endpoint so bypass/DB wiring is exercised end-to-end; use the `AliceWithBlueServerAsync` helper pattern from `QueryTestSetup.cs` but marking columns in the seeded schema. Add:

```csharp
    // Helper: run a query via /api/query and return (statusCode, blockedColumns, reason).
    // Reuse QueryTestSetup.AliceWithBlueServerAsync to get a session + databaseId, then mark
    // sensitive columns via /api/admin/database/{db}/sensitive-column, then POST /api/query.
    // (See SensitiveColumnEndpointTests for the mark-column request shape.)

    [Fact]
    public async Task Query_SameColumnNameOnOtherTable_NotBlockedNorMisattributed()
    {
        // Mark {schema}.users.email sensitive. Query: SELECT email FROM {schema}.contacts.
        // Assert: 200 OK; response is not a sensitive_columns 403; history/touched excludes users.email.
    }

    [Fact]
    public async Task Query_SelectStar_DoesNotBlockOtherTablesSensitiveColumn()
    {
        // Mark {schema}.users.email AND {schema}.orders.amount sensitive.
        // Query: SELECT * FROM {schema}.users → 403 lists only users.email, never orders.amount.
    }

    [Fact]
    public async Task Query_DenylistedXmlFunction_BlockedWithReasonNoColumns()
    {
        // Mark any column sensitive. Query: SELECT query_to_xml('SELECT 1', true, false, '').
        // Assert: 403, problem body type == "sensitive_columns", columns == [], reason names query_to_xml.
    }
```

Implement the helper body concretely using the request shapes already shown in `SensitiveColumnEndpointTests.MarkAndList_RoundTrip` and `QueryEndpoints.QueryRequest`. Parse the 403 problem JSON for `columns` and `reason`.

- [ ] **Step 2: Run to confirm fail.** Run: `dotnet test tests/IntegrationTests --filter SensitiveColumnResolutionTests` — Expected: FAIL (guard still tokenizer-only: mis-attribution/over-block assertions fail; reason absent).

- [ ] **Step 3: Rewrite the guard.** Replace `src/SluiceBase.Api/Queries/SensitiveColumnGuard.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SluiceBase.Api.Data;
using SluiceBase.Api.Servers;
using SluiceBase.Core.Servers;
using SluiceBase.Core.Targets;
using SluiceBase.Core.Users;

namespace SluiceBase.Api.Queries;

// Result of evaluating a SQL string against a user's sensitive-column policy.
// BlockedHits empty && PolicyBlockReason null => allowed. Touched is the full set of
// sensitive columns the SQL references (for audit), regardless of the user's bypass grants.
// PolicyBlockReason is set when a denylisted serialization function blocks the query outright.
internal sealed record SensitiveColumnDecision(
    IReadOnlyList<SensitiveColumnHit> BlockedHits,
    IReadOnlyList<string> Touched,
    string? PolicyBlockReason = null);

internal sealed class SensitiveColumnGuard(
    AppDbContext db,
    IServerConnectionFactory connectionFactory,
    ITargetEngineRegistry engineRegistry)
{
    public async Task<SensitiveColumnDecision> EvaluateAsync(
        UserId userId, DatabaseId databaseId, string sql, CancellationToken ct)
    {
        var sensitiveColumns = await db.SensitiveColumns
            .AsNoTracking()
            .Where(c => c.DatabaseId == databaseId)
            .ToListAsync(ct);

        if (sensitiveColumns.Count == 0)
        {
            return new SensitiveColumnDecision([], []);
        }

        // Opaque XML-export functions can serialize a table named by string literal; neither
        // EXPLAIN nor the tokenizer can see the columns, so block by name up front.
        var denied = SerializationFunctionDenylist.FindFirst(sql);
        if (denied is not null)
        {
            return new SensitiveColumnDecision([], [],
                $"Query uses {denied}(), which can serialize table data that cannot be verified " +
                "against the sensitive-column policy.");
        }

        var sensitiveTuples = sensitiveColumns
            .Select(c => (c.SchemaName, c.TableName, c.ColumnName))
            .ToList();

        var touchedHits = await ResolveTouchedAsync(databaseId, sql, sensitiveTuples, ct);
        if (touchedHits.Count == 0)
        {
            return new SensitiveColumnDecision([], []);
        }

        var touched = touchedHits
            .Select(h => $"{h.Schema}.{h.Table}.{h.Column}")
            .ToArray();

        var sensitiveColumnIds = sensitiveColumns.Select(c => c.Id).ToList();
        var bypassed = await db.UserColumnBypasses
            .AsNoTracking()
            .Where(b => b.UserId == userId && sensitiveColumnIds.Contains(b.SensitiveColumnId))
            .Join(db.SensitiveColumns, b => b.SensitiveColumnId, c => c.Id,
                (b, c) => new { c.SchemaName, c.TableName, c.ColumnName })
            .ToListAsync(ct);
        var bypassedSet = bypassed
            .Select(k => (k.SchemaName, k.TableName, k.ColumnName))
            .ToHashSet();

        var blockedHits = touchedHits
            .Where(h => !bypassedSet.Contains((h.Schema, h.Table, h.Column)))
            .ToList();

        return new SensitiveColumnDecision(blockedHits, touched);
    }

    // Returns the sensitive columns the SQL actually touches. Uses EXPLAIN-based resolution per
    // statement; on any failure for a statement, falls back to the SqlColumnChecker tokenizer
    // (conservative — never under-blocks).
    private async Task<IReadOnlyList<SensitiveColumnHit>> ResolveTouchedAsync(
        DatabaseId databaseId,
        string sql,
        IReadOnlyList<(string Schema, string Table, string Column)> sensitiveTuples,
        CancellationToken ct)
    {
        var hits = new HashSet<SensitiveColumnHit>();

        ITargetEngine engine;
        string connectionString;
        try
        {
            var database = await db.Databases.AsNoTracking()
                .Include(d => d.Server)
                .SingleAsync(d => d.Id == databaseId, ct);
            engine = engineRegistry.Resolve(database.Server!.Kind);
            connectionString = await connectionFactory.GetConnectionStringAsync(
                databaseId, CredentialKind.Read, ct);
        }
        catch
        {
            // Cannot connect / resolve engine — tokenizer over the whole SQL keeps us safe.
            foreach (var h in SqlColumnChecker.FindBlockedColumns(sql, sensitiveTuples))
            {
                hits.Add(h);
            }
            return [.. hits];
        }

        foreach (var statement in SqlStatementSplitter.Split(sql))
        {
            try
            {
                var resolved = await engine.ResolveReferencedColumnsAsync(connectionString, statement, ct);
                foreach (var (schema, table, column) in sensitiveTuples)
                {
                    var isTouched = resolved.Any(r =>
                        Eq(r.Schema, schema) && Eq(r.Table, table) &&
                        (r.Column == "*" || Eq(r.Column, column)));
                    if (isTouched)
                    {
                        hits.Add(new SensitiveColumnHit(schema, table, column));
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // EXPLAIN failed for this statement (unplannable, permission, opaque) —
                // conservative tokenizer fallback for this statement only.
                foreach (var h in SqlColumnChecker.FindBlockedColumns(statement, sensitiveTuples))
                {
                    hits.Add(h);
                }
            }
        }

        return [.. hits];
    }

    private static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 4: Run to confirm pass.** Run: `dotnet test tests/IntegrationTests --filter SensitiveColumnResolutionTests` — Expected: PASS.

- [ ] **Step 5: Commit.**

```bash
git add src/SluiceBase.Api/Queries/SensitiveColumnGuard.cs tests/IntegrationTests/SensitiveColumnResolutionTests.cs
git commit -m "Rewrite SensitiveColumnGuard to resolve columns via EXPLAIN with denylist and fallback"
```

---

### Task 7: Thread `PolicyBlockReason` through IQueryService, endpoints, and MCP

**Files:**
- Modify: `src/SluiceBase.Api/Services/IQueryService.cs`
- Modify: `src/SluiceBase.Api/Endpoints/QueryEndpoints.cs`
- Modify: `src/SluiceBase.Api/Endpoints/UpdateEndpoints.cs`
- Modify: `src/SluiceBase.Api/Mcp/SensitiveColumnBlockPayload.cs`, `src/SluiceBase.Api/Mcp/Tools/DatabaseTools.cs`
- Test: `tests/IntegrationTests/SensitiveColumnResolutionTests.cs` (denylist 403 assertion from Task 6 covers query path; add one update-preview assertion).

**Interfaces:**
- Consumes: `SensitiveColumnDecision.PolicyBlockReason` (Task 6).
- Produces: 403 problem body with `columns: []` + `extensions.reason` for policy blocks; `query_log.Error` = reason; MCP payload conveys reason.

- [ ] **Step 1: `IQueryService` — carry the reason.** In `CheckAccessAsync` (`IQueryService.cs:73-85`), replace the block check:

```csharp
        var decision = await sensitiveGuard.EvaluateAsync(user.Id, database.Id, sql, ct);
        var touchedSensitive = decision.Touched.ToArray();

        if (decision.BlockedHits.Count > 0 || decision.PolicyBlockReason is not null)
        {
            var blockedList = decision.BlockedHits
                .Select(h => new BlockedColumn(h.Schema, h.Table, h.Column))
                .ToList();
            return new AccessResult(AccessCheck.Blocked, database, blockedList, touchedSensitive, decision.PolicyBlockReason);
        }

        return new AccessResult(AccessCheck.Ok, database, null, touchedSensitive, null);
```

Add `string? BlockReason` to the `AccessResult` record (`IQueryService.cs:49-53`) as the last positional field, and update the other two `new AccessResult(...)` constructions (NotFound/Forbidden at `:63`, `:70`) to pass a trailing `null`.

- [ ] **Step 2: Blocked outcomes carry the reason.** In `ExplainAsync` Blocked branch (`:98-99`): `return new QueryExplainResult(QueryOutcome.Blocked, null, access.BlockedColumns, access.BlockReason);`. In `ExecuteAsync` Blocked branch (`:131-142`), set the log error to the reason when present:

```csharp
            case AccessCheck.Blocked:
            {
                var durationMs = (int)(timeProvider.GetUtcNow() - startedAt).TotalMilliseconds;
                var errorText = access.BlockReason
                    ?? $"Sensitive columns: {string.Join(", ", access.BlockedColumns!.Select(c => $"{c.Schema}.{c.Table}.{c.Column}"))}";
                var logEntry = QueryLog.Create(user.Id, access.Database!.Id, sql,
                    QueryLogStatus.Blocked, startedAt, durationMs, null,
                    errorText, access.TouchedSensitive, source);
                db.QueryLogs.Add(logEntry);
                await db.SaveChangesAsync(ct);
                return new QueryExecutionResult(QueryOutcome.Blocked, null, access.BlockedColumns, access.BlockReason);
            }
```

- [ ] **Step 3: `QueryEndpoints` — reason extension.** In both `ExecuteQuery` (`:44-48`) and `ExplainQuery` (`:67-71`) Blocked branches, add the reason:

```csharp
            QueryOutcome.Blocked => TypedResults.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Sensitive columns",
                type: "sensitive_columns",
                extensions: new Dictionary<string, object?>
                {
                    ["columns"] = result.BlockedColumns!.Select(c => new { schema = c.Schema, table = c.Table, column = c.Column }).ToArray(),
                    ["reason"] = result.Error,
                }),
```

- [ ] **Step 4: `UpdateEndpoints` — policy block.** Replace the block at `UpdateEndpoints.cs:407-420`:

```csharp
        var decision = await sensitiveGuard.EvaluateAsync(user.Id, request.DatabaseId.Value, request.SqlText, ct);
        if (decision.BlockedHits.Count > 0 || decision.PolicyBlockReason is not null)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Sensitive columns",
                type: "sensitive_columns",
                extensions: new Dictionary<string, object?>
                {
                    ["columns"] = decision.BlockedHits
                        .Select(c => new { schema = c.Schema, table = c.Table, column = c.Column })
                        .ToArray(),
                    ["reason"] = decision.PolicyBlockReason,
                });
        }
```

- [ ] **Step 5: MCP payload.** In `SensitiveColumnBlockPayload.cs`, add an optional reason to `From`:

```csharp
    public static SensitiveColumnBlockPayload From(
        IReadOnlyList<BlockedColumn> blockedColumns, string? reason = null) =>
        new(
            ErrorDiscriminator,
            [.. blockedColumns.Select(c => $"{c.Schema}.{c.Table}.{c.Column}")],
            reason is null ? GuidanceText : $"{reason} {GuidanceText}");
```

In `DatabaseTools.cs:62`: `QueryOutcome.Blocked => SensitiveColumnBlockPayload.From(result.BlockedColumns!, result.Error),`.

- [ ] **Step 6: Add an update-preview denylist test.** In `SensitiveColumnResolutionTests.cs`, add a test that POSTs a denylisted-function SQL to the update-preview endpoint and asserts a 403 with `reason` set and `columns == []`. (Use the update-preview request path exercised in existing update tests.)

- [ ] **Step 7: Build + run.**

Run: `dotnet build SluiceBase.sln` then `dotnet test tests/IntegrationTests --filter SensitiveColumnResolutionTests`
Expected: build succeeds; tests PASS.

- [ ] **Step 8: Commit.**

```bash
git add src/SluiceBase.Api tests/IntegrationTests/SensitiveColumnResolutionTests.cs
git commit -m "Thread policy-block reason through query, update, and MCP block paths"
```

---

### Task 8: Brute-force serialization + robustness battery

**Files:**
- Test: `tests/IntegrationTests/SensitiveColumnResolutionTests.cs` (extend)

**Interfaces:**
- Consumes: everything above. No production changes expected; if a case reveals under-blocking, fix the extractor (Task 2) and reference it.

- [ ] **Step 1: Add expression-argument serializer tests.** Seed the schema; mark `{schema}.users.email` and `{schema}.users.ssn` sensitive. For each SQL below, assert the query is blocked and the blocked/touched set is exactly `{users.email, users.ssn}` with no other table:

```
to_jsonb(u), to_json(u), row_to_json(u), json_agg(u), jsonb_agg(u),
jsonb_build_object('e', u.email), array_to_json(array_agg(u)),
xmlelement(name r, u.*), xmlforest(u.email AS email, u.ssn AS ssn),
xmlagg(xmlelement(name r, u.*)),
SELECT u FROM {schema}.users u, SELECT u::text FROM {schema}.users u,
array_agg(u), SELECT u.* FROM {schema}.users u, SELECT * FROM {schema}.users,
encode(convert_to(u.email,'UTF8'),'base64')
```

Drive these as `[Theory]` `[InlineData]` rows calling the resolver directly (`engine.ResolveReferencedColumnsAsync`) for precision, asserting the resolved set intersected with the two sensitive columns is non-empty and no third table appears. (`hstore(u)` requires the `hstore` extension — include only if `CREATE EXTENSION IF NOT EXISTS hstore` succeeds; otherwise skip.)

- [ ] **Step 2: Add denylist-dormant + literal-safety tests.** Assert `query_to_xml('SELECT ssn FROM users', …)` is NOT blocked when the seeded schema has zero sensitive columns (fresh schema, nothing marked), and that a denylisted name inside a string literal (`SELECT 'table_to_xml' FROM {schema}.users`) does not trip a block.

- [ ] **Step 3: Add robustness/non-crash cases.** For each, assert the guard returns a decision without throwing and never under-blocks a marked sensitive column that is genuinely read: nested subquery selecting `ssn`, recursive CTE over `users`, `UNION` of `SELECT ssn FROM users` and `SELECT phone FROM contacts`, `DISTINCT ON (u.ssn)`, window function `row_number() OVER (ORDER BY u.ssn)`.

- [ ] **Step 4: Run.** Run: `dotnet test tests/IntegrationTests --filter SensitiveColumnResolutionTests` — Expected: PASS. Fix extractor if any expression-arg case under-resolves.

- [ ] **Step 5: Commit.**

```bash
git add tests/IntegrationTests/SensitiveColumnResolutionTests.cs
git commit -m "Add brute-force serialization and robustness battery for column resolution"
```

---

### Task 9: Frontend — render the policy-block reason

**Files:**
- Modify: `src/frontend/src/components/query/ResultGrid.tsx`
- Modify: `src/frontend/src/components/query/PlanView.tsx`
- Test: extend `src/frontend/src/components/query/__tests__/PlanView.test.tsx` (and a ResultGrid test if present).

**Interfaces:**
- Consumes: 403 body `{ columns: Array<{schema,table,column}>, reason?: string }`.

- [ ] **Step 1: Write a failing test.** In `PlanView.test.tsx`, add a case where the ApiError body is `{ type: "sensitive_columns", columns: [], reason: "Query uses query_to_xml(), ..." }` and assert the reason text renders.

- [ ] **Step 2: Run to confirm fail.** Run (in `src/frontend`): `npm run test -- PlanView` — Expected: FAIL.

- [ ] **Step 3: Update `ResultGrid.tsx`.** Extend the body type and render the reason when there are no columns:

```tsx
    const body = apiErr?.body as {
      columns?: Array<{ schema: string; table: string; column: string }>;
      reason?: string;
    } | null;
    const columns = body?.columns ?? [];
    return (
      <Alert color="orange" title="Query blocked — restricted columns" m="xs">
        {columns.length > 0 ? (
          <>
            <Text size="sm" mb="xs">
              Your query references columns you are not authorised to access:
            </Text>
            {columns.map((c, i) => (
              <Code key={i} display="block" fz="xs">
                {c.schema}.{c.table}.{c.column}
              </Code>
            ))}
          </>
        ) : (
          <Text size="sm">{body?.reason ?? "This query is blocked by the sensitive-column policy."}</Text>
        )}
      </Alert>
    );
```

- [ ] **Step 4: Update `PlanView.tsx`** the same way (extend body type with `reason?: string`; when `columns` empty, render `<Text size="sm">{body?.reason ?? "…"}</Text>`).

- [ ] **Step 5: Run tests + lint.** Run (in `src/frontend`): `npm run test -- PlanView` (Expected: PASS) then `npm run lint` (Expected: clean).

- [ ] **Step 6: Commit.**

```bash
git add src/frontend/src/components/query/ResultGrid.tsx src/frontend/src/components/query/PlanView.tsx src/frontend/src/components/query/__tests__/PlanView.test.tsx
git commit -m "Render sensitive-column policy-block reason in query results"
```

---

### Task 10: Full verification + live demonstration

**Files:** none (verification only).

- [ ] **Step 1: Full backend build + test.** Run: `dotnet build SluiceBase.sln` and `dotnet test tests/SluiceBase.Api.Tests` (unit) — Expected: PASS. Run the resolution integration tests if the local Aspire stack is healthy; otherwise rely on CI (per the integration-tests memory).

- [ ] **Step 2: Frontend full test + lint.** Run (in `src/frontend`): `npm run test` and `npm run lint` — Expected: PASS/clean.

- [ ] **Step 3: Live demo.** With the stack from Task 1 running and the demo schema seeded, log in (alice/dev) at `https://localhost:5443`, register the blue-appdb as a server/database (or reuse), mark `public.users.email` sensitive (leave `contacts.email` unmarked), then in the query workspace run:
  - `SELECT email FROM contacts` → **not** blocked (was wrongly blocked before).
  - `SELECT * FROM users` → blocked, listing only `public.users.email`.
  - `SELECT query_to_xml('SELECT 1', true, false, '')` → blocked with the policy reason, no column list.
  Screenshot each with Playwright for the PR.

- [ ] **Step 4: Open the PR.** Push the branch and open a PR with `## Summary` bullets only (no Test Plan). Summarize: EXPLAIN-based resolution fixes over-blocking + wrong-table mis-attribution; denylist closes the XML-export vector; tokenizer retained as fallback; write-path statements whose EXPLAIN is permission-denied degrade to the tokenizer (documented).

---

## Self-Review Notes

- **Spec coverage:** resolver (Tasks 2-3), splitter (4), denylist (5), guard rewrite + fallback (6), reason plumbing across query/update/MCP/frontend (7, 9), brute-force + robustness battery (8), live demo (1, 10). All spec sections mapped.
- **Write-path degradation** (EXPLAIN of writes via read credential can be permission-denied → tokenizer fallback) is intentional and documented in Task 10 Step 4; not a gap.
- **Whole-row shape risk** is de-risked by Task 1 capturing real plans before Task 2 finalizes the extractor, plus the `*` whole-relation marker as a safety net.
- **Type consistency:** `ColumnRef(Schema, Table, Column)`, `SensitiveColumnDecision(BlockedHits, Touched, PolicyBlockReason)`, `AccessResult(..., BlockReason)`, `ResolveReferencedColumnsAsync`, `SqlStatementSplitter.Split`, `SerializationFunctionDenylist.FindFirst` are used consistently across tasks.
