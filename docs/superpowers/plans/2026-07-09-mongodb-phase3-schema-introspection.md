# MongoDB Phase 3 — Schema Introspection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement `MongoTargetEngine.GetSchemaAsync` so the existing schema browser renders a MongoDB database — its collections as tables, sampled fields as dotted-path columns, and indexes.

**Architecture:** Split the work into a pure, unit-testable field-shape inference (a set of sampled `BsonDocument`s → `ColumnInfo` list) and a thin live layer that connects, lists collections, samples documents, reads indexes, and assembles the adapted `SchemaTree`. The relational tree stays additive: database → `SchemaInfo`, collection → `TableInfo`, field → `ColumnInfo`; primary keys, foreign keys, views, routines, sequences, types, and extensions are empty. The frontend `SchemaSidebar` is data-driven and needs no changes.

**Tech Stack:** .NET 10, MongoDB.Driver 3.9.0 (already referenced), xUnit v3, EF Core (metadata only).

## Global Constraints

- Develop on branch `feat/mongodb-phase3-schema-introspection` (already created off `main`). Never commit to `main`.
- Commit messages: single imperative subject line, no `feat:` prefix, no body.
- This repo treats analyzer warnings (e.g. CA1859) as BUILD ERRORS. Verify with real builds; never use `--no-build` when confirming test results. Confirm `0 Warning(s), 0 Error(s)`.
- MongoDB.Driver / MongoDB.Bson types must appear ONLY inside `MongoTargetEngine` and the new `MongoSchemaInference` (the same confinement rule as Npgsql/the Mongo engine).
- Unit tests run locally: `dotnet test tests/SluiceBase.Api.Tests/SluiceBase.Api.Tests.csproj`. Integration tests need the Aspire/Docker stack and are NOT runnable in this session — verify those paths with `dotnet build` and rely on CI.
- Any integration test that starts its OWN Testcontainer must start it via `IntegrationTests.Supports.ContainerStartup.StartWithRetryAsync`. (The tests in this plan use the shared Aspire `mongo-appdb` resource via `factory.InitialisedApp.GetConnectionStringAsync`, so this does not apply here — but do not introduce a raw `.StartAsync()`.)
- Preserve existing comments unless factually wrong.
- Read-only phase: `ExportSchemaDdlAsync`, `ExecuteQueryAsync`, and `ExecuteUpdateAsync` remain `NotSupportedException` (DDL export is Postgres-specific and already degrades gracefully; queries are Phase 4). Do not touch them.

---

## File Structure

**Created:**
- `src/SluiceBase.Api/Targets/MongoSchemaInference.cs` — pure inference: sampled documents → dotted-path `ColumnInfo` list.
- `tests/SluiceBase.Api.Tests/MongoSchemaInferenceTests.cs` — unit tests for the inference.
- `tests/IntegrationTests/MongoSchemaIntrospectionTests.cs` — live `GetSchemaAsync` against the Aspire `mongo-appdb` (CI).

**Modified:**
- `src/SluiceBase.Api/Targets/MongoTargetEngine.cs` — implement `GetSchemaAsync` (was `NotSupportedException`).

**Unchanged (intentionally):** the frontend, `SchemaTree` (Core), `SchemaService`, `SchemaEndpoint`, and `ExportSchemaDdlAsync`. The browser renders the adapted tree as-is.

---

## Task 1: Field-shape inference (pure)

**Files:**
- Create: `src/SluiceBase.Api/Targets/MongoSchemaInference.cs`
- Test: `tests/SluiceBase.Api.Tests/MongoSchemaInferenceTests.cs`

**Interfaces:**
- Consumes: `MongoDB.Bson.BsonDocument`; `SluiceBase.Core.Schemas.ColumnInfo` (record: `ColumnInfo(string Name, string DataType, bool IsNullable, bool IsSensitive = false, bool IsRestricted = false)`).
- Produces: `internal static class MongoSchemaInference` with `static IReadOnlyList<ColumnInfo> InferColumns(IReadOnlyCollection<BsonDocument> sample)`.
  - Nested documents are flattened to dotted paths (`address.city`); the intermediate object (`address`) is not emitted.
  - Arrays are a single leaf field of type `"array"` (elements are not descended into).
  - A field seen with more than one BSON type across the sample renders its `DataType` as a union like `"int | string"` (type names sorted for determinism).
  - A field absent from any sampled document has `IsNullable == true`; a field present in every document is not nullable.
  - Field order in the output is first-seen order across the sample.

- [ ] **Step 1: Write the failing tests**

Create `tests/SluiceBase.Api.Tests/MongoSchemaInferenceTests.cs`:

```csharp
using MongoDB.Bson;
using SluiceBase.Api.Targets;

namespace SluiceBase.Api.Tests;

public class MongoSchemaInferenceTests
{
    [Fact]
    public void InferColumns_FlattensNestedDocumentsToDottedPaths()
    {
        var docs = new List<BsonDocument>
        {
            new()
            {
                { "_id", ObjectId.GenerateNewId() },
                { "name", "a" },
                { "address", new BsonDocument { { "city", "NYC" }, { "zip", "10001" } } },
            },
        };

        var cols = MongoSchemaInference.InferColumns(docs);
        var names = cols.Select(c => c.Name).ToList();

        Assert.Contains("address.city", names);
        Assert.Contains("address.zip", names);
        Assert.DoesNotContain("address", names);
    }

    [Fact]
    public void InferColumns_MissingFieldIsNullable_PresentFieldIsNot()
    {
        var docs = new List<BsonDocument>
        {
            new() { { "_id", 1 }, { "email", "x@y.z" } },
            new() { { "_id", 2 } },
        };

        var cols = MongoSchemaInference.InferColumns(docs);

        Assert.False(cols.Single(c => c.Name == "_id").IsNullable);
        Assert.True(cols.Single(c => c.Name == "email").IsNullable);
    }

    [Fact]
    public void InferColumns_PolymorphicFieldRendersSortedUnionType()
    {
        var docs = new List<BsonDocument>
        {
            new() { { "v", 1 } },
            new() { { "v", "text" } },
        };

        var cols = MongoSchemaInference.InferColumns(docs);

        Assert.Equal("int | string", cols.Single(c => c.Name == "v").DataType);
    }

    [Fact]
    public void InferColumns_ArrayFieldIsLeafOfTypeArray()
    {
        var docs = new List<BsonDocument>
        {
            new() { { "tags", new BsonArray { "a", "b" } } },
        };

        var cols = MongoSchemaInference.InferColumns(docs);

        Assert.Equal("array", cols.Single(c => c.Name == "tags").DataType);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/SluiceBase.Api.Tests/SluiceBase.Api.Tests.csproj`
Expected: FAIL — compile error, `MongoSchemaInference` does not exist.

- [ ] **Step 3: Implement the inference**

Create `src/SluiceBase.Api/Targets/MongoSchemaInference.cs`:

```csharp
using MongoDB.Bson;
using SluiceBase.Core.Schemas;

namespace SluiceBase.Api.Targets;

// Infers a flat, dotted-path column list from a sample of documents. Nested documents are
// flattened (address.city); arrays are treated as a single leaf field of type "array"; a
// field observed with more than one BSON type across the sample renders as a "type1 | type2"
// union. A field absent from any sampled document is nullable.
internal static class MongoSchemaInference
{
    public static IReadOnlyList<ColumnInfo> InferColumns(IReadOnlyCollection<BsonDocument> sample)
    {
        var types = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        var presence = new Dictionary<string, int>(StringComparer.Ordinal);
        var order = new List<string>();

        foreach (var doc in sample)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            Collect(doc, prefix: null, types, order, seen);
            foreach (var path in seen)
            {
                presence[path] = presence.GetValueOrDefault(path) + 1;
            }
        }

        var total = sample.Count;
        return order
            .Select(path => new ColumnInfo(
                path,
                string.Join(" | ", types[path]),
                IsNullable: presence.GetValueOrDefault(path) < total))
            .ToList();
    }

    private static void Collect(
        BsonDocument doc,
        string? prefix,
        Dictionary<string, SortedSet<string>> types,
        List<string> order,
        HashSet<string> seen)
    {
        foreach (var element in doc.Elements)
        {
            var path = prefix is null ? element.Name : $"{prefix}.{element.Name}";

            if (element.Value is BsonDocument nested)
            {
                Collect(nested, path, types, order, seen);
                continue;
            }

            if (!types.TryGetValue(path, out var observed))
            {
                observed = new SortedSet<string>(StringComparer.Ordinal);
                types[path] = observed;
                order.Add(path);
            }

            observed.Add(TypeName(element.Value));
            seen.Add(path);
        }
    }

    private static string TypeName(BsonValue value) => value.BsonType switch
    {
        BsonType.Double => "double",
        BsonType.String => "string",
        BsonType.Array => "array",
        BsonType.Boolean => "bool",
        BsonType.DateTime => "date",
        BsonType.Int32 => "int",
        BsonType.Int64 => "long",
        BsonType.ObjectId => "objectId",
        BsonType.Decimal128 => "decimal",
        BsonType.Null => "null",
        _ => value.BsonType.ToString().ToLowerInvariant(),
    };
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/SluiceBase.Api.Tests/SluiceBase.Api.Tests.csproj`
Expected: PASS (4 new tests plus the existing suite). Confirm build shows 0 warnings.

- [ ] **Step 5: Commit**

```bash
git add src/SluiceBase.Api/Targets/MongoSchemaInference.cs \
        tests/SluiceBase.Api.Tests/MongoSchemaInferenceTests.cs
git commit -m "Infer MongoDB collection fields as dotted-path columns from a document sample"
```

---

## Task 2: GetSchemaAsync — live sampling, indexes, and tree assembly

**Files:**
- Modify: `src/SluiceBase.Api/Targets/MongoTargetEngine.cs`
- Test: `tests/IntegrationTests/MongoSchemaIntrospectionTests.cs`

**Interfaces:**
- Consumes: `MongoSchemaInference.InferColumns` (Task 1); `MongoUrl`, `MongoClient`, `IMongoCollection<BsonDocument>` (MongoDB.Driver); `SchemaTree`/`SchemaInfo`/`TableInfo`/`IndexInfo` (Core, see below); `factory.InitialisedApp.GetConnectionStringAsync("mongo-appdb", ct)` (integration fixture).
- Produces: `MongoTargetEngine.GetSchemaAsync(string connectionString, CancellationToken ct)` returns a `SchemaTree` with exactly one `SchemaInfo` (named after the connection string's database) whose `Tables` are the collections; each `TableInfo` has dotted-path columns, `PrimaryKey: null`, empty `ForeignKeys`, and the collection's indexes; `Views`/`MaterializedViews`/`Routines`/`Sequences`/`Types`/`Extensions` are empty.

Core record shapes this task constructs (from `src/SluiceBase.Core/Schemas/SchemaTree.cs`):
- `SchemaTree(IReadOnlyList<SchemaInfo> Schemas, IReadOnlyList<ExtensionInfo> Extensions)`
- `SchemaInfo(string Name, IReadOnlyList<TableInfo> Tables, IReadOnlyList<ViewInfo> Views, IReadOnlyList<MaterializedViewInfo> MaterializedViews, IReadOnlyList<RoutineInfo> Routines, IReadOnlyList<SequenceInfo> Sequences, IReadOnlyList<TypeInfo> Types)`
- `TableInfo(string Name, IReadOnlyList<ColumnInfo> Columns, PrimaryKey? PrimaryKey, IReadOnlyList<ForeignKey> ForeignKeys, IReadOnlyList<IndexInfo> Indexes)`
- `IndexInfo(string Name, IReadOnlyList<string> Columns, bool IsUnique, bool IsPrimary, string Method)`

- [ ] **Step 1: Replace the GetSchemaAsync stub**

In `src/SluiceBase.Api/Targets/MongoTargetEngine.cs`, replace the line:

```csharp
    public Task<SchemaTree> GetSchemaAsync(string connectionString, CancellationToken ct) =>
        throw new NotSupportedException("Schema introspection is not yet supported for MongoDB.");
```

with:

```csharp
    private const int SampleSize = 1000;

    public async Task<SchemaTree> GetSchemaAsync(string connectionString, CancellationToken ct)
    {
        var url = MongoUrl.Create(connectionString);
        var databaseName = url.DatabaseName
            ?? throw new InvalidOperationException("The MongoDB connection string must specify a database.");

        var settings = MongoClientSettings.FromConnectionString(connectionString);
        settings.ServerSelectionTimeout = TimeSpan.FromSeconds(5);
        var client = new MongoClient(settings);
        var database = client.GetDatabase(databaseName);

        var names = await (await database
                .ListCollectionNamesAsync(cancellationToken: ct).ConfigureAwait(false))
            .ToListAsync(ct).ConfigureAwait(false);
        names.Sort(StringComparer.Ordinal);

        var tables = new List<TableInfo>(names.Count);
        foreach (var name in names)
        {
            var collection = database.GetCollection<BsonDocument>(name);

            var sample = await collection
                .Find(Builders<BsonDocument>.Filter.Empty)
                .Limit(SampleSize)
                .ToListAsync(ct).ConfigureAwait(false);

            var columns = MongoSchemaInference.InferColumns(sample);
            var indexes = await ReadIndexesAsync(collection, ct).ConfigureAwait(false);

            tables.Add(new TableInfo(name, columns, PrimaryKey: null, ForeignKeys: [], Indexes: indexes));
        }

        var schema = new SchemaInfo(
            databaseName, tables,
            Views: [], MaterializedViews: [], Routines: [], Sequences: [], Types: []);

        return new SchemaTree([schema], Extensions: []);
    }

    private static async Task<IReadOnlyList<IndexInfo>> ReadIndexesAsync(
        IMongoCollection<BsonDocument> collection, CancellationToken ct)
    {
        var cursor = await collection.Indexes.ListAsync(ct).ConfigureAwait(false);
        var docs = await cursor.ToListAsync(ct).ConfigureAwait(false);

        var result = new List<IndexInfo>(docs.Count);
        foreach (var index in docs)
        {
            var name = index.Contains("name") ? index["name"].AsString : string.Empty;
            var key = index.Contains("key") ? index["key"].AsBsonDocument : new BsonDocument();
            var columns = key.Names.ToList();
            var unique = index.Contains("unique") && index["unique"].ToBoolean();
            result.Add(new IndexInfo(name, columns, IsUnique: unique, IsPrimary: false, Method: MethodOf(key)));
        }

        return result;
    }

    // A string index-key value denotes a special index type (hashed, text, 2dsphere);
    // a numeric direction (1 / -1) is a standard b-tree index.
    private static string MethodOf(BsonDocument key)
    {
        foreach (var element in key.Elements)
        {
            if (element.Value.IsString)
            {
                return element.Value.AsString;
            }
        }

        return "btree";
    }
```

(`MongoUrl`, `MongoClient`, `MongoClientSettings`, `Builders`, `IMongoCollection` come from `MongoDB.Driver`, already imported in this file; `BsonDocument` from `MongoDB.Bson`, also imported. `SchemaTree` etc. from `SluiceBase.Core.Schemas`, already imported.)

- [ ] **Step 2: Build the API project**

Run: `dotnet build src/SluiceBase.Api/SluiceBase.Api.csproj`
Expected: Build succeeded, 0 warnings, 0 errors.

- [ ] **Step 3: Run the API unit tests (nothing regressed)**

Run: `dotnet test tests/SluiceBase.Api.Tests/SluiceBase.Api.Tests.csproj`
Expected: PASS. Note: the existing `MongoTargetEngineTests.GetSchemaAsync_Throws_NotSupported` test asserts the OLD behavior — it will now FAIL to compile/pass. Delete that single test method from `tests/SluiceBase.Api.Tests/MongoTargetEngineTests.cs` (the `GetSchemaAsync_Throws_NotSupported` `[Fact]`), since `GetSchemaAsync` is now supported. Leave `ExecuteQueryAsync_Throws_NotSupported` untouched. Re-run and confirm PASS.

- [ ] **Step 4: Write the live integration test**

The integration suite uses an assembly-wide Aspire fixture injected via the class primary constructor (mirror `tests/IntegrationTests/MongoTestConnectionTests.cs` exactly — no Testcontainers, no `IClassFixture`/`[Collection]`). Create `tests/IntegrationTests/MongoSchemaIntrospectionTests.cs`:

```csharp
using Aspire.Hosting.Testing;
using IntegrationTests.Supports;
using MongoDB.Bson;
using MongoDB.Driver;
using SluiceBase.Api.Targets;

namespace IntegrationTests;

public sealed class MongoSchemaIntrospectionTests(SluiceBaseStackFactory factory)
{
    private readonly MongoTargetEngine _engine = new();

    [Fact]
    public async Task GetSchema_ReturnsCollectionFieldsAndIndexes()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = await factory.InitialisedApp.GetConnectionStringAsync("mongo-appdb", ct);
        Assert.NotNull(connectionString);

        var client = new MongoClient(connectionString);
        var databaseName = MongoUrl.Create(connectionString).DatabaseName;
        var database = client.GetDatabase(databaseName);
        var collectionName = "users_" + Guid.NewGuid().ToString("N")[..8];
        var collection = database.GetCollection<BsonDocument>(collectionName);

        await collection.InsertManyAsync(
            [
                new BsonDocument
                {
                    { "name", "alice" },
                    { "address", new BsonDocument { { "city", "NYC" } } },
                    { "email", "a@x.z" },
                },
                new BsonDocument
                {
                    { "name", "bob" },
                    { "address", new BsonDocument { { "city", "LA" } } },
                },
            ],
            cancellationToken: ct);

        try
        {
            var tree = await _engine.GetSchemaAsync(connectionString!, ct);

            var schema = Assert.Single(tree.Schemas);
            Assert.Equal(databaseName, schema.Name);

            var table = schema.Tables.Single(t => t.Name == collectionName);
            var columnNames = table.Columns.Select(c => c.Name).ToList();
            Assert.Contains("name", columnNames);
            Assert.Contains("address.city", columnNames);
            Assert.Contains("email", columnNames);
            Assert.True(table.Columns.Single(c => c.Name == "email").IsNullable);
            Assert.Contains(table.Indexes, i => i.Columns.Contains("_id"));
        }
        finally
        {
            await database.DropCollectionAsync(collectionName, ct);
        }
    }
}
```

- [ ] **Step 5: Build the integration test project**

Run: `dotnet build tests/IntegrationTests/IntegrationTests.csproj`
Expected: Build succeeded, 0 warnings, 0 errors. Do NOT run `tests/IntegrationTests` locally (needs the Aspire/Docker stack); CI runs it. State this in your report.

- [ ] **Step 6: Commit**

```bash
git add src/SluiceBase.Api/Targets/MongoTargetEngine.cs \
        tests/SluiceBase.Api.Tests/MongoTargetEngineTests.cs \
        tests/IntegrationTests/MongoSchemaIntrospectionTests.cs
git commit -m "Introspect MongoDB schema by sampling collections and reading indexes"
```

---

## Done criteria

- `MongoTargetEngine.GetSchemaAsync` connects to the connection string's database, lists collections, samples up to 1000 documents each, infers dotted-path columns (nested flattened, arrays as `"array"`, polymorphic as unions, absent-in-some ⇒ nullable), reads indexes, and returns a one-`SchemaInfo` `SchemaTree` with everything relational-only left empty. MongoDB types stay confined to `MongoTargetEngine` + `MongoSchemaInference`.
- Unit tests cover the inference; an integration test proves end-to-end introspection against the live Aspire `mongo-appdb`.
- The existing schema browser renders a MongoDB database with **no frontend changes** (the `SchemaSidebar` is data-driven and already handles empty PK/FK/views/routines). A registered MongoDB database selected on the query page shows its collections, fields, and indexes.
- `dotnet build` clean across Api + IntegrationTests; `tests/SluiceBase.Api.Tests` green; CI runs the integration test.
- Out of scope (later phases): the Mongo query builder replacing the SQL editor and the table-click SQL snippet (Phase 4/5); descending into array element shapes and configurable sample size (enhancements); `ExportSchemaDdlAsync` stays a graceful `NotSupportedException`.
