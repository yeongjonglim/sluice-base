# MongoDB Phase 3 — Document Schema Introspection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give MongoDB a faithful, Compass-style structure browser — collections, a nested field tree (type set + optional flag per field, no percentages), and indexes — without forcing documents into the relational "column" model.

**Architecture:** Model Mongo structure on its own terms with a new `DocumentSchema` (Core), exposed through a capability interface `IDocumentSchemaProvider` that **only** `MongoTargetEngine` implements (Mongo's relational `GetSchemaAsync` stays `NotSupportedException`, honestly). A new document-schema endpoint returns it; the query page renders a new Compass-style field tree for `mongodb` databases. The entire relational path (`SchemaTree`, `SchemaService`, `SchemaSidebar`, Postgres) is untouched.

**Tech Stack:** .NET 10, MongoDB.Driver 3.9.0 (already referenced), xUnit v3, React/TS + Mantine + generated `schema.ts`, EF Core (metadata only).

## Global Constraints

- Develop on branch `feat/mongodb-phase3-schema-introspection` (already created off `main`). Never commit to `main`.
- Commit messages: single imperative subject line, no `feat:` prefix, no body.
- This repo treats analyzer warnings (e.g. CA1859) as BUILD ERRORS. Verify with real builds; never `--no-build` when confirming test results. Confirm `0 Warning(s), 0 Error(s)`.
- MongoDB.Driver / MongoDB.Bson types appear ONLY in `MongoTargetEngine` and `MongoDocumentSchemaInference` (confinement rule).
- `Array<T>` not `T[]` in any TypeScript (ESLint `@typescript-eslint/array-type`).
- Unit tests run locally (`dotnet test tests/SluiceBase.Api.Tests/...`, frontend `npx tsc -b` / `npm run lint` / `npx vitest run`). Integration tests need the Aspire/Docker stack and are NOT runnable here — verify with `dotnet build`, rely on CI. Any integration test starting its own Testcontainer must use `IntegrationTests.Supports.ContainerStartup.StartWithRetryAsync` (the test here uses the shared Aspire `mongo-appdb` resource, so this does not apply — do not add a raw `.StartAsync()`).
- After changing API request/response shapes, regenerate `src/SluiceBase.Api/openapi.json` (Debug build) and `src/frontend/src/api/schema.ts` (`npm run gen:api` from `src/frontend`). CI gates their consistency.
- Preserve existing comments. Do NOT touch `ExportSchemaDdlAsync`/`ExecuteQueryAsync`/`ExecuteUpdateAsync` (still `NotSupportedException`) or `GetSchemaAsync` on `MongoTargetEngine` (still `NotSupportedException` — Mongo has no relational schema).
- No percentages: a field carries its observed **type set** and a boolean **Optional** (absent from ≥1 sampled instance). No numeric type/presence shares.

---

## File Structure

**Created:**
- `src/SluiceBase.Core/Schemas/DocumentSchema.cs` — `DocumentSchema` / `CollectionSchema` / `DocumentField` records.
- `src/SluiceBase.Core/Targets/IDocumentSchemaProvider.cs` — the capability interface.
- `src/SluiceBase.Api/Targets/MongoDocumentSchemaInference.cs` — pure sample → field tree.
- `tests/SluiceBase.Api.Tests/MongoDocumentSchemaInferenceTests.cs` — inference unit tests.
- `tests/IntegrationTests/MongoDocumentSchemaTests.cs` — live introspection (CI).
- `src/frontend/src/components/schema/MongoStructureView.tsx` — Compass-style field tree.
- `src/frontend/src/components/schema/__tests__/MongoStructureView.test.tsx` — component tests.

**Modified:**
- `src/SluiceBase.Api/Targets/MongoTargetEngine.cs` — implement `IDocumentSchemaProvider`.
- `src/SluiceBase.Api/Endpoints/SchemaEndpoint.cs` — add `GET /api/schema/{databaseId}/document`.
- `src/SluiceBase.Api/Endpoints/CatalogEndpoints.cs` + `src/SluiceBase.Api/Services/ICatalogService.cs` — add `Kind` to the catalog server item (so the query page can branch on engine).
- `src/SluiceBase.Api/openapi.json` + `src/frontend/src/api/schema.ts` — regenerated.
- `src/frontend/src/api/hooks.ts` — add `useDocumentSchema`.
- `src/frontend/src/routes/_authed/query/index.tsx` — branch on `kind`: Mongo → structure view.

**Unchanged (intentionally):** `SchemaTree`, `SchemaService`, `SchemaSidebar`, Postgres path.

---

## Task 1: Document schema model, capability, and inference

**Files:**
- Create: `src/SluiceBase.Core/Schemas/DocumentSchema.cs`
- Create: `src/SluiceBase.Core/Targets/IDocumentSchemaProvider.cs`
- Create: `src/SluiceBase.Api/Targets/MongoDocumentSchemaInference.cs`
- Test: `tests/SluiceBase.Api.Tests/MongoDocumentSchemaInferenceTests.cs`

**Interfaces:**
- Consumes: `MongoDB.Bson.BsonDocument`; existing `IndexInfo` (in `SluiceBase.Core.Schemas`).
- Produces:
  - `DocumentSchema(string Database, IReadOnlyList<CollectionSchema> Collections)`
  - `CollectionSchema(string Name, int SampledDocuments, IReadOnlyList<DocumentField> Fields, IReadOnlyList<IndexInfo> Indexes)`
  - `DocumentField(string Name, IReadOnlyList<string> Types, bool Optional, IReadOnlyList<DocumentField> Children)`
  - `IDocumentSchemaProvider { Task<DocumentSchema> GetDocumentSchemaAsync(string connectionString, CancellationToken ct) }`
  - `MongoDocumentSchemaInference.InferFields(IReadOnlyCollection<BsonDocument> sample) -> IReadOnlyList<DocumentField>`. Subdocuments → a field with type `"object"` + children; arrays → type `"array"` (array-of-document elements contribute their fields as children; scalar-array elements do not); polymorphic fields carry a sorted multi-type set; `Optional == true` when the field is absent from ≥1 instance at its level; field order is first-seen.

- [ ] **Step 1: Write the failing tests**

Create `tests/SluiceBase.Api.Tests/MongoDocumentSchemaInferenceTests.cs`:

```csharp
using MongoDB.Bson;
using SluiceBase.Api.Targets;

namespace SluiceBase.Api.Tests;

public class MongoDocumentSchemaInferenceTests
{
    [Fact]
    public void InferFields_NestedDocument_BecomesObjectFieldWithChildren()
    {
        var docs = new List<BsonDocument>
        {
            new()
            {
                { "_id", ObjectId.GenerateNewId() },
                { "address", new BsonDocument { { "city", "NYC" }, { "zip", "10001" } } },
            },
        };

        var fields = MongoDocumentSchemaInference.InferFields(docs);

        var address = fields.Single(f => f.Name == "address");
        Assert.Equal(["object"], address.Types);
        Assert.Equal(["city", "zip"], address.Children.Select(c => c.Name).ToList());
    }

    [Fact]
    public void InferFields_MissingField_IsOptional()
    {
        var docs = new List<BsonDocument>
        {
            new() { { "_id", 1 }, { "email", "x@y.z" } },
            new() { { "_id", 2 } },
        };

        var fields = MongoDocumentSchemaInference.InferFields(docs);

        Assert.False(fields.Single(f => f.Name == "_id").Optional);
        Assert.True(fields.Single(f => f.Name == "email").Optional);
    }

    [Fact]
    public void InferFields_PolymorphicField_HasSortedTypeSet()
    {
        var docs = new List<BsonDocument>
        {
            new() { { "v", 1 } },
            new() { { "v", "text" } },
        };

        var fields = MongoDocumentSchemaInference.InferFields(docs);

        Assert.Equal(["int", "string"], fields.Single(f => f.Name == "v").Types);
    }

    [Fact]
    public void InferFields_ArrayOfDocuments_ExposesElementFieldsAsChildren()
    {
        var docs = new List<BsonDocument>
        {
            new()
            {
                { "items", new BsonArray { new BsonDocument { { "sku", "A1" } } } },
            },
        };

        var fields = MongoDocumentSchemaInference.InferFields(docs);

        var items = fields.Single(f => f.Name == "items");
        Assert.Equal(["array"], items.Types);
        Assert.Equal("sku", items.Children.Single().Name);
    }

    [Fact]
    public void InferFields_ScalarArray_HasNoChildren()
    {
        var docs = new List<BsonDocument> { new() { { "tags", new BsonArray { "a", "b" } } } };

        var fields = MongoDocumentSchemaInference.InferFields(docs);

        var tags = fields.Single(f => f.Name == "tags");
        Assert.Equal(["array"], tags.Types);
        Assert.Empty(tags.Children);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/SluiceBase.Api.Tests/SluiceBase.Api.Tests.csproj`
Expected: FAIL — compile errors, types don't exist.

- [ ] **Step 3: Create the Core model**

Create `src/SluiceBase.Core/Schemas/DocumentSchema.cs`:

```csharp
namespace SluiceBase.Core.Schemas;

// A MongoDB database's inferred structure. Unlike SchemaTree (relational), this models
// documents faithfully: a nested field tree with a per-field type set and an "optional" flag
// (absent from at least one sampled instance at its level), plus the collection's indexes.
// The shape is sampled, not declared.
public sealed record DocumentSchema(
    string Database,
    IReadOnlyList<CollectionSchema> Collections);

public sealed record CollectionSchema(
    string Name,
    int SampledDocuments,
    IReadOnlyList<DocumentField> Fields,
    IReadOnlyList<IndexInfo> Indexes);

public sealed record DocumentField(
    string Name,
    IReadOnlyList<string> Types,
    bool Optional,
    IReadOnlyList<DocumentField> Children);
```

- [ ] **Step 4: Create the capability interface**

Create `src/SluiceBase.Core/Targets/IDocumentSchemaProvider.cs`:

```csharp
using SluiceBase.Core.Schemas;

namespace SluiceBase.Core.Targets;

// A capability implemented by engines whose "schema" is a sampled document structure rather
// than a relational schema (e.g. MongoDB). Relational engines do not implement this.
public interface IDocumentSchemaProvider
{
    Task<DocumentSchema> GetDocumentSchemaAsync(string connectionString, CancellationToken ct);
}
```

- [ ] **Step 5: Implement the inference**

Create `src/SluiceBase.Api/Targets/MongoDocumentSchemaInference.cs`:

```csharp
using MongoDB.Bson;
using SluiceBase.Core.Schemas;

namespace SluiceBase.Api.Targets;

// Builds a nested field tree from a document sample. Each field records the set of BSON types
// observed for it, whether it is optional (missing from at least one instance at its level),
// and its children (subdocument fields, or the fields of array-of-document elements). Types
// are named simply: "object" for subdocuments, "array" for arrays, plus scalar BSON names.
internal static class MongoDocumentSchemaInference
{
    public static IReadOnlyList<DocumentField> InferFields(IReadOnlyCollection<BsonDocument> sample)
    {
        var root = new Aggregator();
        foreach (var doc in sample)
        {
            root.Add(doc);
        }

        return root.Build();
    }

    private sealed class Aggregator
    {
        private int _instances;
        private readonly List<string> _order = [];
        private readonly Dictionary<string, Node> _fields = new(StringComparer.Ordinal);

        public void Add(BsonDocument doc)
        {
            _instances++;
            foreach (var element in doc.Elements)
            {
                if (!_fields.TryGetValue(element.Name, out var node))
                {
                    node = new Node();
                    _fields[element.Name] = node;
                    _order.Add(element.Name);
                }

                node.Count++;
                node.Observe(element.Value);
            }
        }

        public IReadOnlyList<DocumentField> Build() =>
            _order
                .Select(name =>
                {
                    var node = _fields[name];
                    return new DocumentField(
                        name,
                        node.Types.ToList(),
                        Optional: node.Count < _instances,
                        node.Children?.Build() ?? []);
                })
                .ToList();

        private sealed class Node
        {
            public int Count;
            public readonly SortedSet<string> Types = new(StringComparer.Ordinal);
            public Aggregator? Children;

            public void Observe(BsonValue value)
            {
                switch (value)
                {
                    case BsonDocument sub:
                        Types.Add("object");
                        (Children ??= new Aggregator()).Add(sub);
                        break;
                    case BsonArray array:
                        Types.Add("array");
                        foreach (var item in array)
                        {
                            if (item is BsonDocument itemDoc)
                            {
                                (Children ??= new Aggregator()).Add(itemDoc);
                            }
                        }

                        break;
                    default:
                        Types.Add(TypeName(value));
                        break;
                }
            }
        }
    }

    private static string TypeName(BsonValue value) => value.BsonType switch
    {
        BsonType.Double => "double",
        BsonType.String => "string",
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

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/SluiceBase.Api.Tests/SluiceBase.Api.Tests.csproj`
Expected: PASS (5 new tests + existing). Build 0 warnings.

- [ ] **Step 7: Commit**

```bash
git add src/SluiceBase.Core/Schemas/DocumentSchema.cs \
        src/SluiceBase.Core/Targets/IDocumentSchemaProvider.cs \
        src/SluiceBase.Api/Targets/MongoDocumentSchemaInference.cs \
        tests/SluiceBase.Api.Tests/MongoDocumentSchemaInferenceTests.cs
git commit -m "Add MongoDB document schema model and sample-based field-tree inference"
```

---

## Task 2: MongoTargetEngine implements the document-schema capability

**Files:**
- Modify: `src/SluiceBase.Api/Targets/MongoTargetEngine.cs`
- Test: `tests/IntegrationTests/MongoDocumentSchemaTests.cs`

**Interfaces:**
- Consumes: `MongoDocumentSchemaInference.InferFields` (Task 1); `IDocumentSchemaProvider`, `DocumentSchema`/`CollectionSchema` (Task 1); `IndexInfo` (Core); `MongoUrl`/`MongoClient`/`IMongoCollection<BsonDocument>` (driver); `factory.InitialisedApp.GetConnectionStringAsync("mongo-appdb", ct)`.
- Produces: `MongoTargetEngine : ITargetEngine, IDocumentSchemaProvider`; `GetDocumentSchemaAsync` returns a `DocumentSchema` named after the connection string's database, one `CollectionSchema` per collection (with `SampledDocuments`, inferred `Fields`, and `Indexes`).

- [ ] **Step 1: Add the capability to the engine**

In `src/SluiceBase.Api/Targets/MongoTargetEngine.cs`, change the class declaration:

```csharp
internal sealed class MongoTargetEngine : ITargetEngine, IDocumentSchemaProvider
```

Then add these members (place after the `TestConnectionAsync` method, before the `NotSupportedException` stubs). Leave the existing `GetSchemaAsync` stub as-is (Mongo has no relational schema).

```csharp
    private const int SampleSize = 1000;

    public async Task<DocumentSchema> GetDocumentSchemaAsync(string connectionString, CancellationToken ct)
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

        var collections = new List<CollectionSchema>(names.Count);
        foreach (var name in names)
        {
            var collection = database.GetCollection<BsonDocument>(name);

            var sample = await collection
                .Find(Builders<BsonDocument>.Filter.Empty)
                .Limit(SampleSize)
                .ToListAsync(ct).ConfigureAwait(false);

            var fields = MongoDocumentSchemaInference.InferFields(sample);
            var indexes = await ReadIndexesAsync(collection, ct).ConfigureAwait(false);

            collections.Add(new CollectionSchema(name, sample.Count, fields, indexes));
        }

        return new DocumentSchema(databaseName, collections);
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

(`MongoUrl`/`MongoClient`/`MongoClientSettings`/`Builders`/`IMongoCollection` from `MongoDB.Driver`; `BsonDocument` from `MongoDB.Bson`; `DocumentSchema`/`CollectionSchema`/`IndexInfo` from `SluiceBase.Core.Schemas`; `IDocumentSchemaProvider` from `SluiceBase.Core.Targets` — add `using SluiceBase.Core.Targets;` if not present. All others are already imported in this file.)

- [ ] **Step 2: Build the API project**

Run: `dotnet build src/SluiceBase.Api/SluiceBase.Api.csproj`
Expected: Build succeeded, 0 warnings, 0 errors.

- [ ] **Step 3: Run API unit tests (nothing regressed)**

Run: `dotnet test tests/SluiceBase.Api.Tests/SluiceBase.Api.Tests.csproj`
Expected: PASS. (The existing `GetSchemaAsync_Throws_NotSupported` test stays valid — Mongo's relational `GetSchemaAsync` is unchanged.)

- [ ] **Step 4: Write the live integration test**

Mirror `tests/IntegrationTests/MongoTestConnectionTests.cs` (assembly-wide Aspire fixture via primary constructor; no Testcontainers, no `IClassFixture`). Create `tests/IntegrationTests/MongoDocumentSchemaTests.cs`:

```csharp
using Aspire.Hosting.Testing;
using IntegrationTests.Supports;
using MongoDB.Bson;
using MongoDB.Driver;
using SluiceBase.Api.Targets;

namespace IntegrationTests;

public sealed class MongoDocumentSchemaTests(SluiceBaseStackFactory factory)
{
    private readonly MongoTargetEngine _engine = new();

    [Fact]
    public async Task GetDocumentSchema_ReturnsFieldTreeAndIndexes()
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
            var schema = await _engine.GetDocumentSchemaAsync(connectionString!, ct);

            Assert.Equal(databaseName, schema.Database);
            var users = schema.Collections.Single(c => c.Name == collectionName);
            Assert.Equal(2, users.SampledDocuments);

            var address = users.Fields.Single(f => f.Name == "address");
            Assert.Contains("object", address.Types);
            Assert.Contains(address.Children, c => c.Name == "city");
            Assert.True(users.Fields.Single(f => f.Name == "email").Optional);
            Assert.Contains(users.Indexes, i => i.Columns.Contains("_id"));
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
Expected: Build succeeded, 0 warnings, 0 errors. Do NOT run `tests/IntegrationTests` locally (Aspire/Docker; CI runs it). State this in your report.

- [ ] **Step 6: Commit**

```bash
git add src/SluiceBase.Api/Targets/MongoTargetEngine.cs \
        tests/IntegrationTests/MongoDocumentSchemaTests.cs
git commit -m "Introspect MongoDB document schema by sampling collections and reading indexes"
```

---

## Task 3: Document-schema endpoint and API contract

**Files:**
- Modify: `src/SluiceBase.Api/Endpoints/SchemaEndpoint.cs`
- Regenerate: `src/SluiceBase.Api/openapi.json`, `src/frontend/src/api/schema.ts`

**Interfaces:**
- Consumes: `IDocumentSchemaProvider` (Task 1), `MongoTargetEngine`'s implementation (Task 2), `ITargetEngineRegistry` (existing), `IServerConnectionFactory` (existing).
- Produces: `GET /api/schema/{databaseId}/document` → `200 Ok<DocumentSchema>` for a MongoDB database the user can query; `404` if the database is missing or its engine is not an `IDocumentSchemaProvider`; `403` without `query:execute`.

- [ ] **Step 1: Read the existing SchemaEndpoint to match its patterns**

Open `src/SluiceBase.Api/Endpoints/SchemaEndpoint.cs`. Note the `Map` method (where routes are registered), the `using`s, and the existing `ExportSchemaDdl` handler — the new handler mirrors its auth (`ICurrentUserAccessor` + `IAccessResolver.HasDatabasePermissionAsync(..., Permissions.QueryExecute, ...)`), database load (`.Include(d => d.Server)`), and connection (`IServerConnectionFactory.GetConnectionStringAsync(databaseId, CredentialKind.Read, ct)`).

- [ ] **Step 2: Register the route**

In `SchemaEndpoint.Map`, add alongside the existing route registrations:

```csharp
        app.MapGet("/api/schema/{databaseId}/document", GetDocumentSchema)
            .RequireAuthorization()
            .WithName("GetDocumentSchema");
```

(The existing schema routes use exactly `.RequireAuthorization()` — no policy — with the permission check done inside the handler. Match that.)

- [ ] **Step 3: Add the handler**

Add this handler method to `SchemaEndpoint` (mirror the `ExportSchemaDdl` signature/usings; add `using SluiceBase.Core.Targets;` and `using SluiceBase.Core.Schemas;` if not already present):

```csharp
    private static async Task<Results<Ok<DocumentSchema>, NotFound, ForbidHttpResult>> GetDocumentSchema(
        DatabaseId databaseId,
        AppDbContext db,
        ICurrentUserAccessor currentUser,
        IAccessResolver resolver,
        IServerConnectionFactory connectionFactory,
        ITargetEngineRegistry engineRegistry,
        CancellationToken ct)
    {
        var user = await currentUser.GetAsync(ct);

        var database = await db.Databases.AsNoTracking()
            .Include(d => d.Server)
            .SingleOrDefaultAsync(d => d.Id == databaseId, ct);
        if (database is null)
        {
            return TypedResults.NotFound();
        }

        var hasRole = await resolver.HasDatabasePermissionAsync(user!.Id, databaseId, Permissions.QueryExecute, ct);
        if (!hasRole)
        {
            return TypedResults.Forbid();
        }

        // Only engines that model a document structure (MongoDB) answer this endpoint.
        if (engineRegistry.Resolve(database.Server!.Kind) is not IDocumentSchemaProvider provider)
        {
            return TypedResults.NotFound();
        }

        var connectionString = await connectionFactory
            .GetConnectionStringAsync(databaseId, CredentialKind.Read, ct);
        var schema = await provider.GetDocumentSchemaAsync(connectionString, ct);
        return TypedResults.Ok(schema);
    }
```

- [ ] **Step 4: Expose the server kind in the catalog (needed by the query page)**

The query page must know a selected database's engine kind to choose the browser, and query users read the catalog (`/api/catalog/server`), not `/api/server`. The catalog currently omits `kind`. In `src/SluiceBase.Api/Endpoints/CatalogEndpoints.cs`, add `Kind` to `CatalogServerItem`:

```csharp
    public sealed record CatalogServerItem(
        ServerId Id,
        string Name,
        string Kind,
        IReadOnlyList<CatalogDatabaseItem> Databases);
```

In `src/SluiceBase.Api/Services/ICatalogService.cs`, pass it through where `CatalogServerItem` is constructed (the group key is the `Server`):

```csharp
            .Select(g => new CatalogServerItem(g.Key.Id, g.Key.Name, g.Key.Kind,
                [.. g.Select(d => new CatalogDatabaseItem(d.Id, d.DisplayName, d.CanWrite)).OrderBy(d => d.DisplayName)]))
```

(This is additive; existing catalog consumers ignore the extra field.)

- [ ] **Step 5: Build and regenerate the contract**

Run: `dotnet build src/SluiceBase.Api/SluiceBase.Api.csproj` — expect 0 warnings/0 errors (Debug build regenerates `openapi.json`).
Then from `src/frontend`: `npm run gen:api`. Confirm `git diff --stat` shows both `src/SluiceBase.Api/openapi.json` and `src/frontend/src/api/schema.ts` changed, that `schema.ts` now has the `/api/schema/{databaseId}/document` path with a `DocumentSchema`-shaped response (grep for `document` and `Collections`/`Fields`), and that the catalog server item now carries `kind`.

- [ ] **Step 6: Run API unit tests**

Run: `dotnet test tests/SluiceBase.Api.Tests/SluiceBase.Api.Tests.csproj`
Expected: PASS. (Endpoint and catalog behavior are covered by CI integration tests; do not run `tests/IntegrationTests` locally.)

- [ ] **Step 7: Commit**

```bash
git add src/SluiceBase.Api/Endpoints/SchemaEndpoint.cs \
        src/SluiceBase.Api/Endpoints/CatalogEndpoints.cs \
        src/SluiceBase.Api/Services/ICatalogService.cs \
        src/SluiceBase.Api/openapi.json \
        src/frontend/src/api/schema.ts
git commit -m "Expose MongoDB document schema and server kind through query-gated endpoints"
```

---

## Task 4: Compass-style Mongo structure browser (frontend)

**Files:**
- Modify: `src/frontend/src/api/hooks.ts`
- Create: `src/frontend/src/components/schema/MongoStructureView.tsx`
- Create: `src/frontend/src/components/schema/__tests__/MongoStructureView.test.tsx`
- Modify: `src/frontend/src/routes/_authed/query/index.tsx`

**Interfaces:**
- Consumes: the regenerated `paths["/api/schema/{databaseId}/document"]` types (Task 3); the `useServers` catalog (already used by `DatabaseSelect`) to resolve the selected database's kind.
- Produces: `useDocumentSchema(databaseId)`; `<MongoStructureView schema={...} />`; the query page renders the Mongo browser for `kind === "mongodb"`.

- [ ] **Step 1: Add the data hook**

In `src/frontend/src/api/hooks.ts`, next to `useSchema`, add (match the existing `useSchema` implementation style — same `apiRequest` + `useQuery` shape):

```ts
type DocumentSchemaResponse =
  paths["/api/schema/{databaseId}/document"]["get"]["responses"][200]["content"]["application/json"];

export function useDocumentSchema(databaseId: string | null) {
  return useQuery({
    queryKey: ["document-schema", databaseId] as const,
    queryFn: () =>
      apiRequest<void, DocumentSchemaResponse>(`/api/schema/${databaseId}/document`),
    enabled: databaseId !== null,
  });
}
```

(Copy the exact `useQuery`/`enabled`/`apiRequest` conventions from the neighbouring `useSchema` — the snippet above must match this file's actual patterns, e.g. any `staleTime` or return-shape it uses.)

- [ ] **Step 2: Write the component test (failing)**

Create `src/frontend/src/components/schema/__tests__/MongoStructureView.test.tsx`. Like `SchemaSidebar.test.tsx`, the component uses Mantine, so wrap it in `MantineProvider`. `MongoStructureView` takes its data as a prop (no hooks), so no `vi.mock` is needed:

```tsx
import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { MantineProvider } from "@mantine/core";
import { MongoStructureView } from "@/components/schema/MongoStructureView";

const schema = {
  database: "shop",
  collections: [
    {
      name: "users",
      sampledDocuments: 2,
      fields: [
        { name: "name", types: ["string"], optional: false, children: [] },
        {
          name: "address",
          types: ["object"],
          optional: false,
          children: [{ name: "city", types: ["string"], optional: false, children: [] }],
        },
        { name: "email", types: ["string"], optional: true, children: [] },
      ],
      indexes: [{ name: "_id_", columns: ["_id"], isUnique: true, isPrimary: false, method: "btree" }],
    },
  ],
};

describe("MongoStructureView", () => {
  it("renders collections, fields, nested children, types, and indexes", () => {
    render(
      <MantineProvider>
        <MongoStructureView schema={schema} />
      </MantineProvider>,
    );
    expect(screen.getByText("users")).toBeInTheDocument();
    expect(screen.getByText("name")).toBeInTheDocument();
    expect(screen.getByText("address")).toBeInTheDocument();
    expect(screen.getByText("city")).toBeInTheDocument();
    // type chips
    expect(screen.getAllByText("string").length).toBeGreaterThan(0);
    // optional marker on email
    expect(screen.getByTestId("field-email")).toHaveTextContent("optional");
    // index
    expect(screen.getByText("_id_")).toBeInTheDocument();
  });
});
```

Run: `cd src/frontend && npx vitest run MongoStructureView` → FAIL (component missing).

- [ ] **Step 3: Implement the component**

Create `src/frontend/src/components/schema/MongoStructureView.tsx`:

```tsx
import { Badge, Box, Group, Stack, Text } from "@mantine/core";
import type { paths } from "@/api/schema";

type DocumentSchema =
  paths["/api/schema/{databaseId}/document"]["get"]["responses"][200]["content"]["application/json"];
type CollectionSchema = DocumentSchema["collections"][number];
type DocumentField = CollectionSchema["fields"][number];

function FieldRow({ field, depth }: { field: DocumentField; depth: number }) {
  return (
    <Box>
      <Group
        gap="xs"
        wrap="nowrap"
        data-testid={`field-${field.name}`}
        style={{ paddingLeft: depth * 12 }}
      >
        <Text size="sm">{field.name}</Text>
        {field.types.map((t) => (
          <Badge key={t} size="xs" variant="light" color="gray">
            {t}
          </Badge>
        ))}
        {field.optional && (
          <Text size="xs" c="dimmed">
            optional
          </Text>
        )}
      </Group>
      {field.children.map((child) => (
        <FieldRow key={child.name} field={child} depth={depth + 1} />
      ))}
    </Box>
  );
}

export function MongoStructureView({ schema }: { schema: DocumentSchema }) {
  return (
    <Stack gap="md" p="xs">
      {schema.collections.map((collection) => (
        <Box key={collection.name}>
          <Text fw={600} size="sm">
            {collection.name}
          </Text>
          <Text size="xs" c="dimmed" mb={4}>
            sampled from {collection.sampledDocuments} document(s)
          </Text>
          <Stack gap={2}>
            {collection.fields.map((field) => (
              <FieldRow key={field.name} field={field} depth={0} />
            ))}
          </Stack>
          {collection.indexes.length > 0 && (
            <Box mt={4}>
              <Text size="xs" fw={600} c="dimmed">
                Indexes
              </Text>
              {collection.indexes.map((index) => (
                <Text key={index.name} size="xs">
                  {index.name} ({index.columns.join(", ")})
                </Text>
              ))}
            </Box>
          )}
        </Box>
      ))}
    </Stack>
  );
}
```

Run: `cd src/frontend && npx vitest run MongoStructureView` → PASS.

- [ ] **Step 4: Branch the query page on kind**

In `src/frontend/src/routes/_authed/query/index.tsx`:

1. Import the hook, the component, and the servers catalog hook that `DatabaseSelect` uses (check `DatabaseSelect.tsx` for the exact hook name — e.g. `useServers` — and reuse it):

```tsx
import { MongoStructureView } from "@/components/schema/MongoStructureView";
import { useDocumentSchema } from "@/api/hooks"; // add to the existing hooks import
```

2. Resolve the selected database's kind from the catalog (place near the other hooks, after `selectedDatabaseId` is defined). Use `useCatalogServer` — the same query-user-facing hook `DatabaseSelect` uses (import it from `@/api/hooks`); its `select` returns `{ servers }` where each server now carries `kind` (Task 3, Step 4):

```tsx
  const catalog = useCatalogServer();
  const selectedKind =
    catalog.data?.servers.find((s) =>
      s.databases.some((d) => d.id === selectedDatabaseId),
    )?.kind ?? null;
  const isMongo = selectedKind === "mongodb";
  const documentSchema = useDocumentSchema(isMongo ? selectedDatabaseId : null);
```

3. In the left `Splitter.Pane`, render the Mongo browser instead of `SchemaSidebar` when `isMongo`. Replace the `<SchemaSidebar schema={schema} onTableClick={handleTableClick} />` line with:

```tsx
            {isMongo ? (
              documentSchema.data ? (
                <MongoStructureView schema={documentSchema.data} />
              ) : null
            ) : (
              <SchemaSidebar schema={schema} onTableClick={handleTableClick} />
            )}
```

4. In the right (editor) pane, show a placeholder for Mongo instead of the SQL editor (the Mongo query builder is Phase 4). Wrap the existing `<SqlEditor ... />` block so it renders only for non-Mongo, and add a Mongo placeholder:

```tsx
              {isMongo ? (
                <Text size="sm" c="dimmed" p="md">
                  A MongoDB query builder is coming in a later phase. You can browse the
                  collection structure on the left.
                </Text>
              ) : (
                <SqlEditor
                  ref={editorRef}
                  value={editorContent}
                  onChange={setEditorContent}
                  databaseId={selectedDatabaseId}
                  extensions={editorExtensions}
                  minLines={20}
                  height="100%"
                  style={{ flex: 1, minHeight: 0 }}
                />
              )}
```

(Keep the existing Run button / results area; they simply stay unused for Mongo since there is no editor content. If `Text` is not already imported from `@mantine/core` in this file, add it.)

- [ ] **Step 5: Typecheck, lint, and test the frontend**

From `src/frontend`, run and paste the real output:
- `npx tsc -b` — 0 errors.
- `npm run lint` — 0 errors; no `T[]` array types introduced.
- `npx vitest run` — all pass (including the new `MongoStructureView` test and the unchanged suite).

- [ ] **Step 6: Commit**

```bash
git add src/frontend/src/api/hooks.ts \
        src/frontend/src/components/schema/MongoStructureView.tsx \
        src/frontend/src/components/schema/__tests__/MongoStructureView.test.tsx \
        src/frontend/src/routes/_authed/query/index.tsx
git commit -m "Render a Compass-style MongoDB structure browser on the query page"
```

---

## Done criteria

- MongoDB structure is modelled faithfully (`DocumentSchema`: collections → nested field tree with a type set + `Optional` flag + children, plus indexes) — no "column" abstraction, no percentages. MongoDB types stay confined to `MongoTargetEngine` + `MongoDocumentSchemaInference`.
- `MongoTargetEngine` implements `IDocumentSchemaProvider` by sampling ≤1000 documents per collection; its relational `GetSchemaAsync` stays `NotSupportedException` (honestly).
- `GET /api/schema/{databaseId}/document` returns the structure for a MongoDB database the user can query, `404` for non-document engines, `403` without `query:execute`.
- The query page shows a Compass-style collapsible field tree (types, optional markers, nested fields, indexes) for `mongodb` databases; the relational path (Postgres, `SchemaSidebar`, `SchemaTree`) is untouched.
- Unit tests cover the inference and the component; an integration test proves end-to-end introspection against the live Aspire `mongo-appdb`. `dotnet build` clean across Api + IntegrationTests; `tests/SluiceBase.Api.Tests` and the frontend suites green; CI runs the integration test.
- Out of scope (later phases): the Mongo query builder replacing the placeholder (Phase 4); sensitive-field annotation for document schemas (Phase 4, when query blocking applies); type/presence percentages, array-scalar element types, and per-collection stats (fidelity enhancements).
```
