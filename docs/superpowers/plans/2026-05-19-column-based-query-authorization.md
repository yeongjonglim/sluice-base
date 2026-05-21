# Column-Based Query Authorization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Globally mark columns as sensitive so all `query:execute` users are blocked from referencing them in SQL queries, with a per-user bypass grant for trusted users and full audit logging.

**Architecture:** Two new EF entities (`sensitive_column`, `user_column_bypass`) drive a pre-execution check in `QueryEndpoints`. A hand-written `SqlTokenizer` breaks SQL into typed tokens (identifiers, string literals, comments, dollar-quoted strings) with no external dependencies. `SqlColumnChecker` uses it to find identifier tokens matching sensitive column names and blocks the query. The schema endpoint is annotated to mark restricted columns for the frontend so the generate-query button can exclude them.

**Tech Stack:** .NET 10, EF Core (snake_case naming), Vogen IDs, React/TypeScript/Mantine frontend, `openapi-typescript` for type generation. No external SQL parser dependency.

---

## File Map

**New — Core:**
- `src/SluiceBase.Core/Permissions/SensitiveColumnId.cs` — Vogen ID
- `src/SluiceBase.Core/Permissions/UserColumnBypassId.cs` — Vogen ID
- `src/SluiceBase.Core/Permissions/SensitiveColumn.cs` — domain entity
- `src/SluiceBase.Core/Permissions/UserColumnBypass.cs` — domain entity

**Modified — Core:**
- `src/SluiceBase.Core/Queries/QueryLogStatus.cs` — add `Blocked` variant
- `src/SluiceBase.Core/Schemas/SchemaTree.cs` — add `IsRestricted` to `ColumnInfo`

**New — Api:**
- `src/SluiceBase.Api/Data/Configurations/SensitiveColumnConfiguration.cs`
- `src/SluiceBase.Api/Data/Configurations/UserColumnBypassConfiguration.cs`
- `src/SluiceBase.Api/Queries/SqlTokenizer.cs` — hand-written SQL tokenizer (no external deps)
- `src/SluiceBase.Api/Queries/SqlColumnChecker.cs` — uses tokenizer to find blocked column hits
- `src/SluiceBase.Api/Endpoints/SensitiveColumnEndpoints.cs`
- `src/SluiceBase.Api/Data/Migrations/<timestamp>_AddSensitiveColumns.cs` — EF generated

**Modified — Api:**
- `src/SluiceBase.Api/Data/AppDbContext.cs` — add `SensitiveColumns`, `UserColumnBypasses` DbSets
- `src/SluiceBase.Api/Endpoints/QueryEndpoints.cs` — add enforcement + `Blocked` logging
- `src/SluiceBase.Api/Endpoints/SchemaEndpoints.cs` — annotate `IsRestricted` per column
- `src/SluiceBase.Api/Endpoints/EndpointMapper.cs` — register `SensitiveColumnEndpoints`

**New — Tests:**
- `tests/IntegrationTests/SqlTokenizerTests.cs` — fast unit tests for the tokenizer
- `tests/IntegrationTests/SqlColumnCheckerTests.cs` — fast unit tests for the checker
- `tests/IntegrationTests/SensitiveColumnEndpointTests.cs` — integration tests

**Modified — Tests:**
- `tests/IntegrationTests/QueryEndpointTest.cs` — add blocked and bypass query tests
- `tests/IntegrationTests/SchemaEndpointTests.cs` — add `IsRestricted` test

**Modified — Frontend:**
- `src/frontend/src/api/schema.ts` — regenerated via `npm run gen:api`
- `src/frontend/src/api/hooks.ts` — add sensitive column hooks
- `src/frontend/src/routes/_authed/access.tsx` — add Sensitive Columns tab
- `src/frontend/src/routes/_authed/query/index.tsx` — schema sidebar indicator + 403 error

---

## Task 1: Core Entities

**Files:**
- Create: `src/SluiceBase.Core/Permissions/SensitiveColumnId.cs`
- Create: `src/SluiceBase.Core/Permissions/UserColumnBypassId.cs`
- Create: `src/SluiceBase.Core/Permissions/SensitiveColumn.cs`
- Create: `src/SluiceBase.Core/Permissions/UserColumnBypass.cs`

- [ ] **Step 1: Create SensitiveColumnId**

```csharp
// src/SluiceBase.Core/Permissions/SensitiveColumnId.cs
using Vogen;

namespace SluiceBase.Core.Permissions;

[ValueObject<Guid>(customizations: Customizations.AddFactoryMethodForGuids)]
public readonly partial struct SensitiveColumnId;
```

- [ ] **Step 2: Create UserColumnBypassId**

```csharp
// src/SluiceBase.Core/Permissions/UserColumnBypassId.cs
using Vogen;

namespace SluiceBase.Core.Permissions;

[ValueObject<Guid>(customizations: Customizations.AddFactoryMethodForGuids)]
public readonly partial struct UserColumnBypassId;
```

- [ ] **Step 3: Create SensitiveColumn entity**

```csharp
// src/SluiceBase.Core/Permissions/SensitiveColumn.cs
using SluiceBase.Core.Servers;
using SluiceBase.Core.Users;

namespace SluiceBase.Core.Permissions;

public sealed class SensitiveColumn
{
#pragma warning disable CS8618
    private SensitiveColumn() { }
#pragma warning restore CS8618

    private SensitiveColumn(
        SensitiveColumnId id, DatabaseId databaseId,
        string schemaName, string tableName, string columnName,
        UserId? markedById, DateTimeOffset at)
    {
        Id = id;
        DatabaseId = databaseId;
        SchemaName = schemaName;
        TableName = tableName;
        ColumnName = columnName;
        MarkedById = markedById;
        MarkedAt = at;
    }

    public SensitiveColumnId Id { get; private set; }
    public DatabaseId DatabaseId { get; private set; }
    public string SchemaName { get; private set; }
    public string TableName { get; private set; }
    public string ColumnName { get; private set; }
    public DateTimeOffset MarkedAt { get; private set; }
    public UserId? MarkedById { get; private set; }

    public static SensitiveColumn Mark(
        DatabaseId databaseId, string schemaName, string tableName, string columnName,
        UserId? markedById, DateTimeOffset at) =>
        new(SensitiveColumnId.FromNewVersion7Guid(), databaseId,
            schemaName, tableName, columnName, markedById, at);
}
```

- [ ] **Step 4: Create UserColumnBypass entity**

```csharp
// src/SluiceBase.Core/Permissions/UserColumnBypass.cs
using SluiceBase.Core.Users;

namespace SluiceBase.Core.Permissions;

public sealed class UserColumnBypass
{
#pragma warning disable CS8618
    private UserColumnBypass() { }
#pragma warning restore CS8618

    private UserColumnBypass(
        UserColumnBypassId id, UserId userId,
        SensitiveColumnId sensitiveColumnId,
        UserId? grantedById, DateTimeOffset at)
    {
        Id = id;
        UserId = userId;
        SensitiveColumnId = sensitiveColumnId;
        GrantedById = grantedById;
        GrantedAt = at;
    }

    public UserColumnBypassId Id { get; private set; }
    public UserId UserId { get; private set; }
    public SensitiveColumnId SensitiveColumnId { get; private set; }
    public DateTimeOffset GrantedAt { get; private set; }
    public UserId? GrantedById { get; private set; }

    public static UserColumnBypass Grant(
        UserId userId, SensitiveColumnId sensitiveColumnId,
        UserId? grantedById, DateTimeOffset at) =>
        new(UserColumnBypassId.FromNewVersion7Guid(), userId, sensitiveColumnId, grantedById, at);
}
```

- [ ] **Step 5: Build to verify**

```bash
dotnet build src/SluiceBase.Core/SluiceBase.Core.csproj
```
Expected: no errors.

- [ ] **Step 6: Commit**

```bash
git add src/SluiceBase.Core/Permissions/
git commit -m "feat: add SensitiveColumn and UserColumnBypass domain entities"
```

---

## Task 2: QueryLogStatus.Blocked + SchemaTree.IsRestricted

**Files:**
- Modify: `src/SluiceBase.Core/Queries/QueryLogStatus.cs`
- Modify: `src/SluiceBase.Core/Schemas/SchemaTree.cs`

- [ ] **Step 1: Add Blocked to QueryLogStatus**

Open `src/SluiceBase.Core/Queries/QueryLogStatus.cs`. Add `Blocked` after `Timeout`:

```csharp
namespace SluiceBase.Core.Queries;

public enum QueryLogStatus
{
    Unknown = 0,
    Success,
    Error,
    Timeout,
    Blocked
}
```

- [ ] **Step 2: Add IsRestricted to ColumnInfo**

Open `src/SluiceBase.Core/Schemas/SchemaTree.cs`. Update `ColumnInfo`:

```csharp
namespace SluiceBase.Core.Schemas;

public sealed record SchemaTree(IReadOnlyList<SchemaInfo> Schemas);
public sealed record SchemaInfo(string Name, IReadOnlyList<TableInfo> Tables);
public sealed record TableInfo(string Name, IReadOnlyList<ColumnInfo> Columns);
public sealed record ColumnInfo(string Name, string DataType, bool IsNullable, bool IsRestricted = false);
```

The `IsRestricted = false` default keeps existing callers (including `PostgresTargetEngine`) working without changes.

- [ ] **Step 3: Build Core**

```bash
dotnet build src/SluiceBase.Core/SluiceBase.Core.csproj
```
Expected: no errors.

- [ ] **Step 4: Commit**

```bash
git add src/SluiceBase.Core/Queries/QueryLogStatus.cs src/SluiceBase.Core/Schemas/SchemaTree.cs
git commit -m "feat: add QueryLogStatus.Blocked and ColumnInfo.IsRestricted"
```

---

## Task 3: EF Configuration, AppDbContext, Migration

**Files:**
- Create: `src/SluiceBase.Api/Data/Configurations/SensitiveColumnConfiguration.cs`
- Create: `src/SluiceBase.Api/Data/Configurations/UserColumnBypassConfiguration.cs`
- Modify: `src/SluiceBase.Api/Data/AppDbContext.cs`
- Create: migration (EF generated)

- [ ] **Step 1: Create SensitiveColumnConfiguration**

```csharp
// src/SluiceBase.Api/Data/Configurations/SensitiveColumnConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Servers;
using SluiceBase.Core.Users;

namespace SluiceBase.Api.Data.Configurations;

internal sealed class SensitiveColumnConfiguration : IEntityTypeConfiguration<SensitiveColumn>
{
    public void Configure(EntityTypeBuilder<SensitiveColumn> builder)
    {
        builder.ToTable("sensitive_column");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.SchemaName).HasMaxLength(128).IsRequired();
        builder.Property(c => c.TableName).HasMaxLength(128).IsRequired();
        builder.Property(c => c.ColumnName).HasMaxLength(128).IsRequired();
        builder.Property(c => c.MarkedAt).IsRequired();
        builder.HasIndex(c => new { c.DatabaseId, c.SchemaName, c.TableName, c.ColumnName }).IsUnique();
        builder.HasOne<Database>()
            .WithMany()
            .HasForeignKey(c => c.DatabaseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(c => c.MarkedById)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
```

- [ ] **Step 2: Create UserColumnBypassConfiguration**

```csharp
// src/SluiceBase.Api/Data/Configurations/UserColumnBypassConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Users;

namespace SluiceBase.Api.Data.Configurations;

internal sealed class UserColumnBypassConfiguration : IEntityTypeConfiguration<UserColumnBypass>
{
    public void Configure(EntityTypeBuilder<UserColumnBypass> builder)
    {
        builder.ToTable("user_column_bypass");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.GrantedAt).IsRequired();
        builder.HasIndex(b => new { b.UserId, b.SensitiveColumnId }).IsUnique();
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(b => b.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<SensitiveColumn>()
            .WithMany()
            .HasForeignKey(b => b.SensitiveColumnId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(b => b.GrantedById)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
```

- [ ] **Step 3: Add DbSets to AppDbContext**

Open `src/SluiceBase.Api/Data/AppDbContext.cs`. Add two DbSets after `UserDatabaseRoles`:

```csharp
public DbSet<SensitiveColumn> SensitiveColumns => Set<SensitiveColumn>();
public DbSet<UserColumnBypass> UserColumnBypasses => Set<UserColumnBypass>();
```

Also add the namespace at the top:
```csharp
// already has: using SluiceBase.Core.Permissions;
// (no change needed if it's already there)
```

- [ ] **Step 4: Build to verify EF picks up the configuration**

```bash
dotnet build src/SluiceBase.Api/SluiceBase.Api.csproj
```
Expected: no errors.

- [ ] **Step 5: Generate migration**

Run from the repo root:

```bash
dotnet ef migrations add AddSensitiveColumns \
  --project src/SluiceBase.Api \
  --startup-project src/SluiceBase.Api \
  -- --connectionString "Host=localhost;Database=dummy;Username=dummy;Password=dummy"
```

EF will generate two files under `src/SluiceBase.Api/Data/Migrations/`. Open the generated `Up` method and verify it creates both `sensitive_column` and `user_column_bypass` tables with the expected columns, FKs, and unique indexes. If columns are missing or names look wrong, check the configuration files.

- [ ] **Step 6: Commit**

```bash
git add src/SluiceBase.Api/Data/
git commit -m "feat: add EF configuration and migration for sensitive_column and user_column_bypass"
```

---

## Task 4: SensitiveColumnEndpoints

**Files:**
- Create: `src/SluiceBase.Api/Endpoints/SensitiveColumnEndpoints.cs`
- Modify: `src/SluiceBase.Api/Endpoints/EndpointMapper.cs`

- [ ] **Step 1: Create SensitiveColumnEndpoints**

```csharp
// src/SluiceBase.Api/Endpoints/SensitiveColumnEndpoints.cs
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SluiceBase.Api.Auth;
using SluiceBase.Api.Data;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Servers;
using SluiceBase.Core.Users;

namespace SluiceBase.Api.Endpoints;

internal static class SensitiveColumnEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin")
            .RequireAuthorization(Permissions.PermissionManage);

        admin.MapGet("/database/{databaseId}/sensitive-column", ListByDatabase)
            .WithName("ListSensitiveColumns");
        admin.MapPost("/database/{databaseId}/sensitive-column", MarkColumn)
            .WithName("MarkSensitiveColumn");
        admin.MapDelete("/database/{databaseId}/sensitive-column/{sensitiveColumnId}", UnmarkColumn)
            .WithName("UnmarkSensitiveColumn");

        admin.MapPost("/database/{databaseId}/sensitive-column/{sensitiveColumnId}/bypass", GrantBypass)
            .WithName("GrantColumnBypass");
        admin.MapDelete("/database/{databaseId}/sensitive-column/{sensitiveColumnId}/bypass/{userId}", RevokeBypass)
            .WithName("RevokeColumnBypass");
    }

    // ── list ──────────────────────────────────────────────────────────────────

    private static async Task<Ok<SensitiveColumnListResponse>> ListByDatabase(
        DatabaseId databaseId, AppDbContext db, CancellationToken ct)
    {
        var columns = await db.SensitiveColumns
            .AsNoTracking()
            .Where(c => c.DatabaseId == databaseId)
            .ToListAsync(ct);

        var columnIds = columns.Select(c => c.Id).ToList();
        var bypasses = await db.UserColumnBypasses
            .AsNoTracking()
            .Where(b => columnIds.Contains(b.SensitiveColumnId))
            .Join(db.ExternalLogins,
                b => b.UserId, l => l.UserId,
                (b, l) => new BypassItem(b.Id, b.UserId, l.Email, l.Name, b.GrantedAt, b.GrantedById))
            .ToListAsync(ct);

        var bypassesByColumnId = bypasses
            .GroupBy(b => b.Id)
            .ToDictionary(g => g.Key, g => g.ToList());

        var items = columns.Select(c => new SensitiveColumnItem(
            c.Id, c.SchemaName, c.TableName, c.ColumnName, c.MarkedAt, c.MarkedById,
            bypassesByColumnId.TryGetValue(c.Id, out var bs) ? bs : []));

        return TypedResults.Ok(new SensitiveColumnListResponse([.. items]));
    }

    // ── mark / unmark ─────────────────────────────────────────────────────────

    private static async Task<Results<ValidationProblem, NotFound, Ok, Created>> MarkColumn(
        DatabaseId databaseId,
        MarkSensitiveColumnRequest req,
        AppDbContext db,
        ICurrentUserAccessor currentUser,
        TimeProvider clock,
        CancellationToken ct)
    {
        var dbExists = await db.Databases.AnyAsync(d => d.Id == databaseId, ct);
        if (!dbExists) return TypedResults.NotFound();

        var existing = await db.SensitiveColumns.AnyAsync(
            c => c.DatabaseId == databaseId
              && c.SchemaName == req.SchemaName
              && c.TableName == req.TableName
              && c.ColumnName == req.ColumnName, ct);
        if (existing) return TypedResults.Ok();

        var actor = await currentUser.GetAsync(ct);
        db.SensitiveColumns.Add(SensitiveColumn.Mark(
            databaseId, req.SchemaName, req.TableName, req.ColumnName,
            actor?.Id, clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/admin/database/{databaseId}/sensitive-column");
    }

    private static async Task<NoContent> UnmarkColumn(
        DatabaseId databaseId,
        SensitiveColumnId sensitiveColumnId,
        AppDbContext db,
        CancellationToken ct)
    {
        var column = await db.SensitiveColumns.SingleOrDefaultAsync(
            c => c.DatabaseId == databaseId && c.Id == sensitiveColumnId, ct);
        if (column is not null)
        {
            db.SensitiveColumns.Remove(column);
            await db.SaveChangesAsync(ct);
        }
        return TypedResults.NoContent();
    }

    // ── bypass ────────────────────────────────────────────────────────────────

    private static async Task<Results<NotFound, Ok, Created>> GrantBypass(
        DatabaseId databaseId,
        SensitiveColumnId sensitiveColumnId,
        GrantBypassRequest req,
        AppDbContext db,
        ICurrentUserAccessor currentUser,
        TimeProvider clock,
        CancellationToken ct)
    {
        var columnExists = await db.SensitiveColumns.AnyAsync(
            c => c.Id == sensitiveColumnId && c.DatabaseId == databaseId, ct);
        if (!columnExists) return TypedResults.NotFound();

        var userExists = await db.Users.AnyAsync(u => u.Id == req.UserId, ct);
        if (!userExists) return TypedResults.NotFound();

        var existing = await db.UserColumnBypasses.AnyAsync(
            b => b.UserId == req.UserId && b.SensitiveColumnId == sensitiveColumnId, ct);
        if (existing) return TypedResults.Ok();

        var actor = await currentUser.GetAsync(ct);
        db.UserColumnBypasses.Add(UserColumnBypass.Grant(
            req.UserId, sensitiveColumnId, actor?.Id, clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);

        return TypedResults.Created(
            $"/api/admin/database/{databaseId}/sensitive-column/{sensitiveColumnId}/bypass");
    }

    private static async Task<NoContent> RevokeBypass(
        DatabaseId databaseId,
        SensitiveColumnId sensitiveColumnId,
        UserId userId,
        AppDbContext db,
        CancellationToken ct)
    {
        var bypass = await db.UserColumnBypasses.SingleOrDefaultAsync(
            b => b.SensitiveColumnId == sensitiveColumnId && b.UserId == userId, ct);
        if (bypass is not null)
        {
            db.UserColumnBypasses.Remove(bypass);
            await db.SaveChangesAsync(ct);
        }
        return TypedResults.NoContent();
    }

    // ── request / response records ────────────────────────────────────────────

    public sealed record MarkSensitiveColumnRequest(string SchemaName, string TableName, string ColumnName);
    public sealed record GrantBypassRequest(UserId UserId);

    public sealed record BypassItem(
        UserColumnBypassId Id,
        UserId UserId,
        string? UserEmail,
        string? UserName,
        DateTimeOffset GrantedAt,
        UserId? GrantedById);

    public sealed record SensitiveColumnItem(
        SensitiveColumnId Id,
        string SchemaName,
        string TableName,
        string ColumnName,
        DateTimeOffset MarkedAt,
        UserId? MarkedById,
        IReadOnlyList<BypassItem> Bypasses);

    public sealed record SensitiveColumnListResponse(IReadOnlyList<SensitiveColumnItem> Columns);
}
```

Note: The `bypassesByColumnId` grouping key above uses `b.Id` — this should be `b.SensitiveColumnId` (the bypass's foreign key, not the bypass's own ID). Fix that in the `ListByDatabase` method:

```csharp
// In the ListByDatabase body, replace:
var bypassesByColumnId = bypasses
    .GroupBy(b => b.Id)
// With:
var bypassesByColumnId = bypasses
    .GroupBy(b => b.GrantedById)  // This is wrong too — see actual fix below
```

Actually, since `BypassItem` doesn't carry `SensitiveColumnId`, fetch bypasses differently. Replace the `ListByDatabase` body with this corrected version:

```csharp
private static async Task<Ok<SensitiveColumnListResponse>> ListByDatabase(
    DatabaseId databaseId, AppDbContext db, CancellationToken ct)
{
    var columns = await db.SensitiveColumns
        .AsNoTracking()
        .Where(c => c.DatabaseId == databaseId)
        .ToListAsync(ct);

    var columnIds = columns.Select(c => c.Id).ToHashSet();

    var rawBypasses = await db.UserColumnBypasses
        .AsNoTracking()
        .Where(b => columnIds.Contains(b.SensitiveColumnId))
        .Join(db.ExternalLogins,
            b => b.UserId, l => l.UserId,
            (b, l) => new { b.Id, b.UserId, l.Email, l.Name, b.GrantedAt, b.GrantedById, b.SensitiveColumnId })
        .ToListAsync(ct);

    var bypassesBySensitiveColumnId = rawBypasses
        .GroupBy(b => b.SensitiveColumnId)
        .ToDictionary(
            g => g.Key,
            g => (IReadOnlyList<BypassItem>)g.Select(b =>
                new BypassItem(b.Id, b.UserId, b.Email, b.Name, b.GrantedAt, b.GrantedById)).ToList());

    var items = columns.Select(c => new SensitiveColumnItem(
        c.Id, c.SchemaName, c.TableName, c.ColumnName, c.MarkedAt, c.MarkedById,
        bypassesBySensitiveColumnId.TryGetValue(c.Id, out var bs) ? bs : []));

    return TypedResults.Ok(new SensitiveColumnListResponse([.. items]));
}
```

Use this corrected version in the file.

- [ ] **Step 2: Register endpoints in EndpointMapper**

Open `src/SluiceBase.Api/Endpoints/EndpointMapper.cs`. Add `SensitiveColumnEndpoints.Map(app);` after `DatabaseRoleEndpoints.Map(app);`:

```csharp
DatabaseRoleEndpoints.Map(app);
SensitiveColumnEndpoints.Map(app);
```

- [ ] **Step 3: Build**

```bash
dotnet build src/SluiceBase.Api/SluiceBase.Api.csproj
```
Expected: no errors.

- [ ] **Step 4: Commit**

```bash
git add src/SluiceBase.Api/Endpoints/SensitiveColumnEndpoints.cs \
        src/SluiceBase.Api/Endpoints/EndpointMapper.cs
git commit -m "feat: add SensitiveColumnEndpoints"
```

---

## Task 5: SqlTokenizer + SqlColumnChecker (TDD)

**Files:**
- Create: `tests/IntegrationTests/SqlTokenizerTests.cs`
- Create: `tests/IntegrationTests/SqlColumnCheckerTests.cs`
- Create: `src/SluiceBase.Api/Queries/SqlTokenizer.cs`
- Create: `src/SluiceBase.Api/Queries/SqlColumnChecker.cs`

Both are pure static services: no DB, no HTTP, no external dependencies. Test them directly without the Aspire stack.

**Approach:** `SqlTokenizer` does a single left-to-right scan, classifying each stretch of SQL as an identifier, string literal, comment, or punctuation. Only identifier tokens are emitted. `SqlColumnChecker` checks emitted identifiers against sensitive column names and conservatively blocks any query containing `*` when sensitive columns exist.

**Known limitation:** `SELECT id AS price` — an alias named after a sensitive column — is a false positive (the alias is an identifier token). This is accepted as a rare and safe-to-block case.

- [ ] **Step 1: Write the first failing tokenizer test**

```csharp
// tests/IntegrationTests/SqlTokenizerTests.cs
using SluiceBase.Api.Queries;

namespace IntegrationTests;

public class SqlTokenizerTests
{
    [Fact]
    public void Tokenize_ExtractsIdentifiers()
    {
        var result = SqlTokenizer.Tokenize("SELECT email, name FROM users");
        Assert.Contains("email", result.Identifiers);
        Assert.Contains("name", result.Identifiers);
        Assert.Contains("users", result.Identifiers);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

```bash
dotnet test tests/IntegrationTests \
  --filter "FullyQualifiedName~SqlTokenizerTests.Tokenize_ExtractsIdentifiers"
```
Expected: build error — `SqlTokenizer` doesn't exist yet.

- [ ] **Step 3: Create SqlTokenizer**

```csharp
// src/SluiceBase.Api/Queries/SqlTokenizer.cs
using System.Text;

namespace SluiceBase.Api.Queries;

internal static class SqlTokenizer
{
    public sealed record Result(IReadOnlyList<string> Identifiers, bool HasWildcard);

    /// <summary>
    /// Extracts identifier tokens from SQL, skipping string literals, comments, and
    /// dollar-quoted strings. Also reports whether a bare * appears outside those contexts.
    /// </summary>
    public static Result Tokenize(string sql)
    {
        var identifiers = new List<string>();
        var hasWildcard = false;
        var pos = 0;
        var len = sql.Length;

        while (pos < len)
        {
            var c = sql[pos];

            // Line comment: -- to end of line
            if (c == '-' && pos + 1 < len && sql[pos + 1] == '-')
            {
                pos += 2;
                while (pos < len && sql[pos] != '\n') pos++;
                continue;
            }

            // Block comment: /* ... */ with PostgreSQL nesting support
            if (c == '/' && pos + 1 < len && sql[pos + 1] == '*')
            {
                pos += 2;
                var depth = 1;
                while (pos + 1 < len && depth > 0)
                {
                    if (sql[pos] == '/' && sql[pos + 1] == '*') { depth++; pos += 2; }
                    else if (sql[pos] == '*' && sql[pos + 1] == '/') { depth--; pos += 2; }
                    else pos++;
                }
                continue;
            }

            // Dollar-quoted string: $tag$...$tag$ (tag may be empty)
            if (c == '$')
            {
                var tagEnd = pos + 1;
                while (tagEnd < len && (sql[tagEnd] == '_' || char.IsLetterOrDigit(sql[tagEnd])))
                    tagEnd++;
                if (tagEnd < len && sql[tagEnd] == '$')
                {
                    var tag = sql[pos..(tagEnd + 1)];
                    pos = tagEnd + 1;
                    var close = sql.IndexOf(tag, pos, StringComparison.Ordinal);
                    pos = close >= 0 ? close + tag.Length : len;
                }
                else
                {
                    pos++; // lone $ — skip as punctuation
                }
                continue;
            }

            // Single-quoted string: '...' with '' as escape
            if (c == '\'')
            {
                pos++;
                while (pos < len)
                {
                    if (sql[pos] == '\'') { pos++; if (pos < len && sql[pos] == '\'') pos++; else break; }
                    else pos++;
                }
                continue;
            }

            // Double-quoted identifier: "..." with "" as escape — emit as identifier
            if (c == '"')
            {
                pos++;
                var sb = new StringBuilder();
                while (pos < len)
                {
                    if (sql[pos] == '"') { pos++; if (pos < len && sql[pos] == '"') { sb.Append('"'); pos++; } else break; }
                    else sb.Append(sql[pos++]);
                }
                if (sb.Length > 0) identifiers.Add(sb.ToString());
                continue;
            }

            // Unquoted identifier: [a-zA-Z_][a-zA-Z0-9_]*
            // Prefix strings (E'...', B'...', X'...', N'...') start with a letter then '.
            // Treat them as string literals — skip without emitting the prefix letter.
            if (c == '_' || char.IsLetter(c))
            {
                var start = pos;
                while (pos < len && (sql[pos] == '_' || char.IsLetterOrDigit(sql[pos]))) pos++;
                if (pos < len && sql[pos] == '\'')
                {
                    // Prefix string — skip the following string literal
                    pos++;
                    while (pos < len)
                    {
                        if (sql[pos] == '\'') { pos++; if (pos < len && sql[pos] == '\'') pos++; else break; }
                        else pos++;
                    }
                    continue;
                }
                identifiers.Add(sql[start..pos]);
                continue;
            }

            // Wildcard
            if (c == '*') { hasWildcard = true; pos++; continue; }

            // Everything else (operators, punctuation, digits): skip
            pos++;
        }

        return new Result(identifiers, hasWildcard);
    }
}
```

- [ ] **Step 4: Run the first test to verify it passes**

```bash
dotnet test tests/IntegrationTests \
  --filter "FullyQualifiedName~SqlTokenizerTests.Tokenize_ExtractsIdentifiers"
```
Expected: PASS.

- [ ] **Step 5: Add remaining tokenizer tests**

Add these tests to `SqlTokenizerTests.cs`:

```csharp
[Fact]
public void Tokenize_SkipsSingleQuotedStringContent()
{
    var result = SqlTokenizer.Tokenize("SELECT id FROM users WHERE note = 'email address'");
    Assert.DoesNotContain("email", result.Identifiers);
    Assert.DoesNotContain("address", result.Identifiers);
}

[Fact]
public void Tokenize_SkipsLineComments()
{
    var result = SqlTokenizer.Tokenize("SELECT id FROM users -- email column");
    Assert.DoesNotContain("email", result.Identifiers);
}

[Fact]
public void Tokenize_SkipsBlockComments()
{
    var result = SqlTokenizer.Tokenize("SELECT /* email */ id FROM users");
    Assert.DoesNotContain("email", result.Identifiers);
}

[Fact]
public void Tokenize_SkipsNestedBlockComments()
{
    var result = SqlTokenizer.Tokenize("SELECT /* /* email */ inner */ id FROM users");
    Assert.DoesNotContain("email", result.Identifiers);
    Assert.DoesNotContain("inner", result.Identifiers);
}

[Fact]
public void Tokenize_SkipsDollarQuotedStrings()
{
    var result = SqlTokenizer.Tokenize("SELECT $$email$$, id FROM users");
    Assert.DoesNotContain("email", result.Identifiers);
}

[Fact]
public void Tokenize_SkipsTaggedDollarQuotedStrings()
{
    var result = SqlTokenizer.Tokenize("SELECT $body$email$body$, id FROM users");
    Assert.DoesNotContain("email", result.Identifiers);
}

[Fact]
public void Tokenize_ExtractsDoubleQuotedIdentifiers()
{
    var result = SqlTokenizer.Tokenize("SELECT \"email\" FROM users");
    Assert.Contains("email", result.Identifiers);
}

[Fact]
public void Tokenize_SkipsPrefixStringContent()
{
    // E'...' is a string literal — 'email' inside it should not be extracted
    var result = SqlTokenizer.Tokenize("SELECT id FROM users WHERE note = E'email'");
    Assert.DoesNotContain("email", result.Identifiers);
}

[Fact]
public void Tokenize_PriceAndPriceTypeAreDistinctTokens()
{
    var result = SqlTokenizer.Tokenize("SELECT price_type FROM orders");
    Assert.Contains("price_type", result.Identifiers);
    Assert.DoesNotContain("price", result.Identifiers);
}

[Fact]
public void Tokenize_DetectsWildcard()
{
    var result = SqlTokenizer.Tokenize("SELECT * FROM users");
    Assert.True(result.HasWildcard);
}

[Fact]
public void Tokenize_WildcardInsideString_NotDetected()
{
    var result = SqlTokenizer.Tokenize("SELECT id FROM users WHERE note LIKE '%*%'");
    Assert.False(result.HasWildcard);
}

[Fact]
public void Tokenize_UppercaseKeywords_ExtractedAsIdentifiers()
{
    // Keywords like SELECT and FROM are valid identifiers in the token stream;
    // column names may be written in any casing by the user.
    var result = SqlTokenizer.Tokenize("SELECT EMAIL, FIRST_NAME FROM USERS");
    Assert.Contains("EMAIL", result.Identifiers);
    Assert.Contains("FIRST_NAME", result.Identifiers);
    Assert.Contains("USERS", result.Identifiers);
}

[Fact]
public void Tokenize_MixedCasing_ExtractedVerbatim()
{
    // The tokenizer preserves original casing — the checker handles case-insensitive comparison.
    var result = SqlTokenizer.Tokenize("SELECT Email FROM Users");
    Assert.Contains("Email", result.Identifiers);
    Assert.DoesNotContain("email", result.Identifiers);
    Assert.DoesNotContain("EMAIL", result.Identifiers);
}

[Fact]
public void Tokenize_IdentifiersWithNumbers_Extracted()
{
    // Identifiers may contain digits anywhere after the first character.
    var result = SqlTokenizer.Tokenize("SELECT order_v2, column1, v3_price FROM orders_2024");
    Assert.Contains("order_v2", result.Identifiers);
    Assert.Contains("column1", result.Identifiers);
    Assert.Contains("v3_price", result.Identifiers);
    Assert.Contains("orders_2024", result.Identifiers);
}

[Fact]
public void Tokenize_MultilineQuery_ExtractsAllIdentifiers()
{
    var sql = """
        SELECT
          id,
          email,
          created_at
        FROM users
        WHERE status = 'active'
        ORDER BY created_at DESC
        """;
    var result = SqlTokenizer.Tokenize(sql);
    Assert.Contains("id", result.Identifiers);
    Assert.Contains("email", result.Identifiers);
    Assert.Contains("created_at", result.Identifiers);
    Assert.Contains("users", result.Identifiers);
    Assert.Contains("status", result.Identifiers);
}

[Fact]
public void Tokenize_CTE_ExtractsIdentifiersFromBothParts()
{
    var sql = """
        WITH active_users AS (
            SELECT id, email FROM users WHERE active = true
        )
        SELECT id, email FROM active_users
        """;
    var result = SqlTokenizer.Tokenize(sql);
    // identifiers from both the CTE body and the outer query
    Assert.Contains("active_users", result.Identifiers);
    Assert.Contains("id", result.Identifiers);
    Assert.Contains("email", result.Identifiers);
    Assert.Contains("users", result.Identifiers);
    Assert.Contains("active", result.Identifiers);
}

[Fact]
public void Tokenize_SchemaQualifiedColumn_ExtractsSeparateTokens()
{
    // The dot is punctuation; schema, table, and column become separate identifier tokens.
    // SqlColumnChecker matches on column name alone, so this is still correctly detected.
    var result = SqlTokenizer.Tokenize("SELECT public.users.email FROM public.users");
    Assert.Contains("public", result.Identifiers);
    Assert.Contains("users", result.Identifiers);
    Assert.Contains("email", result.Identifiers);
    // Dot must NOT become part of any token
    Assert.DoesNotContain("public.users", result.Identifiers);
    Assert.DoesNotContain("users.email", result.Identifiers);
}
```

- [ ] **Step 6: Run all tokenizer tests**

```bash
dotnet test tests/IntegrationTests \
  --filter "FullyQualifiedName~SqlTokenizerTests"
```
Expected: all PASS.

- [ ] **Step 7: Write failing checker tests**

```csharp
// tests/IntegrationTests/SqlColumnCheckerTests.cs
using SluiceBase.Api.Queries;

namespace IntegrationTests;

public class SqlColumnCheckerTests
{
    [Fact]
    public void FindBlockedColumns_SimpleSelect_ReturnsHit()
    {
        var blocked = new[] { ("public", "users", "email") };
        var hits = SqlColumnChecker.FindBlockedColumns("SELECT email FROM users", blocked);
        Assert.Single(hits);
        Assert.Equal("email", hits[0].Column);
    }
}
```

- [ ] **Step 8: Create SqlColumnChecker**

```csharp
// src/SluiceBase.Api/Queries/SqlColumnChecker.cs
namespace SluiceBase.Api.Queries;

public sealed record SensitiveColumnHit(string Schema, string Table, string Column);

internal static class SqlColumnChecker
{
    /// <summary>
    /// Returns hits for sensitive columns referenced in <paramref name="sql"/>.
    /// </summary>
    /// <param name="sql">The SQL to check.</param>
    /// <param name="blockedColumns">
    ///   Sensitive columns that are NOT bypassed for the current user.
    ///   (schema, table, column) — values need not be pre-lowercased.
    /// </param>
    public static IReadOnlyList<SensitiveColumnHit> FindBlockedColumns(
        string sql,
        IReadOnlyList<(string Schema, string Table, string Column)> blockedColumns)
    {
        if (blockedColumns.Count == 0) return [];

        var tokenResult = SqlTokenizer.Tokenize(sql);
        var hits = new HashSet<SensitiveColumnHit>();

        // SELECT * — conservatively block all sensitive columns on this database.
        // We cannot know which tables * expands to without a live schema lookup,
        // so any wildcard in a query that has sensitive columns is blocked.
        if (tokenResult.HasWildcard)
        {
            foreach (var (schema, table, column) in blockedColumns)
                hits.Add(new(schema, table, column));
            return [.. hits];
        }

        // Check identifier tokens against blocked column names (case-insensitive).
        // Table/schema qualification is not available after tokenization, so any
        // identifier matching a blocked column name is treated as a hit regardless
        // of which table it belongs to. This is conservative and correct for the
        // common case where a column name uniquely identifies the sensitive field.
        foreach (var identifier in tokenResult.Identifiers)
        {
            var lower = identifier.ToLowerInvariant();
            foreach (var (schema, table, column) in blockedColumns)
            {
                if (column.ToLowerInvariant() == lower)
                    hits.Add(new(schema, table, column));
            }
        }

        return [.. hits];
    }
}
```

- [ ] **Step 9: Run the first checker test**

```bash
dotnet test tests/IntegrationTests \
  --filter "FullyQualifiedName~SqlColumnCheckerTests.FindBlockedColumns_SimpleSelect_ReturnsHit"
```
Expected: PASS.

- [ ] **Step 10: Add remaining checker tests**

```csharp
[Fact]
public void FindBlockedColumns_SafeColumn_ReturnsEmpty()
{
    var blocked = new[] { ("public", "users", "email") };
    var hits = SqlColumnChecker.FindBlockedColumns("SELECT name FROM users", blocked);
    Assert.Empty(hits);
}

[Fact]
public void FindBlockedColumns_WhereClause_Detected()
{
    var blocked = new[] { ("public", "users", "email") };
    var hits = SqlColumnChecker.FindBlockedColumns(
        "SELECT id FROM users WHERE email = 'x@example.com'", blocked);
    Assert.Single(hits);
}

[Fact]
public void FindBlockedColumns_ColumnInStringLiteral_NotBlocked()
{
    var blocked = new[] { ("public", "users", "email") };
    var hits = SqlColumnChecker.FindBlockedColumns(
        "SELECT id FROM users WHERE note = 'email address'", blocked);
    Assert.Empty(hits);
}

[Fact]
public void FindBlockedColumns_ColumnInComment_NotBlocked()
{
    var blocked = new[] { ("public", "users", "email") };
    var hits = SqlColumnChecker.FindBlockedColumns(
        "SELECT id FROM users -- email is sensitive", blocked);
    Assert.Empty(hits);
}

[Fact]
public void FindBlockedColumns_Wildcard_BlocksAllSensitiveColumns()
{
    var blocked = new[] { ("public", "users", "email"), ("public", "users", "ssn") };
    var hits = SqlColumnChecker.FindBlockedColumns("SELECT * FROM users", blocked);
    Assert.Equal(2, hits.Count);
}

[Fact]
public void FindBlockedColumns_PriceVsPriceType_OnlyExactNameBlocked()
{
    var blocked = new[] { ("public", "orders", "price") };
    var hits = SqlColumnChecker.FindBlockedColumns("SELECT price_type FROM orders", blocked);
    Assert.Empty(hits);
}

[Fact]
public void FindBlockedColumns_NoSensitiveColumns_ReturnsEmpty()
{
    var hits = SqlColumnChecker.FindBlockedColumns("SELECT email FROM users", []);
    Assert.Empty(hits);
}

[Fact]
public void FindBlockedColumns_UppercaseColumn_MatchesCaseInsensitively()
{
    // Users may write column names in any casing; matching must be case-insensitive.
    var blocked = new[] { ("public", "users", "email") };
    var hits = SqlColumnChecker.FindBlockedColumns("SELECT EMAIL FROM users", blocked);
    Assert.Single(hits);
}

[Fact]
public void FindBlockedColumns_SchemaQualified_MatchesColumnName()
{
    // schema.table.email tokenises into three separate identifiers; "email" is still found.
    var blocked = new[] { ("public", "users", "email") };
    var hits = SqlColumnChecker.FindBlockedColumns(
        "SELECT public.users.email FROM public.users", blocked);
    Assert.Single(hits);
}

[Fact]
public void FindBlockedColumns_CTE_DetectsColumnInCteBody()
{
    var blocked = new[] { ("public", "users", "email") };
    var sql = """
        WITH cte AS (SELECT id, email FROM users WHERE active = true)
        SELECT id FROM cte
        """;
    var hits = SqlColumnChecker.FindBlockedColumns(sql, blocked);
    Assert.Single(hits);
}
```

- [ ] **Step 11: Run all checker tests**

```bash
dotnet test tests/IntegrationTests \
  --filter "FullyQualifiedName~SqlColumnCheckerTests"
```
Expected: all PASS.

- [ ] **Step 12: Commit**

```bash
git add src/SluiceBase.Api/Queries/SqlTokenizer.cs \
        src/SluiceBase.Api/Queries/SqlColumnChecker.cs \
        tests/IntegrationTests/SqlTokenizerTests.cs \
        tests/IntegrationTests/SqlColumnCheckerTests.cs
git commit -m "feat: add SqlTokenizer and SqlColumnChecker for sensitive column enforcement"
```

---

## Task 6: QueryEndpoints Enforcement

**Files:**
- Modify: `src/SluiceBase.Api/Endpoints/QueryEndpoints.cs`

- [ ] **Step 1: Add enforcement block to ExecuteQuery**

Open `src/SluiceBase.Api/Endpoints/QueryEndpoints.cs`.

First, update the method signature to add `ForbidHttpResult` and change `BadRequest<string>` to include the new error type. The full return type becomes:

```csharp
private static async Task<Results<Ok<QueryResponse>, NotFound, BadRequest<string>, ForbidHttpResult>> ExecuteQuery(
```

(This is unchanged from the current signature — the new 403 is still `ForbidHttpResult` but we'll return it with a body using `TypedResults.Problem` which maps to a `ProblemHttpResult`.)

Update the return type to add `ProblemHttpResult`:

```csharp
private static async Task<Results<Ok<QueryResponse>, NotFound, BadRequest<string>, ForbidHttpResult, ProblemHttpResult>> ExecuteQuery(
```

Then, after the existing `hasRole` check (around line 52), add the sensitive column enforcement block:

```csharp
// ── sensitive column check ────────────────────────────────────────────────
var sensitiveColumns = await db.SensitiveColumns
    .AsNoTracking()
    .Where(c => c.DatabaseId == database.Id)
    .ToListAsync(ct);

if (sensitiveColumns.Count > 0)
{
    var bypassedIds = await db.UserColumnBypasses
        .AsNoTracking()
        .Where(b => b.UserId == user!.Id
                 && sensitiveColumns.Select(c => c.Id).Contains(b.SensitiveColumnId))
        .Select(b => b.SensitiveColumnId)
        .ToHashSetAsync(ct);

    var blockedSet = sensitiveColumns
        .Where(c => !bypassedIds.Contains(c.Id))
        .Select(c => (c.SchemaName.ToLowerInvariant(), c.TableName.ToLowerInvariant(), c.ColumnName.ToLowerInvariant()))
        .ToHashSet();

    // Build tableColumns map for SELECT * expansion using cached schema
    // (fetch is done only when sensitive columns exist and the query might use *)
    var tableColumnsMap = new Dictionary<(string, string), string[]>();
    if (request.Sql.Contains('*'))
    {
        try
        {
            var connStr = await connectionFactory.GetConnectionStringAsync(database.Id, CredentialKind.Read, ct);
            var schemaTree = await targetEngine.GetSchemaAsync(connStr, ct);
            foreach (var schema in schemaTree.Schemas)
            foreach (var table in schema.Tables)
                tableColumnsMap[(schema.Name.ToLowerInvariant(), table.Name.ToLowerInvariant())] =
                    table.Columns.Select(c => c.Name.ToLowerInvariant()).ToArray();
        }
        catch { /* schema fetch failure — wildcards won't expand, non-wildcard check still runs */ }
    }

    var hits = SqlColumnChecker.FindBlockedColumns(request.Sql, blockedSet, tableColumnsMap);

    if (hits.Count > 0)
    {
        var blocked = hits.Select(h => new { schema = h.Schema, table = h.Table, column = h.Column }).ToArray();
        var logEntry = QueryLog.Create(user?.Id, database.Id, request.Sql,
            QueryLogStatus.Blocked, startedAt, null, null,
            $"Sensitive columns: {string.Join(", ", hits.Select(h => $"{h.Schema}.{h.Table}.{h.Column}"))}");
        db.QueryLogs.Add(logEntry);
        await db.SaveChangesAsync(ct);

        return TypedResults.Problem(
            statusCode: StatusCodes.Status403Forbidden,
            title: "Sensitive columns",
            type: "sensitive_columns",
            extensions: new Dictionary<string, object?> { ["columns"] = blocked });
    }
}
```

Add the `using SluiceBase.Api.Queries;` import at the top of the file.

- [ ] **Step 2: Build**

```bash
dotnet build src/SluiceBase.Api/SluiceBase.Api.csproj
```
Expected: no errors.

- [ ] **Step 3: Commit**

```bash
git add src/SluiceBase.Api/Endpoints/QueryEndpoints.cs
git commit -m "feat: enforce sensitive column restrictions in query execution"
```

---

## Task 7: SchemaEndpoints — IsRestricted Annotation

**Files:**
- Modify: `src/SluiceBase.Api/Endpoints/SchemaEndpoints.cs`

- [ ] **Step 1: Annotate columns with IsRestricted**

Open `src/SluiceBase.Api/Endpoints/SchemaEndpoints.cs`. After fetching the schema tree, annotate each column. Replace the `return TypedResults.Ok(tree)` line with:

```csharp
var sensitiveColumns = await db.SensitiveColumns
    .AsNoTracking()
    .Where(c => c.DatabaseId == databaseId)
    .ToListAsync(ct);

if (sensitiveColumns.Count == 0)
    return TypedResults.Ok(tree);

var bypassedIds = await db.UserColumnBypasses
    .AsNoTracking()
    .Where(b => b.UserId == user!.Id
             && sensitiveColumns.Select(c => c.Id).Contains(b.SensitiveColumnId))
    .Select(b => b.SensitiveColumnId)
    .ToHashSetAsync(ct);

var restrictedKeys = sensitiveColumns
    .Where(c => !bypassedIds.Contains(c.Id))
    .Select(c => (c.SchemaName.ToLowerInvariant(), c.TableName.ToLowerInvariant(), c.ColumnName.ToLowerInvariant()))
    .ToHashSet();

var annotatedSchemas = tree.Schemas.Select(s =>
    new SchemaInfo(s.Name,
        s.Tables.Select(t =>
            new TableInfo(t.Name,
                t.Columns.Select(c =>
                    new ColumnInfo(
                        c.Name, c.DataType, c.IsNullable,
                        restrictedKeys.Contains((
                            s.Name.ToLowerInvariant(),
                            t.Name.ToLowerInvariant(),
                            c.Name.ToLowerInvariant()))
                    )).ToList()
            )).ToList()
    )).ToList();

return TypedResults.Ok(new SchemaTree(annotatedSchemas));
```

Also update the method signature to inject `AppDbContext db` and remove the redundant `user` if already in scope. The full updated method signature:

```csharp
private static async Task<Results<Ok<SchemaTree>, NotFound, BadRequest<string>, ForbidHttpResult>> GetSchema(
    DatabaseId databaseId,
    AppDbContext db,
    ICurrentUserAccessor currentUser,
    IServerConnectionFactory connectionFactory,
    ITargetEngine targetEngine,
    CancellationToken ct)
```

`db` is already in the signature — no change needed there.

Add using statements: `using SluiceBase.Core.Schemas;` is already present; add the Permissions namespace if not already included.

- [ ] **Step 2: Build**

```bash
dotnet build src/SluiceBase.Api/SluiceBase.Api.csproj
```
Expected: no errors.

- [ ] **Step 3: Regenerate OpenAPI spec**

```bash
dotnet build src/SluiceBase.Api/SluiceBase.Api.csproj --configuration Release 2>&1 | tail -5
```
Expected: `openapi.json` regenerated with `isRestricted` in `ColumnInfo`.

Verify: `grep isRestricted src/SluiceBase.Api/openapi.json`
Expected: `"isRestricted": { "type": "boolean" }` or similar.

- [ ] **Step 4: Regenerate frontend types**

```bash
cd src/frontend && npm run gen:api && cd ../..
```
Expected: `src/frontend/src/api/schema.ts` now includes `isRestricted: boolean` in `ColumnInfo`.

Verify: `grep isRestricted src/frontend/src/api/schema.ts`

- [ ] **Step 5: Commit**

```bash
git add src/SluiceBase.Api/Endpoints/SchemaEndpoints.cs \
        src/SluiceBase.Api/openapi.json \
        src/frontend/src/api/schema.ts
git commit -m "feat: annotate schema columns with IsRestricted for the current user"
```

---

## Task 8: Integration Tests — SensitiveColumnEndpoints

**Files:**
- Create: `tests/IntegrationTests/SensitiveColumnEndpointTests.cs`
- Create: `tests/IntegrationTests/Supports/SensitiveColumnTestHelper.cs`

- [ ] **Step 1: Create SensitiveColumnTestHelper**

```csharp
// tests/IntegrationTests/Supports/SensitiveColumnTestHelper.cs
using System.Net.Http.Json;

namespace IntegrationTests.Supports;

internal static class SensitiveColumnTestHelper
{
    public static async Task<string> MarkColumnAsync(
        AuthenticatedSession adminSession,
        string databaseId,
        string schemaName,
        string tableName,
        string columnName,
        string xsrf,
        CancellationToken ct)
    {
        using var req = new HttpRequestMessage(
            HttpMethod.Post, $"/api/admin/database/{databaseId}/sensitive-column");
        req.Headers.Add("X-XSRF-TOKEN", xsrf);
        req.Content = JsonContent.Create(new { schemaName, tableName, columnName });
        var resp = await adminSession.Client.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        var list = await adminSession.Client.GetFromJsonAsync<SensitiveColumnListBody>(
            $"/api/admin/database/{databaseId}/sensitive-column", ct);
        return list!.Columns
            .Single(c => c.SchemaName == schemaName && c.TableName == tableName && c.ColumnName == columnName)
            .Id;
    }

    public static async Task GrantBypassAsync(
        AuthenticatedSession adminSession,
        string databaseId,
        string sensitiveColumnId,
        string userId,
        string xsrf,
        CancellationToken ct)
    {
        using var req = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/admin/database/{databaseId}/sensitive-column/{sensitiveColumnId}/bypass");
        req.Headers.Add("X-XSRF-TOKEN", xsrf);
        req.Content = JsonContent.Create(new { userId });
        (await adminSession.Client.SendAsync(req, ct)).EnsureSuccessStatusCode();
    }

    public sealed record SensitiveColumnListBody(SensitiveColumnRow[] Columns);
    public sealed record SensitiveColumnRow(string Id, string SchemaName, string TableName, string ColumnName);
}
```

- [ ] **Step 2: Create SensitiveColumnEndpointTests**

```csharp
// tests/IntegrationTests/SensitiveColumnEndpointTests.cs
using System.Net;
using System.Net.Http.Json;
using Aspire.Hosting.Testing;
using IntegrationTests.Supports;
using Npgsql;
using SluiceBase.Api.Endpoints;
using SluiceBase.Core.Permissions;

namespace IntegrationTests;

public class SensitiveColumnEndpointTests(SluiceBaseStackFactory factory)
{
    private KeycloakLoginHelper LoginHelper => new(factory.InitialisedApp);

    private static HttpRequestMessage MutationRequest(
        HttpMethod method, string url, string xsrf, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Add("X-XSRF-TOKEN", xsrf);
        if (body is not null) req.Content = JsonContent.Create(body);
        return req;
    }

    private async Task<(AuthenticatedSession Session, string Xsrf, string DatabaseId, string AliceId)>
        AliceWithDatabaseAsync(CancellationToken ct)
    {
        var session = await LoginHelper.SignInAsync("alice", "dev", ct);
        var xsrf = await session.FetchXsrfTokenAsync(ct);

        var users = await session.Client.GetFromJsonAsync<ListUserBody>("/api/admin/user", ct);
        var alice = Assert.Single(users!.Users, u => u.Email == "alice@example.com");

        using var grantServer = MutationRequest(HttpMethod.Post,
            $"/api/admin/user/{alice.Id}/permission", xsrf,
            new { permission = Permissions.ServerManage });
        (await session.Client.SendAsync(grantServer, ct)).EnsureSuccessStatusCode();

        var blueConnStr = await factory.InitialisedApp.GetConnectionStringAsync("blue-appdb", ct);
        var blueBuilder = new NpgsqlConnectionStringBuilder(blueConnStr!);

        var serverName = $"sc-{Guid.NewGuid():N}"[..20];
        using var sReq = MutationRequest(HttpMethod.Post, "/api/server", xsrf,
            new ServerEndpoints.CreateServerRequest(serverName, "postgres", blueBuilder.Host!, blueBuilder.Port));
        var sResp = await session.Client.SendAsync(sReq, ct);
        sResp.EnsureSuccessStatusCode();
        var server = (await sResp.Content.ReadFromJsonAsync<ServerEndpoints.ServerResponse>(ct))!;

        using var cReq = MutationRequest(HttpMethod.Post, $"/api/server/{server.Id}/credential", xsrf,
            new CredentialEndpoints.AddCredentialRequest("read", blueBuilder.Username!, blueBuilder.Password!));
        var cResp = await session.Client.SendAsync(cReq, ct);
        cResp.EnsureSuccessStatusCode();
        var cred = (await cResp.Content.ReadFromJsonAsync<CredentialEndpoints.CredentialResponse>(ct))!;

        using var dbReq = MutationRequest(HttpMethod.Post, $"/api/server/{server.Id}/database", xsrf,
            new DatabaseEndpoints.AddDatabaseRequest("App DB", blueBuilder.Database ?? "appdb", cred.Id));
        var dbResp = await session.Client.SendAsync(dbReq, ct);
        dbResp.EnsureSuccessStatusCode();
        var db = (await dbResp.Content.ReadFromJsonAsync<DatabaseEndpoints.DatabaseResponse>(ct))!;

        return (session, xsrf, db.Id.ToString(), alice.Id);
    }

    [Fact]
    public async Task ListSensitiveColumns_Anonymous_Returns401()
    {
        using var client = factory.InitialisedApp.CreateHttpClient("api", "https");
        var resp = await client.GetAsync(
            $"/api/admin/database/{Guid.NewGuid()}/sensitive-column",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task ListSensitiveColumns_Bob_Returns403()
    {
        using var session = await LoginHelper.SignInAsync("bob", "dev", TestContext.Current.CancellationToken);
        var resp = await session.Client.GetAsync(
            $"/api/admin/database/{Guid.NewGuid()}/sensitive-column",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task MarkAndList_RoundTrip()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf, databaseId, _) = await AliceWithDatabaseAsync(ct);
        using var _ = session;

        using var markReq = MutationRequest(HttpMethod.Post,
            $"/api/admin/database/{databaseId}/sensitive-column", xsrf,
            new { schemaName = "public", tableName = "users", columnName = "email" });
        var markResp = await session.Client.SendAsync(markReq, ct);
        Assert.Equal(HttpStatusCode.Created, markResp.StatusCode);

        var list = await session.Client.GetFromJsonAsync<SensitiveColumnTestHelper.SensitiveColumnListBody>(
            $"/api/admin/database/{databaseId}/sensitive-column", ct);
        Assert.Single(list!.Columns, c =>
            c.SchemaName == "public" && c.TableName == "users" && c.ColumnName == "email");
    }

    [Fact]
    public async Task Mark_Duplicate_Returns200()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf, databaseId, _) = await AliceWithDatabaseAsync(ct);
        using var _ = session;

        for (int i = 0; i < 2; i++)
        {
            using var req = MutationRequest(HttpMethod.Post,
                $"/api/admin/database/{databaseId}/sensitive-column", xsrf,
                new { schemaName = "public", tableName = "orders", columnName = "amount" });
            var resp = await session.Client.SendAsync(req, ct);
            Assert.True(resp.IsSuccessStatusCode);
        }
    }

    [Fact]
    public async Task Unmark_RemovesColumn()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf, databaseId, _) = await AliceWithDatabaseAsync(ct);
        using var _ = session;

        var columnId = await SensitiveColumnTestHelper.MarkColumnAsync(
            session, databaseId, "public", "users", "ssn", xsrf, ct);

        using var deleteReq = MutationRequest(
            HttpMethod.Delete, $"/api/admin/database/{databaseId}/sensitive-column/{columnId}", xsrf);
        (await session.Client.SendAsync(deleteReq, ct)).EnsureSuccessStatusCode();

        var list = await session.Client.GetFromJsonAsync<SensitiveColumnTestHelper.SensitiveColumnListBody>(
            $"/api/admin/database/{databaseId}/sensitive-column", ct);
        Assert.DoesNotContain(list!.Columns, c => c.Id == columnId);
    }

    [Fact]
    public async Task GrantAndRevokeBypass_RoundTrip()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf, databaseId, aliceId) = await AliceWithDatabaseAsync(ct);
        using var _ = session;

        var columnId = await SensitiveColumnTestHelper.MarkColumnAsync(
            session, databaseId, "public", "users", "phone", xsrf, ct);

        await SensitiveColumnTestHelper.GrantBypassAsync(session, databaseId, columnId, aliceId, xsrf, ct);

        var list = await session.Client.GetFromJsonAsync<FullSensitiveColumnListBody>(
            $"/api/admin/database/{databaseId}/sensitive-column", ct);
        var col = Assert.Single(list!.Columns, c => c.Id == columnId);
        Assert.Single(col.Bypasses, b => b.UserId == aliceId);

        using var revokeReq = MutationRequest(
            HttpMethod.Delete,
            $"/api/admin/database/{databaseId}/sensitive-column/{columnId}/bypass/{aliceId}", xsrf);
        (await session.Client.SendAsync(revokeReq, ct)).EnsureSuccessStatusCode();

        var listAfter = await session.Client.GetFromJsonAsync<FullSensitiveColumnListBody>(
            $"/api/admin/database/{databaseId}/sensitive-column", ct);
        var colAfter = Assert.Single(listAfter!.Columns, c => c.Id == columnId);
        Assert.Empty(colAfter.Bypasses);
    }

    private sealed record ListUserBody(UserRow[] Users);
    private sealed record UserRow(string Id, string Email);
    private sealed record FullSensitiveColumnListBody(FullSensitiveColumnRow[] Columns);
    private sealed record FullSensitiveColumnRow(string Id, string SchemaName, string TableName, string ColumnName, BypassRow[] Bypasses);
    private sealed record BypassRow(string UserId);
}
```

- [ ] **Step 3: Run integration tests**

```bash
dotnet test tests/IntegrationTests \
  --filter "FullyQualifiedName~SensitiveColumnEndpointTests"
```
Expected: all PASS.

- [ ] **Step 4: Commit**

```bash
git add tests/IntegrationTests/SensitiveColumnEndpointTests.cs \
        tests/IntegrationTests/Supports/SensitiveColumnTestHelper.cs
git commit -m "test: add SensitiveColumnEndpoints integration tests"
```

---

## Task 9: Integration Tests — Query Blocking

**Files:**
- Modify: `tests/IntegrationTests/QueryEndpointTest.cs`

- [ ] **Step 1: Add blocked query test**

Add these tests to `QueryEndpointTests`:

```csharp
[Fact]
public async Task PostQuery_SensitiveColumn_Returns403WithColumnList()
{
    var ct = TestContext.Current.CancellationToken;
    var (session, xsrf, databaseId) = await AuthorizedSessionWithBlueServerAsync(ct);
    using var _ = session;

    var users = await session.Client.GetFromJsonAsync<ListUserBody>("/api/admin/user", ct);
    var alice = users!.Users.Single(u => u.Email == "alice@example.com");

    // Mark a known column in the blue appdb schema as sensitive
    await SensitiveColumnTestHelper.MarkColumnAsync(
        session, databaseId.ToString(), "public", "query_log", "query_text", xsrf, ct);

    using var req = MutationRequest(HttpMethod.Post, "/api/query", xsrf,
        new QueryEndpoints.QueryRequest(databaseId, "SELECT query_text FROM query_log LIMIT 1"));
    var resp = await session.Client.SendAsync(req, ct);

    Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    var body = await resp.Content.ReadFromJsonAsync<ProblemBody>(ct);
    Assert.Equal("sensitive_columns", body!.Type);
    Assert.NotEmpty(body.Extensions.Columns);
}

[Fact]
public async Task PostQuery_SensitiveColumnWithBypass_Returns200()
{
    var ct = TestContext.Current.CancellationToken;
    var (session, xsrf, databaseId) = await AuthorizedSessionWithBlueServerAsync(ct);
    using var _ = session;

    var users = await session.Client.GetFromJsonAsync<ListUserBody>("/api/admin/user", ct);
    var alice = users!.Users.Single(u => u.Email == "alice@example.com");

    var columnId = await SensitiveColumnTestHelper.MarkColumnAsync(
        session, databaseId.ToString(), "public", "query_log", "query_text", xsrf, ct);
    await SensitiveColumnTestHelper.GrantBypassAsync(
        session, databaseId.ToString(), columnId, alice.Id, xsrf, ct);

    using var req = MutationRequest(HttpMethod.Post, "/api/query", xsrf,
        new QueryEndpoints.QueryRequest(databaseId, "SELECT query_text FROM query_log LIMIT 1"));
    var resp = await session.Client.SendAsync(req, ct);

    Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
}

[Fact]
public async Task PostQuery_SensitiveInWhereClause_Returns403()
{
    var ct = TestContext.Current.CancellationToken;
    var (session, xsrf, databaseId) = await AuthorizedSessionWithBlueServerAsync(ct);
    using var _ = session;

    await SensitiveColumnTestHelper.MarkColumnAsync(
        session, databaseId.ToString(), "public", "query_log", "query_text", xsrf, ct);

    using var req = MutationRequest(HttpMethod.Post, "/api/query", xsrf,
        new QueryEndpoints.QueryRequest(databaseId,
            "SELECT id FROM query_log WHERE query_text LIKE '%secret%'"));
    var resp = await session.Client.SendAsync(req, ct);

    Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
}
```

Add the response records inside the test class:

```csharp
private sealed record ProblemBody(string Type, ProblemExtensions Extensions);
private sealed record ProblemExtensions(ColumnRef[] Columns);
private sealed record ColumnRef(string Schema, string Table, string Column);
```

- [ ] **Step 2: Run query blocking tests**

```bash
dotnet test tests/IntegrationTests \
  --filter "FullyQualifiedName~QueryEndpointTests.PostQuery_Sensitive"
```
Expected: all PASS.

- [ ] **Step 3: Commit**

```bash
git add tests/IntegrationTests/QueryEndpointTest.cs
git commit -m "test: add query blocking integration tests for sensitive columns"
```

---

## Task 10: Integration Test — Schema IsRestricted

**Files:**
- Modify: `tests/IntegrationTests/SchemaEndpointTests.cs`

- [ ] **Step 1: Add IsRestricted test**

Add these two tests to `SchemaEndpointTests`:

```csharp
[Fact]
public async Task GetSchema_SensitiveColumn_MarkedAsRestricted()
{
    var ct = TestContext.Current.CancellationToken;
    var (session, xsrf, databaseId) = await AuthorizedSessionWithBlueServerAsync(ct);
    using var _ = session;

    await SensitiveColumnTestHelper.MarkColumnAsync(
        session, databaseId.ToString(), "public", "query_log", "query_text", xsrf, ct);

    var schema = await session.Client.GetFromJsonAsync<SchemaTreeBody>(
        $"/api/schema/{databaseId}", ct);

    var col = schema!.Schemas
        .Single(s => s.Name == "public").Tables
        .Single(t => t.Name == "query_log").Columns
        .Single(c => c.Name == "query_text");

    Assert.True(col.IsRestricted);
}

[Fact]
public async Task GetSchema_SensitiveColumnWithBypass_NotRestricted()
{
    var ct = TestContext.Current.CancellationToken;
    var (session, xsrf, databaseId) = await AuthorizedSessionWithBlueServerAsync(ct);
    using var _ = session;

    var users = await session.Client.GetFromJsonAsync<ListUserBody>("/api/admin/user", ct);
    var alice = users!.Users.Single(u => u.Email == "alice@example.com");

    var columnId = await SensitiveColumnTestHelper.MarkColumnAsync(
        session, databaseId.ToString(), "public", "query_log", "query_text", xsrf, ct);
    await SensitiveColumnTestHelper.GrantBypassAsync(
        session, databaseId.ToString(), columnId, alice.Id, xsrf, ct);

    var schema = await session.Client.GetFromJsonAsync<SchemaTreeBody>(
        $"/api/schema/{databaseId}", ct);

    var col = schema!.Schemas
        .Single(s => s.Name == "public").Tables
        .Single(t => t.Name == "query_log").Columns
        .Single(c => c.Name == "query_text");

    Assert.False(col.IsRestricted);
}
```

Add the response records to the test class (if not already present):

```csharp
private sealed record SchemaTreeBody(SchemaInfoBody[] Schemas);
private sealed record SchemaInfoBody(string Name, TableInfoBody[] Tables);
private sealed record TableInfoBody(string Name, ColumnInfoBody[] Columns);
private sealed record ColumnInfoBody(string Name, string DataType, bool IsNullable, bool IsRestricted);
private sealed record ListUserBody(UserRow[] Users);
private sealed record UserRow(string Id, string Email);
```

- [ ] **Step 2: Run schema tests**

```bash
dotnet test tests/IntegrationTests \
  --filter "FullyQualifiedName~SchemaEndpointTests"
```
Expected: all PASS (including the new IsRestricted tests).

- [ ] **Step 3: Commit**

```bash
git add tests/IntegrationTests/SchemaEndpointTests.cs
git commit -m "test: verify schema endpoint annotates IsRestricted correctly"
```

---

## Task 11: Frontend — Admin Tab + Query Workspace

**Files:**
- Modify: `src/frontend/src/api/hooks.ts`
- Modify: `src/frontend/src/routes/_authed/access.tsx`
- Modify: `src/frontend/src/routes/_authed/query/index.tsx`

The `schema.ts` types were already regenerated in Task 7 and include `isRestricted`.

- [ ] **Step 1: Add sensitive column API hooks**

Open `src/frontend/src/api/hooks.ts`. Add after the existing database role hooks:

```typescript
// ── Sensitive columns ─────────────────────────────────────────────────────

export type SensitiveColumnListResponse =
  paths["/api/admin/database/{databaseId}/sensitive-column"]["get"]["responses"][200]["content"]["application/json"];

export function useSensitiveColumns(databaseId: string | null) {
  return useQuery({
    queryKey: ["admin", "database", databaseId, "sensitive-column"] as const,
    enabled: databaseId !== null,
    queryFn: () =>
      apiRequest<void, SensitiveColumnListResponse>(
        `/api/admin/database/${databaseId}/sensitive-column`,
      ),
  });
}

export function useMarkSensitiveColumn() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({
      databaseId,
      schemaName,
      tableName,
      columnName,
    }: {
      databaseId: string;
      schemaName: string;
      tableName: string;
      columnName: string;
    }) =>
      apiRequest<
        paths["/api/admin/database/{databaseId}/sensitive-column"]["post"]["requestBody"]["content"]["application/json"],
        void
      >(`/api/admin/database/${databaseId}/sensitive-column`, {
        method: "POST",
        body: { schemaName, tableName, columnName },
      }),
    onSuccess: (_, { databaseId }) => {
      void qc.invalidateQueries({
        queryKey: ["admin", "database", databaseId, "sensitive-column"],
      });
    },
  });
}

export function useUnmarkSensitiveColumn() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({
      databaseId,
      sensitiveColumnId,
    }: {
      databaseId: string;
      sensitiveColumnId: string;
    }) =>
      apiRequest<void, void>(
        `/api/admin/database/${databaseId}/sensitive-column/${sensitiveColumnId}`,
        { method: "DELETE" },
      ),
    onSuccess: (_, { databaseId }) => {
      void qc.invalidateQueries({
        queryKey: ["admin", "database", databaseId, "sensitive-column"],
      });
    },
  });
}

export function useGrantColumnBypass() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({
      databaseId,
      sensitiveColumnId,
      userId,
    }: {
      databaseId: string;
      sensitiveColumnId: string;
      userId: string;
    }) =>
      apiRequest<
        paths["/api/admin/database/{databaseId}/sensitive-column/{sensitiveColumnId}/bypass"]["post"]["requestBody"]["content"]["application/json"],
        void
      >(
        `/api/admin/database/${databaseId}/sensitive-column/${sensitiveColumnId}/bypass`,
        { method: "POST", body: { userId } },
      ),
    onSuccess: (_, { databaseId }) => {
      void qc.invalidateQueries({
        queryKey: ["admin", "database", databaseId, "sensitive-column"],
      });
    },
  });
}

export function useRevokeColumnBypass() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({
      databaseId,
      sensitiveColumnId,
      userId,
    }: {
      databaseId: string;
      sensitiveColumnId: string;
      userId: string;
    }) =>
      apiRequest<void, void>(
        `/api/admin/database/${databaseId}/sensitive-column/${sensitiveColumnId}/bypass/${userId}`,
        { method: "DELETE" },
      ),
    onSuccess: (_, { databaseId }) => {
      void qc.invalidateQueries({
        queryKey: ["admin", "database", databaseId, "sensitive-column"],
      });
    },
  });
}
```

- [ ] **Step 2: Add Sensitive Columns tab to access.tsx**

Open `src/frontend/src/routes/_authed/access.tsx`.

Add `IconShieldLock` to the tabler icons import:
```typescript
import { IconDatabase, IconShieldLock, IconUser } from "@tabler/icons-react";
```

Add the new imports for hooks and schema:
```typescript
import {
  useAdminServers,
  useAssignDatabaseRole,
  useAssignUserRole,
  useDatabaseRoles,
  useGrantColumnBypass,
  useMarkSensitiveColumn,
  useRemoveDatabaseRole,
  useRevokeColumnBypass,
  useSensitiveColumns,
  useUnmarkSensitiveColumn,
  useUserRoles,
  useUsers,
  useSchema,
} from "@/api/hooks";
```

In `AccessPage`, add a third tab to the `Tabs.List`:
```tsx
<Tabs.Tab value="sensitive" leftSection={<IconShieldLock size={14} />}>
  Sensitive Columns
</Tabs.Tab>
```

And a new panel below the existing two:
```tsx
<Tabs.Panel value="sensitive" pt="md">
  <SensitiveColumnsTab />
</Tabs.Panel>
```

Add the `SensitiveColumnsTab` component:

```tsx
function SensitiveColumnsTab() {
  const servers = useAdminServers();
  const [selectedDb, setSelectedDb] = useState<
    (AdminDatabaseItem & { serverName: string }) | null
  >(null);

  return (
    <Group align="flex-start" gap="md">
      <Stack gap={4} style={{ minWidth: 220, maxWidth: 260 }}>
        <Text size="xs" fw={600} c="dimmed" tt="uppercase">
          Databases
        </Text>
        {(servers.data?.servers ?? []).map((s) => (
          <Stack key={s.id} gap={2}>
            <Text size="xs" c="dimmed" fw={500} pl={4}>
              {s.name}
            </Text>
            {s.databases.map((d) => (
              <NavLink
                key={d.id}
                label={d.displayName}
                active={selectedDb?.id === d.id}
                onClick={() =>
                  setSelectedDb({ ...d, serverName: s.name })
                }
                pl="lg"
              />
            ))}
          </Stack>
        ))}
      </Stack>
      {selectedDb && (
        <SensitiveColumnsPanel database={selectedDb} />
      )}
    </Group>
  );
}

function SensitiveColumnsPanel({
  database,
}: {
  database: AdminDatabaseItem & { serverName: string };
}) {
  const cols = useSensitiveColumns(database.id);
  const schema = useSchema(database.id);
  const mark = useMarkSensitiveColumn();
  const unmark = useUnmarkSensitiveColumn();
  const grantBypass = useGrantColumnBypass();
  const revokeBypass = useRevokeColumnBypass();
  const users = useUsers();

  const [addOpen, setAddOpen] = useState(false);

  return (
    <Stack gap="md" style={{ flex: 1 }}>
      <Group justify="space-between">
        <Text fw={500}>{database.displayName}</Text>
        <Button size="xs" onClick={() => setAddOpen(true)}>
          Mark column as sensitive
        </Button>
      </Group>

      {cols.isLoading && <Loader size="sm" />}

      {cols.data?.columns.length === 0 && (
        <Text size="sm" c="dimmed">
          No sensitive columns configured.
        </Text>
      )}

      {(cols.data?.columns ?? []).map((col) => (
        <Stack key={col.id} gap={4} p="xs" style={{ border: "1px solid var(--mantine-color-default-border)", borderRadius: 4 }}>
          <Group justify="space-between">
            <Text size="sm" fw={500}>
              {col.schemaName}.{col.tableName}.{col.columnName}
            </Text>
            <Button
              size="xs"
              color="red"
              variant="light"
              onClick={() =>
                unmark.mutate({ databaseId: database.id, sensitiveColumnId: col.id })
              }
            >
              Remove
            </Button>
          </Group>

          {col.bypasses.length > 0 && (
            <Table fz="xs">
              <Table.Thead>
                <Table.Tr>
                  <Table.Th>User</Table.Th>
                  <Table.Th>Granted</Table.Th>
                  <Table.Th />
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {col.bypasses.map((b) => (
                  <Table.Tr key={b.id}>
                    <Table.Td>{b.userEmail ?? b.userId}</Table.Td>
                    <Table.Td>
                      {new Date(b.grantedAt).toLocaleDateString()}
                    </Table.Td>
                    <Table.Td>
                      <Button
                        size="xs"
                        variant="subtle"
                        color="red"
                        onClick={() =>
                          revokeBypass.mutate({
                            databaseId: database.id,
                            sensitiveColumnId: col.id,
                            userId: b.userId,
                          })
                        }
                      >
                        Revoke
                      </Button>
                    </Table.Td>
                  </Table.Tr>
                ))}
              </Table.Tbody>
            </Table>
          )}

          <Select
            placeholder="Add bypass for user…"
            size="xs"
            data={(users.data?.users ?? []).map((u) => ({
              value: u.id,
              label: u.email,
            }))}
            onChange={(userId) => {
              if (userId) {
                grantBypass.mutate({
                  databaseId: database.id,
                  sensitiveColumnId: col.id,
                  userId,
                });
              }
            }}
            clearable
          />
        </Stack>
      ))}

      <Modal
        opened={addOpen}
        onClose={() => setAddOpen(false)}
        title="Mark columns as sensitive"
      >
        <MarkColumnsForm
          databaseId={database.id}
          schema={schema.data}
          onMark={(schemaName, tableName, columnName) =>
            mark.mutate(
              { databaseId: database.id, schemaName, tableName, columnName },
              { onSuccess: () => setAddOpen(false) },
            )
          }
        />
      </Modal>
    </Stack>
  );
}

function MarkColumnsForm({
  databaseId,
  schema,
  onMark,
}: {
  databaseId: string;
  schema: ReturnType<typeof useSchema>["data"];
  onMark: (schema: string, table: string, column: string) => void;
}) {
  const [selectedSchema, setSelectedSchema] = useState<string | null>(null);
  const [selectedTable, setSelectedTable] = useState<string | null>(null);
  const [selectedColumn, setSelectedColumn] = useState<string | null>(null);

  const schemaOptions = (schema?.schemas ?? []).map((s) => ({
    value: s.name,
    label: s.name,
  }));
  const tableOptions = (
    schema?.schemas.find((s) => s.name === selectedSchema)?.tables ?? []
  ).map((t) => ({ value: t.name, label: t.name }));
  const columnOptions = (
    schema?.schemas
      .find((s) => s.name === selectedSchema)
      ?.tables.find((t) => t.name === selectedTable)?.columns ?? []
  ).map((c) => ({ value: c.name, label: c.name }));

  return (
    <Stack gap="sm">
      <Select
        label="Schema"
        placeholder="Pick schema"
        data={schemaOptions}
        value={selectedSchema}
        onChange={(v) => {
          setSelectedSchema(v);
          setSelectedTable(null);
          setSelectedColumn(null);
        }}
      />
      <Select
        label="Table"
        placeholder="Pick table"
        data={tableOptions}
        value={selectedTable}
        disabled={!selectedSchema}
        onChange={(v) => {
          setSelectedTable(v);
          setSelectedColumn(null);
        }}
      />
      <Select
        label="Column"
        placeholder="Pick column"
        data={columnOptions}
        value={selectedColumn}
        disabled={!selectedTable}
        onChange={setSelectedColumn}
      />
      <Button
        disabled={!selectedSchema || !selectedTable || !selectedColumn}
        onClick={() => {
          if (selectedSchema && selectedTable && selectedColumn)
            onMark(selectedSchema, selectedTable, selectedColumn);
        }}
      >
        Mark as sensitive
      </Button>
    </Stack>
  );
}
```

Add missing Mantine imports (`Loader`, `Modal`, `NavLink`, `Select`, `Table`) to the import line at the top of the file.

- [ ] **Step 3: Update query workspace — schema sidebar + 403 error**

Open `src/frontend/src/routes/_authed/query/index.tsx`.

**3a. Update column rendering in the schema sidebar** to show restricted columns with a lock icon and dimmed style. Find the section rendering `{t.columns.map((c) => (` (around line 471) and replace the column rendering block:

```tsx
{t.columns.map((c) => (
  <Group key={c.name} gap="xs" px="xs" py={2} wrap="nowrap"
    style={c.isRestricted ? { opacity: 0.45 } : undefined}>
    {c.isRestricted && <IconLock size={10} color="var(--mantine-color-red-6)" />}
    <Text size="xs" style={{ minWidth: 0 }}>
      {c.name}
    </Text>
    <Code fz="xs">{c.dataType}</Code>
    {c.isNullable && (
      <Text size="xs" c="dimmed">
        null
      </Text>
    )}
  </Group>
))}
```

Add `IconLock` to the tabler icons import at the top of the file.

**3b. Update `handleTableClick`** to exclude restricted columns from the generated SELECT snippet:

```typescript
const handleTableClick = useCallback(
  (schemaName: string, tableName: string, columns: Array<{ name: string; isRestricted: boolean }>) => {
    const allowedCols = columns.filter((c) => !c.isRestricted);
    if (allowedCols.length === 0) return; // all columns restricted — button disabled
    const colList = allowedCols.map((c) => c.name).join(", ");
    const snippet = `SELECT ${colList}\nFROM ${schemaName}.${tableName}\nLIMIT 1000;\n`;
    setEditorContent((prev) =>
      prev.trimEnd() === "" ? snippet : `${prev.trimEnd()}\n\n${snippet}`,
    );
  },
  [setEditorContent],
);
```

**3c. Disable the generate button when all columns are restricted.** Find the `IconPlaylistAdd` button (around line 457). Update `onClick` and add `disabled`:

```tsx
<Button
  onClick={() => onTableClick(s.name, t.name, t.columns)}
  size="xs"
  variant="subtle"
  disabled={t.columns.every((c) => c.isRestricted)}
>
  <IconPlaylistAdd />
</Button>
```

**3d. Update `QueryResults` to handle the sensitive column 403.** In the `if (isError)` block (around line 269), replace with:

```tsx
if (isError) {
  const apiErr = error instanceof ApiError ? error : null;
  if (apiErr?.status === 403) {
    const body = apiErr.body as { type?: string; extensions?: { columns?: Array<{ schema: string; table: string; column: string }> } } | null;
    if (body?.type === "sensitive_columns") {
      return (
        <Alert color="orange" title="Query blocked — restricted columns" m="xs">
          <Text size="sm" mb="xs">
            Your query references columns you are not authorised to access:
          </Text>
          {(body.extensions?.columns ?? []).map((c, i) => (
            <Code key={i} display="block" fz="xs">
              {c.schema}.{c.table}.{c.column}
            </Code>
          ))}
        </Alert>
      );
    }
  }
  return (
    <Alert color="red" title="Request failed" m="xs">
      Could not reach the server. Check your connection and try again.
    </Alert>
  );
}
```

Update `QueryResults` props to include `error`:
```tsx
function QueryResults({
  result,
  isPending,
  isError,
  error,
}: {
  result: ExecuteQueryResponse | null;
  isPending: boolean;
  isError: boolean;
  error: unknown;
}) {
```

And at the call site, pass `error={executeQuery.error}`:
```tsx
<QueryResults
  result={executeQuery.data ?? null}
  isPending={executeQuery.isPending}
  isError={executeQuery.isError}
  error={executeQuery.error}
/>
```

Add `ApiError` to the import from `@/api/client`.

- [ ] **Step 4: TypeScript build check**

```bash
cd src/frontend && npx tsc --noEmit && cd ../..
```
Expected: no errors.

- [ ] **Step 5: Commit**

```bash
git add src/frontend/src/api/hooks.ts \
        src/frontend/src/routes/_authed/access.tsx \
        src/frontend/src/routes/_authed/query/index.tsx
git commit -m "feat: add sensitive column admin tab and query workspace indicators"
```

---

## Self-Review

**Spec coverage:**

| Spec section | Task(s) |
|---|---|
| `sensitive_column` table | Task 1, 3 |
| `user_column_bypass` table | Task 1, 3 |
| No migration seed data | Task 3 — migration generates empty tables |
| SQL parsing — all clauses | Task 5 (`SqlColumnChecker`) |
| SELECT * expansion | Task 5 |
| Reject with 403 + column list | Task 6 |
| Query logged as Blocked | Task 6, Task 2 |
| GET sensitive columns (with bypasses embedded) | Task 4 |
| POST / DELETE sensitive column | Task 4 |
| POST / DELETE bypass | Task 4 |
| `IsRestricted` in schema response | Task 7 |
| OpenAPI regen | Task 7 |
| Admin Sensitive Columns tab | Task 11 |
| Schema sidebar lock indicator | Task 11 |
| Generate button excludes restricted | Task 11 |
| Generate button disabled when all restricted | Task 11 |
| 403 error rendered with column list | Task 11 |

**Placeholder scan:** No TBD or TODO in task steps.

**Type consistency:** `SensitiveColumnHit` defined in Task 5 and used in Task 6. `SensitiveColumnId` defined in Task 1 and used in Task 4. `QueryLogStatus.Blocked` defined in Task 2 and used in Task 6. `IsRestricted` defined in Task 2 and used in Task 7.
