# Server / Credential / Database Schema Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the monolithic `Server` entity (which embeds credentials and a single database target) with three entities — `Server`, `Credential`, and `Database` — enabling multiple databases per server, shared credentials, and unambiguous audit references via `DatabaseId`.

**Architecture:** `Server` is the aggregate root; `Credential` and `Database` are soft-delete-only children whose lifecycle is controlled through `Server`. `QueryLog` and `UpdateRequest` replace `ServerId` with `DatabaseId`. The connection factory switches its parameter from `ServerId` to `DatabaseId`, joining through `Database → Server + Credential` and checking `IsDisabled` on both before building a connection string. `IsDisabled` replaces the former `IsEnabled` (default `false` = active).

**Tech Stack:** .NET 10, ASP.NET Core minimal APIs, EF Core 9 (Npgsql), Vogen value objects (`AddFactoryMethodForGuids`), React 18 + TanStack Query v5, Mantine v7, openapi-typescript (auto-generated `schema.ts`), xUnit + Aspire integration tests.

---

## File Structure

### New files
| File | Purpose |
|------|---------|
| `src/SluiceBase.Core/Servers/CredentialId.cs` | Vogen value object |
| `src/SluiceBase.Core/Servers/DatabaseId.cs` | Vogen value object |
| `src/SluiceBase.Core/Servers/Credential.cs` | Credential entity |
| `src/SluiceBase.Core/Servers/Database.cs` | Database entity |
| `src/SluiceBase.Api/Data/Configurations/CredentialConfiguration.cs` | EF config — `server_credential` |
| `src/SluiceBase.Api/Data/Configurations/DatabaseConfiguration.cs` | EF config — `server_database` |
| `src/SluiceBase.Api/Endpoints/CredentialEndpoints.cs` | CRUD for credentials |
| `src/SluiceBase.Api/Endpoints/DatabaseEndpoints.cs` | CRUD + test-connection for databases |
| `tests/IntegrationTests/CredentialEndpointTests.cs` | Integration tests |
| `tests/IntegrationTests/DatabaseEndpointTests.cs` | Integration tests |

### Modified files
| File | Change summary |
|------|---------------|
| `src/SluiceBase.Core/Servers/Server.cs` | Remove credential/database fields; add `IsDisabled`, `DeletedAt`, nav collections, `SoftDelete()` |
| `src/SluiceBase.Core/Queries/QueryLog.cs` | `ServerId?` → `DatabaseId?` |
| `src/SluiceBase.Core/Updates/UpdateRequest.cs` | `ServerId?` → `DatabaseId?`; nav `Server?` → `Database?` |
| `src/SluiceBase.Api/Data/AppDbContext.cs` | Add `Credentials` and `Databases` DbSets |
| `src/SluiceBase.Api/Data/Configurations/ServerConfiguration.cs` | Remove old columns; add `IsDisabled`, `DeletedAt`; partial unique index |
| `src/SluiceBase.Api/Data/Configurations/QueryLogConfiguration.cs` | FK `ServerId` → `DatabaseId` with `Restrict` |
| `src/SluiceBase.Api/Data/Configurations/UpdateRequestConfiguration.cs` | FK `ServerId` → `DatabaseId` with `Restrict` |
| `src/SluiceBase.Api/Data/Migrations/` | Drop all; recreate one `InitialSchema` migration |
| `src/SluiceBase.Api/Servers/IServerConnectionFactory.cs` | Parameter `ServerId` → `DatabaseId` |
| `src/SluiceBase.Api/Servers/ServerConnectionFactory.cs` | Full rewrite — loads Database → Server + Credential; `IsDisabled` guard |
| `src/SluiceBase.Api/Endpoints/ServerEndpoints.cs` | Simplified create/update; nested list response; soft-delete; remove test-connection |
| `src/SluiceBase.Api/Endpoints/SchemaEndpoint.cs` | Path param `serverId` → `databaseId` |
| `src/SluiceBase.Api/Endpoints/QueryEndpoints.cs` | Body field `serverId` → `databaseId` |
| `src/SluiceBase.Api/Endpoints/UpdateEndpoints.cs` | Body field `serverId` → `databaseId`; nav `Server` → `Database` |
| `src/SluiceBase.Api/Endpoints/EndpointMapper.cs` | Register `CredentialEndpoints` and `DatabaseEndpoints` |
| `src/AppHost/Extensions/DevServerSeed.cs` | Create server → credentials → database via raw SQL |
| `tests/IntegrationTests/ServerEndpointTests.cs` | Rewrite for new shape and soft-delete |
| `tests/IntegrationTests/QueryEndpointTest.cs` | Use `DatabaseId` helpers |
| `tests/IntegrationTests/SchemaEndpointTests.cs` | Use `DatabaseId` helpers |
| `tests/IntegrationTests/UpdateEndpointTests.cs` | Use `DatabaseId` helpers; disabled-server tests |
| `src/frontend/src/api/hooks.ts` | New credential/database hooks; update existing hooks |
| `src/frontend/src/routes/_authed/server.tsx` | Hierarchical server → credentials/databases UI |
| `src/frontend/src/routes/_authed/query.tsx` | Server selector → database selector |
| `src/frontend/src/routes/_authed/update/new.tsx` | `serverId` → `databaseId` |
| `src/frontend/src/api/__tests__/server-hooks.test.ts` | New response shape |
| `src/frontend/src/api/__tests__/query-hooks.test.ts` | `databaseId` |
| `src/frontend/src/api/__tests__/schema-hooks.test.ts` | `databaseId` |
| `src/frontend/src/api/__tests__/update-hooks.test.ts` | `databaseId` |

---

## Task 1: Add CredentialId and DatabaseId value objects

**Files:**
- Create: `src/SluiceBase.Core/Servers/CredentialId.cs`
- Create: `src/SluiceBase.Core/Servers/DatabaseId.cs`

- [ ] **Step 1: Create CredentialId**

```csharp
// src/SluiceBase.Core/Servers/CredentialId.cs
using Vogen;

namespace SluiceBase.Core.Servers;

[ValueObject<Guid>(conversions: Conversions.SystemTextJson, customizations: Customizations.AddFactoryMethodForGuids)]
public readonly partial struct CredentialId;
```

- [ ] **Step 2: Create DatabaseId**

```csharp
// src/SluiceBase.Core/Servers/DatabaseId.cs
using Vogen;

namespace SluiceBase.Core.Servers;

[ValueObject<Guid>(conversions: Conversions.SystemTextJson, customizations: Customizations.AddFactoryMethodForGuids)]
public readonly partial struct DatabaseId;
```

- [ ] **Step 3: Verify compilation**

```bash
dotnet build src/SluiceBase.Core/SluiceBase.Core.csproj
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 4: Commit**

```bash
git add src/SluiceBase.Core/Servers/CredentialId.cs src/SluiceBase.Core/Servers/DatabaseId.cs
git commit -m "feat: add CredentialId and DatabaseId value objects"
```

---

## Task 2: Create Credential entity

**Files:**
- Create: `src/SluiceBase.Core/Servers/Credential.cs`

- [ ] **Step 1: Create entity**

```csharp
// src/SluiceBase.Core/Servers/Credential.cs
namespace SluiceBase.Core.Servers;

public sealed class Credential
{
#pragma warning disable CS8618
    private Credential() { }
#pragma warning restore CS8618

    public CredentialId Id { get; private set; }
    public ServerId ServerId { get; private set; }
    public string Label { get; private set; }
    public string Username { get; private set; }
    public string EncryptedPassword { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static Credential Create(
        ServerId serverId,
        string label,
        string username,
        string encryptedPassword,
        DateTimeOffset at) =>
        new()
        {
            Id = CredentialId.FromNewVersion7Guid(),
            ServerId = serverId,
            Label = label,
            Username = username,
            EncryptedPassword = encryptedPassword,
            CreatedAt = at,
            UpdatedAt = at,
        };

    public void Update(string label, string username, string? encryptedPassword, DateTimeOffset at)
    {
        Label = label;
        Username = username;
        if (encryptedPassword is not null)
            EncryptedPassword = encryptedPassword;
        UpdatedAt = at;
    }

    public void SoftDelete(DateTimeOffset at)
    {
        DeletedAt = at;
        UpdatedAt = at;
    }
}
```

- [ ] **Step 2: Build**

```bash
dotnet build src/SluiceBase.Core/SluiceBase.Core.csproj
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Commit**

```bash
git add src/SluiceBase.Core/Servers/Credential.cs
git commit -m "feat: add Credential entity"
```

---

## Task 3: Create Database entity

**Files:**
- Create: `src/SluiceBase.Core/Servers/Database.cs`

- [ ] **Step 1: Create entity**

```csharp
// src/SluiceBase.Core/Servers/Database.cs
namespace SluiceBase.Core.Servers;

public sealed class Database
{
#pragma warning disable CS8618
    private Database() { }
#pragma warning restore CS8618

    public DatabaseId Id { get; private set; }
    public ServerId ServerId { get; private set; }
    public string DisplayName { get; private set; }
    public string DatabaseName { get; private set; }
    public CredentialId ReadCredentialId { get; private set; }
    public CredentialId? WriteCredentialId { get; private set; }
    public bool IsDisabled { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public bool CanWrite => WriteCredentialId.HasValue;

    // Loaded by EF for the connection factory
    public Server? Server { get; private set; }

    public static Database Create(
        ServerId serverId,
        string displayName,
        string databaseName,
        CredentialId readCredentialId,
        CredentialId? writeCredentialId,
        DateTimeOffset at) =>
        new()
        {
            Id = DatabaseId.FromNewVersion7Guid(),
            ServerId = serverId,
            DisplayName = displayName,
            DatabaseName = databaseName,
            ReadCredentialId = readCredentialId,
            WriteCredentialId = writeCredentialId,
            IsDisabled = false,
            CreatedAt = at,
            UpdatedAt = at,
        };

    public void Update(
        string displayName,
        string databaseName,
        CredentialId readCredentialId,
        CredentialId? writeCredentialId,
        bool isDisabled,
        DateTimeOffset at)
    {
        DisplayName = displayName;
        DatabaseName = databaseName;
        ReadCredentialId = readCredentialId;
        WriteCredentialId = writeCredentialId;
        IsDisabled = isDisabled;
        UpdatedAt = at;
    }

    public void SoftDelete(DateTimeOffset at)
    {
        DeletedAt = at;
        UpdatedAt = at;
    }
}
```

- [ ] **Step 2: Build**

```bash
dotnet build src/SluiceBase.Core/SluiceBase.Core.csproj
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Commit**

```bash
git add src/SluiceBase.Core/Servers/Database.cs
git commit -m "feat: add Database entity"
```

---

## Task 4: Refactor Server entity

**Files:**
- Modify: `src/SluiceBase.Core/Servers/Server.cs`

- [ ] **Step 1: Replace Server.cs**

```csharp
// src/SluiceBase.Core/Servers/Server.cs
namespace SluiceBase.Core.Servers;

public sealed class Server
{
#pragma warning disable CS8618
    private Server() { }
#pragma warning restore CS8618

    public ServerId Id { get; private set; }
    public string Name { get; private set; }
    public string Kind { get; private set; }
    public string Host { get; private set; }
    public int Port { get; private set; }
    public bool IsDisabled { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IList<Credential> Credentials { get; private set; } = [];
    public IList<Database> Databases { get; private set; } = [];

    public static Server Create(string name, string kind, string host, int port, DateTimeOffset at) =>
        new()
        {
            Id = ServerId.FromNewVersion7Guid(),
            Name = name,
            Kind = kind,
            Host = host,
            Port = port,
            IsDisabled = false,
            CreatedAt = at,
            UpdatedAt = at,
        };

    public void Update(string name, string host, int port, string kind, bool isDisabled, DateTimeOffset at)
    {
        Name = name;
        Host = host;
        Port = port;
        Kind = kind;
        IsDisabled = isDisabled;
        UpdatedAt = at;
    }

    public void SoftDelete(DateTimeOffset at)
    {
        DeletedAt = at;
        UpdatedAt = at;
        foreach (var c in Credentials)
            c.SoftDelete(at);
        foreach (var d in Databases)
            d.SoftDelete(at);
    }
}
```

- [ ] **Step 2: Build Core project (expect API project to fail — that's fine for now)**

```bash
dotnet build src/SluiceBase.Core/SluiceBase.Core.csproj
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Commit**

```bash
git add src/SluiceBase.Core/Servers/Server.cs
git commit -m "feat: refactor Server entity — remove embedded credentials/database, add Credential and Database children"
```

---

## Task 5: Add CredentialConfiguration and DatabaseConfiguration

**Files:**
- Create: `src/SluiceBase.Api/Data/Configurations/CredentialConfiguration.cs`
- Create: `src/SluiceBase.Api/Data/Configurations/DatabaseConfiguration.cs`

- [ ] **Step 1: Create CredentialConfiguration**

```csharp
// src/SluiceBase.Api/Data/Configurations/CredentialConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SluiceBase.Core.Servers;

namespace SluiceBase.Api.Data.Configurations;

internal sealed class CredentialConfiguration : IEntityTypeConfiguration<Credential>
{
    public void Configure(EntityTypeBuilder<Credential> builder)
    {
        builder.ToTable("server_credential");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Label).HasMaxLength(128).IsRequired();
        builder.Property(c => c.Username).HasMaxLength(128).IsRequired();
        builder.Property(c => c.EncryptedPassword).HasMaxLength(4096).IsRequired();
        builder.HasOne<Server>()
            .WithMany(s => s.Credentials)
            .HasForeignKey(c => c.ServerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
```

- [ ] **Step 2: Create DatabaseConfiguration**

The class is named `Database` which would collide with `System.Data` imports. Use an alias in this file only.

```csharp
// src/SluiceBase.Api/Data/Configurations/DatabaseConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SluiceBase.Core.Servers;
using DbEntity = SluiceBase.Core.Servers.Database;

namespace SluiceBase.Api.Data.Configurations;

internal sealed class DatabaseConfiguration : IEntityTypeConfiguration<DbEntity>
{
    public void Configure(EntityTypeBuilder<DbEntity> builder)
    {
        builder.ToTable("server_database");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.DisplayName).HasMaxLength(128).IsRequired();
        builder.Property(d => d.DatabaseName).HasMaxLength(255).IsRequired();
        builder.HasOne(d => d.Server)
            .WithMany(s => s.Databases)
            .HasForeignKey(d => d.ServerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Credential>()
            .WithMany()
            .HasForeignKey(d => d.ReadCredentialId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Credential>()
            .WithMany()
            .HasForeignKey(d => d.WriteCredentialId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add src/SluiceBase.Api/Data/Configurations/CredentialConfiguration.cs \
        src/SluiceBase.Api/Data/Configurations/DatabaseConfiguration.cs
git commit -m "feat: add EF configurations for Credential and Database"
```

---

## Task 6: Update ServerConfiguration, QueryLogConfiguration, UpdateRequestConfiguration

**Files:**
- Modify: `src/SluiceBase.Api/Data/Configurations/ServerConfiguration.cs`
- Modify: `src/SluiceBase.Api/Data/Configurations/QueryLogConfiguration.cs`
- Modify: `src/SluiceBase.Api/Data/Configurations/UpdateRequestConfiguration.cs`

- [ ] **Step 1: Replace ServerConfiguration**

Remove all credential and database column config. Add `IsDisabled` and `DeletedAt`. Change the unique index on `Name` to a partial index that ignores soft-deleted rows.

```csharp
// src/SluiceBase.Api/Data/Configurations/ServerConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SluiceBase.Core.Servers;

namespace SluiceBase.Api.Data.Configurations;

internal sealed class ServerConfiguration : IEntityTypeConfiguration<Server>
{
    public void Configure(EntityTypeBuilder<Server> builder)
    {
        builder.ToTable("server");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Name).HasMaxLength(128).IsRequired();
        builder.HasIndex(s => s.Name).IsUnique().HasFilter("deleted_at IS NULL");
        builder.Property(s => s.Kind).HasMaxLength(32).IsRequired();
        builder.Property(s => s.Host).HasMaxLength(255).IsRequired();
    }
}
```

- [ ] **Step 2: Replace QueryLogConfiguration**

Replace the `Server` FK with a `Database` FK using `Restrict` (databases are never hard-deleted).

```csharp
// src/SluiceBase.Api/Data/Configurations/QueryLogConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SluiceBase.Core.Queries;
using SluiceBase.Core.Servers;
using SluiceBase.Core.Users;

namespace SluiceBase.Api.Data.Configurations;

internal sealed class QueryLogConfiguration : IEntityTypeConfiguration<QueryLog>
{
    public void Configure(EntityTypeBuilder<QueryLog> builder)
    {
        builder.ToTable("query_log");
        builder.HasKey(q => q.Id);
        builder.Property(q => q.QueryText).IsRequired();
        builder.Property(q => q.Status).HasMaxLength(16).IsRequired();

        builder.HasOne<User>().WithMany()
            .HasForeignKey(q => q.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<Database>().WithMany()
            .HasForeignKey(q => q.DatabaseId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
```

- [ ] **Step 3: Replace UpdateRequestConfiguration**

Replace the `Server` FK with a `Database` FK using `Restrict`. Keep all user FKs as `SetNull`.

```csharp
// src/SluiceBase.Api/Data/Configurations/UpdateRequestConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SluiceBase.Core.Servers;
using SluiceBase.Core.Updates;

namespace SluiceBase.Api.Data.Configurations;

internal sealed class UpdateRequestConfiguration : IEntityTypeConfiguration<UpdateRequest>
{
    public void Configure(EntityTypeBuilder<UpdateRequest> builder)
    {
        builder.ToTable("update_request");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.SqlText).IsRequired();
        builder.Property(r => r.Reason).IsRequired();
        builder.Property(r => r.Status).HasMaxLength(16).IsRequired();

        builder.HasOne(r => r.Database).WithMany()
            .HasForeignKey(r => r.DatabaseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Submitter).WithMany()
            .HasForeignKey(r => r.SubmitterId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(r => r.Reviewer).WithMany()
            .HasForeignKey(r => r.ReviewerId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(r => r.Executor).WithMany()
            .HasForeignKey(r => r.ExecutorId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(r => r.CancelledBy).WithMany()
            .HasForeignKey(r => r.CancelledById)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
```

- [ ] **Step 4: Commit**

```bash
git add src/SluiceBase.Api/Data/Configurations/ServerConfiguration.cs \
        src/SluiceBase.Api/Data/Configurations/QueryLogConfiguration.cs \
        src/SluiceBase.Api/Data/Configurations/UpdateRequestConfiguration.cs
git commit -m "feat: update EF configurations for new schema — partial unique index, DatabaseId FKs"
```

---

## Task 7: Update QueryLog and UpdateRequest entities

**Files:**
- Modify: `src/SluiceBase.Core/Queries/QueryLog.cs`
- Modify: `src/SluiceBase.Core/Updates/UpdateRequest.cs`

- [ ] **Step 1: Update QueryLog — replace ServerId with DatabaseId**

```csharp
// src/SluiceBase.Core/Queries/QueryLog.cs
using SluiceBase.Core.Servers;
using SluiceBase.Core.Users;

namespace SluiceBase.Core.Queries;

public sealed class QueryLog
{
#pragma warning disable CS8618
    private QueryLog() { }
#pragma warning restore CS8618

    public QueryLogId Id { get; private set; }
    public UserId? UserId { get; private set; }
    public DatabaseId? DatabaseId { get; private set; }
    public string QueryText { get; private set; }
    public QueryLogStatus Status { get; private set; }
    public DateTimeOffset ExecutedAt { get; private set; }
    public int? DurationMs { get; private set; }
    public int? RowCount { get; private set; }
    public string? Error { get; private set; }

    public static QueryLog Create(
        UserId? userId,
        DatabaseId? databaseId,
        string queryText,
        QueryLogStatus status,
        DateTimeOffset executedAt,
        int? durationMs,
        int? rowCount,
        string? error) => new()
    {
        Id = QueryLogId.FromNewVersion7Guid(),
        UserId = userId,
        DatabaseId = databaseId,
        QueryText = queryText,
        Status = status,
        ExecutedAt = executedAt,
        DurationMs = durationMs,
        RowCount = rowCount,
        Error = error,
    };
}
```

- [ ] **Step 2: Update UpdateRequest — replace ServerId/Server with DatabaseId/Database**

In `UpdateRequest.cs`, make these targeted changes (all state-machine methods are unchanged):

1. Replace `public ServerId? ServerId { get; private set; }` with `public DatabaseId? DatabaseId { get; private set; }`
2. Replace `public Server? Server { get; private set; }` with `public Database? Database { get; private set; }`
3. In the `Create` factory method, replace the `ServerId` parameter with `DatabaseId databaseId` and assign `DatabaseId = databaseId`.

The `Create` signature becomes:
```csharp
public static UpdateRequest Create(
    DatabaseId databaseId,
    string sqlText,
    string reason,
    Actioned by)
```

- [ ] **Step 3: Build Core**

```bash
dotnet build src/SluiceBase.Core/SluiceBase.Core.csproj
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 4: Commit**

```bash
git add src/SluiceBase.Core/Queries/QueryLog.cs src/SluiceBase.Core/Updates/UpdateRequest.cs
git commit -m "feat: replace ServerId with DatabaseId on QueryLog and UpdateRequest"
```

---

## Task 8: Update AppDbContext

**Files:**
- Modify: `src/SluiceBase.Api/Data/AppDbContext.cs`

- [ ] **Step 1: Add Credentials and Databases DbSets**

```csharp
// src/SluiceBase.Api/Data/AppDbContext.cs
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using SluiceBase.Api.Data.Converters;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Queries;
using SluiceBase.Core.Servers;
using SluiceBase.Core.Updates;
using SluiceBase.Core.Users;

namespace SluiceBase.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserPermissionMap> UserPermissions => Set<UserPermissionMap>();
    public DbSet<Server> Servers => Set<Server>();
    public DbSet<Credential> Credentials => Set<Credential>();
    public DbSet<Database> Databases => Set<Database>();
    public DbSet<QueryLog> QueryLogs => Set<QueryLog>();
    public DbSet<UpdateRequest> UpdateRequests => Set<UpdateRequest>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        configurationBuilder.Conventions.Remove<TableNameFromDbSetConvention>();
        configurationBuilder.RegisterAllInVogenEfCoreConverters();
        configurationBuilder.Properties<Enum>().HaveConversion<string>();
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add src/SluiceBase.Api/Data/AppDbContext.cs
git commit -m "feat: add Credentials and Databases DbSets to AppDbContext"
```

---

## Task 9: Drop all migrations and recreate from scratch

**Files:**
- Delete: all `*.cs` files in `src/SluiceBase.Api/Data/Migrations/`
- Create: new `InitialSchema` migration (generated)

- [ ] **Step 1: Delete all migration files**

```bash
find src/SluiceBase.Api/Data/Migrations -name "*.cs" -delete
```

Expected: no output, all `.cs` files in Migrations/ are gone.

> **Note:** Come back to Step 2 after Task 16 — the migration requires a clean build of the entire API project.

- [ ] **Step 2: Create the new InitialSchema migration** *(complete Task 16 first)*

```bash
dotnet build src/SluiceBase.Api && \
dotnet ef migrations add InitialSchema \
  --project src/SluiceBase.Api \
  --startup-project src/SluiceBase.Api \
  --output-dir Data/Migrations
```

Expected: `Done. To undo this action, use 'ef migrations remove'`

- [ ] **Step 3: Commit**

```bash
git add src/SluiceBase.Api/Data/Migrations/
git commit -m "feat: drop all migrations and recreate InitialSchema for new entity model"
```

---

## Task 10: Update IServerConnectionFactory and ServerConnectionFactory

**Files:**
- Modify: `src/SluiceBase.Api/Servers/IServerConnectionFactory.cs`
- Modify: `src/SluiceBase.Api/Servers/ServerConnectionFactory.cs`

- [ ] **Step 1: Update the interface**

```csharp
// src/SluiceBase.Api/Servers/IServerConnectionFactory.cs
using SluiceBase.Core.Servers;

namespace SluiceBase.Api.Servers;

internal interface IServerConnectionFactory
{
    Task<string> GetConnectionStringAsync(DatabaseId databaseId, CredentialKind kind, CancellationToken ct);
}
```

- [ ] **Step 2: Rewrite ServerConnectionFactory**

```csharp
// src/SluiceBase.Api/Servers/ServerConnectionFactory.cs
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SluiceBase.Api.Data;
using SluiceBase.Core.Servers;

namespace SluiceBase.Api.Servers;

internal sealed class ServerConnectionFactory(
    AppDbContext db,
    IDataProtectionProvider dataProtection) : IServerConnectionFactory
{
    private readonly IDataProtector _protector =
        dataProtection.CreateProtector(ProtectorPurpose);

    public const string ProtectorPurpose = "SluiceBase.ServerPassword";

    public async Task<string> GetConnectionStringAsync(DatabaseId databaseId, CredentialKind kind, CancellationToken ct)
    {
        var database = await db.Databases
            .AsNoTracking()
            .Include(d => d.Server)
            .SingleOrDefaultAsync(d => d.Id == databaseId, ct)
            ?? throw new InvalidOperationException($"Database {databaseId} not found.");

        if (database.Server!.IsDisabled)
            throw new InvalidOperationException($"Server '{database.Server.Name}' is disabled.");

        if (database.IsDisabled)
            throw new InvalidOperationException($"Database '{database.DisplayName}' is disabled.");

        var credentialId = kind == CredentialKind.Read
            ? database.ReadCredentialId
            : database.WriteCredentialId
                ?? throw new InvalidOperationException(
                    $"Database '{database.DisplayName}' has no write credential configured.");

        var credential = await db.Credentials
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == credentialId, ct)
            ?? throw new InvalidOperationException($"Credential {credentialId} not found.");

        var password = _protector.Unprotect(credential.EncryptedPassword);

        return new NpgsqlConnectionStringBuilder
        {
            Host = database.Server.Host,
            Port = database.Server.Port,
            Database = database.DatabaseName,
            Username = credential.Username,
            Password = password,
        }.ConnectionString;
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add src/SluiceBase.Api/Servers/IServerConnectionFactory.cs \
        src/SluiceBase.Api/Servers/ServerConnectionFactory.cs
git commit -m "feat: update connection factory to use DatabaseId — load Database → Server + Credential, check IsDisabled"
```

---

## Task 11: Refactor ServerEndpoints

**Files:**
- Modify: `src/SluiceBase.Api/Endpoints/ServerEndpoints.cs`

The endpoint is simplified: create/update only touch server-level fields; list returns nested credentials[] and databases[]; delete becomes a soft-delete that cascades to children.

- [ ] **Step 1: Replace ServerEndpoints.cs**

```csharp
// src/SluiceBase.Api/Endpoints/ServerEndpoints.cs
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SluiceBase.Api.Data;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Servers;

namespace SluiceBase.Api.Endpoints;

internal static class ServerEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var servers = app.MapGroup("/api/server")
            .RequireAuthorization(Permissions.ServerManage);

        servers.MapGet("/", ListServers).WithName("ListServers");
        servers.MapPost("/", CreateServer).WithName("CreateServer");
        servers.MapPut("/{id}", UpdateServer).WithName("UpdateServer");
        servers.MapDelete("/{id}", DeleteServer).WithName("DeleteServer");
    }

    // ── list ─────────────────────────────────────────────────────────────────

    private static async Task<Ok<ListServersResponse>> ListServers(
        AppDbContext db, CancellationToken ct)
    {
        var servers = await db.Servers
            .AsNoTracking()
            .Where(s => s.DeletedAt == null)
            .Include(s => s.Credentials.Where(c => c.DeletedAt == null))
            .Include(s => s.Databases.Where(d => d.DeletedAt == null))
            .OrderBy(s => s.Name)
            .ToListAsync(ct);
        return TypedResults.Ok(new ListServersResponse(servers.Select(ToResponse).ToList()));
    }

    // ── create ────────────────────────────────────────────────────────────────

    private static async Task<Results<Created<ServerResponse>, Conflict>> CreateServer(
        CreateServerRequest req,
        AppDbContext db,
        TimeProvider clock,
        CancellationToken ct)
    {
        var server = Server.Create(req.Name, req.Kind, req.Host, req.Port, clock.GetUtcNow());
        db.Servers.Add(server);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("unique") == true ||
                                           ex.InnerException?.Message.Contains("duplicate") == true)
        {
            return TypedResults.Conflict();
        }
        return TypedResults.Created($"/api/server/{server.Id}", ToResponse(server));
    }

    // ── update ────────────────────────────────────────────────────────────────

    private static async Task<Results<Ok<ServerResponse>, NotFound, Conflict>> UpdateServer(
        ServerId id,
        UpdateServerRequest req,
        AppDbContext db,
        TimeProvider clock,
        CancellationToken ct)
    {
        var server = await db.Servers
            .Include(s => s.Credentials.Where(c => c.DeletedAt == null))
            .Include(s => s.Databases.Where(d => d.DeletedAt == null))
            .SingleOrDefaultAsync(s => s.Id == id && s.DeletedAt == null, ct);
        if (server is null)
            return TypedResults.NotFound();

        server.Update(req.Name, req.Host, req.Port, req.Kind, req.IsDisabled, clock.GetUtcNow());
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("unique") == true ||
                                           ex.InnerException?.Message.Contains("duplicate") == true)
        {
            return TypedResults.Conflict();
        }
        return TypedResults.Ok(ToResponse(server));
    }

    // ── soft-delete ───────────────────────────────────────────────────────────

    private static async Task<Results<NoContent, NotFound>> DeleteServer(
        ServerId id,
        AppDbContext db,
        TimeProvider clock,
        CancellationToken ct)
    {
        var server = await db.Servers
            .Include(s => s.Credentials)
            .Include(s => s.Databases)
            .SingleOrDefaultAsync(s => s.Id == id && s.DeletedAt == null, ct);
        if (server is null)
            return TypedResults.NotFound();

        server.SoftDelete(clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static ServerResponse ToResponse(Server s) =>
        new(s.Id, s.Name, s.Kind, s.Host, s.Port, s.IsDisabled,
            s.Credentials.Select(c => new CredentialResponse(c.Id, c.Label, c.Username, c.CreatedAt, c.UpdatedAt)).ToList(),
            s.Databases.Select(d => new DatabaseResponse(d.Id, d.DisplayName, d.DatabaseName, d.ReadCredentialId, d.WriteCredentialId, d.CanWrite, d.IsDisabled, d.CreatedAt, d.UpdatedAt)).ToList(),
            s.CreatedAt, s.UpdatedAt);

    // ── request / response records ────────────────────────────────────────────

    public sealed record ListServersResponse(IReadOnlyList<ServerResponse> Servers);

    public sealed record ServerResponse(
        ServerId Id,
        string Name,
        string Kind,
        string Host,
        int Port,
        bool IsDisabled,
        IReadOnlyList<CredentialResponse> Credentials,
        IReadOnlyList<DatabaseResponse> Databases,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);

    public sealed record CredentialResponse(
        CredentialId Id,
        string Label,
        string Username,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);

    public sealed record DatabaseResponse(
        DatabaseId Id,
        string DisplayName,
        string DatabaseName,
        CredentialId ReadCredentialId,
        CredentialId? WriteCredentialId,
        bool CanWrite,
        bool IsDisabled,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);

    public sealed record CreateServerRequest(string Name, string Kind, string Host, int Port);

    public sealed record UpdateServerRequest(
        string Name,
        string Host,
        int Port,
        string Kind,
        bool IsDisabled = false);
}
```

- [ ] **Step 2: Commit**

```bash
git add src/SluiceBase.Api/Endpoints/ServerEndpoints.cs
git commit -m "feat: refactor ServerEndpoints — soft-delete, simplified create/update, nested credential/database response"
```

---

## Task 12: Add CredentialEndpoints

**Files:**
- Create: `src/SluiceBase.Api/Endpoints/CredentialEndpoints.cs`

- [ ] **Step 1: Create CredentialEndpoints.cs**

```csharp
// src/SluiceBase.Api/Endpoints/CredentialEndpoints.cs
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SluiceBase.Api.Data;
using SluiceBase.Api.Servers;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Servers;

namespace SluiceBase.Api.Endpoints;

internal static class CredentialEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/server/{serverId}/credential")
            .RequireAuthorization(Permissions.ServerManage);

        group.MapPost("/", AddCredential).WithName("AddCredential");
        group.MapPut("/{credentialId}", UpdateCredential).WithName("UpdateCredential");
        group.MapDelete("/{credentialId}", DeleteCredential).WithName("DeleteCredential");
    }

    private static async Task<Results<Created<CredentialResponse>, NotFound>> AddCredential(
        ServerId serverId,
        AddCredentialRequest req,
        AppDbContext db,
        IDataProtectionProvider dataProtection,
        TimeProvider clock,
        CancellationToken ct)
    {
        var serverExists = await db.Servers.AnyAsync(s => s.Id == serverId && s.DeletedAt == null, ct);
        if (!serverExists)
            return TypedResults.NotFound();

        var protector = dataProtection.CreateProtector(ServerConnectionFactory.ProtectorPurpose);
        var cred = Credential.Create(serverId, req.Label, req.Username, protector.Protect(req.Password), clock.GetUtcNow());
        db.Credentials.Add(cred);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/server/{serverId}/credential/{cred.Id}", ToResponse(cred));
    }

    private static async Task<Results<Ok<CredentialResponse>, NotFound>> UpdateCredential(
        ServerId serverId,
        CredentialId credentialId,
        UpdateCredentialRequest req,
        AppDbContext db,
        IDataProtectionProvider dataProtection,
        TimeProvider clock,
        CancellationToken ct)
    {
        var cred = await db.Credentials
            .SingleOrDefaultAsync(c => c.Id == credentialId && c.ServerId == serverId && c.DeletedAt == null, ct);
        if (cred is null)
            return TypedResults.NotFound();

        var protector = dataProtection.CreateProtector(ServerConnectionFactory.ProtectorPurpose);
        var encPass = req.Password is not null ? protector.Protect(req.Password) : null;
        cred.Update(req.Label, req.Username, encPass, clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(ToResponse(cred));
    }

    private static async Task<Results<NoContent, NotFound, Conflict<string>>> DeleteCredential(
        ServerId serverId,
        CredentialId credentialId,
        AppDbContext db,
        TimeProvider clock,
        CancellationToken ct)
    {
        var cred = await db.Credentials
            .SingleOrDefaultAsync(c => c.Id == credentialId && c.ServerId == serverId && c.DeletedAt == null, ct);
        if (cred is null)
            return TypedResults.NotFound();

        var inUse = await db.Databases.AnyAsync(
            d => d.ServerId == serverId && d.DeletedAt == null &&
                 (d.ReadCredentialId == credentialId || d.WriteCredentialId == credentialId), ct);
        if (inUse)
            return TypedResults.Conflict("Credential is still referenced by one or more active databases.");

        cred.SoftDelete(clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static CredentialResponse ToResponse(Credential c) =>
        new(c.Id, c.Label, c.Username, c.CreatedAt, c.UpdatedAt);

    public sealed record AddCredentialRequest(string Label, string Username, string Password);
    public sealed record UpdateCredentialRequest(string Label, string Username, string? Password = null);
    public sealed record CredentialResponse(CredentialId Id, string Label, string Username, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
}
```

- [ ] **Step 2: Commit**

```bash
git add src/SluiceBase.Api/Endpoints/CredentialEndpoints.cs
git commit -m "feat: add CredentialEndpoints — add/update/soft-delete with 409 guard when referenced by active database"
```

---

## Task 13: Add DatabaseEndpoints

**Files:**
- Create: `src/SluiceBase.Api/Endpoints/DatabaseEndpoints.cs`

- [ ] **Step 1: Create DatabaseEndpoints.cs**

```csharp
// src/SluiceBase.Api/Endpoints/DatabaseEndpoints.cs
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SluiceBase.Api.Data;
using SluiceBase.Api.Servers;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Servers;
using SluiceBase.Core.Targets;
using DbEntity = SluiceBase.Core.Servers.Database;

namespace SluiceBase.Api.Endpoints;

internal static class DatabaseEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/server/{serverId}/database")
            .RequireAuthorization(Permissions.ServerManage);

        group.MapPost("/", AddDatabase).WithName("AddDatabase");
        group.MapPut("/{databaseId}", UpdateDatabase).WithName("UpdateDatabase");
        group.MapDelete("/{databaseId}", DeleteDatabase).WithName("DeleteDatabase");
        group.MapPost("/{databaseId}/test", TestDatabaseConnection).WithName("TestDatabaseConnection");
    }

    private static async Task<Results<Created<DatabaseResponse>, NotFound>> AddDatabase(
        ServerId serverId,
        AddDatabaseRequest req,
        AppDbContext db,
        TimeProvider clock,
        CancellationToken ct)
    {
        var serverExists = await db.Servers.AnyAsync(s => s.Id == serverId && s.DeletedAt == null, ct);
        if (!serverExists)
            return TypedResults.NotFound();

        var dbRecord = DbEntity.Create(serverId, req.DisplayName, req.DatabaseName,
            req.ReadCredentialId, req.WriteCredentialId, clock.GetUtcNow());
        db.Databases.Add(dbRecord);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/server/{serverId}/database/{dbRecord.Id}", ToResponse(dbRecord));
    }

    private static async Task<Results<Ok<DatabaseResponse>, NotFound>> UpdateDatabase(
        ServerId serverId,
        DatabaseId databaseId,
        UpdateDatabaseRequest req,
        AppDbContext db,
        TimeProvider clock,
        CancellationToken ct)
    {
        var dbRecord = await db.Databases
            .SingleOrDefaultAsync(d => d.Id == databaseId && d.ServerId == serverId && d.DeletedAt == null, ct);
        if (dbRecord is null)
            return TypedResults.NotFound();

        dbRecord.Update(req.DisplayName, req.DatabaseName, req.ReadCredentialId, req.WriteCredentialId, req.IsDisabled, clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(ToResponse(dbRecord));
    }

    private static async Task<Results<NoContent, NotFound>> DeleteDatabase(
        ServerId serverId,
        DatabaseId databaseId,
        AppDbContext db,
        TimeProvider clock,
        CancellationToken ct)
    {
        var dbRecord = await db.Databases
            .SingleOrDefaultAsync(d => d.Id == databaseId && d.ServerId == serverId && d.DeletedAt == null, ct);
        if (dbRecord is null)
            return TypedResults.NotFound();

        dbRecord.SoftDelete(clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<TestConnectionResponse>, NotFound>> TestDatabaseConnection(
        ServerId serverId,
        DatabaseId databaseId,
        AppDbContext db,
        IServerConnectionFactory factory,
        ITargetEngine targetEngine,
        CancellationToken ct)
    {
        var dbRecord = await db.Databases.AsNoTracking()
            .SingleOrDefaultAsync(d => d.Id == databaseId && d.ServerId == serverId && d.DeletedAt == null, ct);
        if (dbRecord is null)
            return TypedResults.NotFound();

        var readCs = await factory.GetConnectionStringAsync(databaseId, CredentialKind.Read, ct);
        var readResult = await targetEngine.TestConnectionAsync(readCs, ct);

        ConnectivityResult? writeResult = null;
        if (dbRecord.CanWrite)
        {
            var writeCs = await factory.GetConnectionStringAsync(databaseId, CredentialKind.Write, ct);
            writeResult = await targetEngine.TestConnectionAsync(writeCs, ct);
        }

        return TypedResults.Ok(new TestConnectionResponse(readResult, writeResult));
    }

    private static DatabaseResponse ToResponse(DbEntity d) =>
        new(d.Id, d.DisplayName, d.DatabaseName, d.ReadCredentialId, d.WriteCredentialId, d.CanWrite, d.IsDisabled, d.CreatedAt, d.UpdatedAt);

    public sealed record AddDatabaseRequest(string DisplayName, string DatabaseName, CredentialId ReadCredentialId, CredentialId? WriteCredentialId = null);
    public sealed record UpdateDatabaseRequest(string DisplayName, string DatabaseName, CredentialId ReadCredentialId, CredentialId? WriteCredentialId, bool IsDisabled);
    public sealed record DatabaseResponse(DatabaseId Id, string DisplayName, string DatabaseName, CredentialId ReadCredentialId, CredentialId? WriteCredentialId, bool CanWrite, bool IsDisabled, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
    public sealed record TestConnectionResponse(ConnectivityResult Read, ConnectivityResult? Write);
}
```

- [ ] **Step 2: Commit**

```bash
git add src/SluiceBase.Api/Endpoints/DatabaseEndpoints.cs
git commit -m "feat: add DatabaseEndpoints — add/update/soft-delete + test-connection (moved from server level)"
```

---

## Task 14: Register new endpoints and update QueryEndpoints, SchemaEndpoints, UpdateEndpoints

**Files:**
- Modify: `src/SluiceBase.Api/Endpoints/EndpointMapper.cs`
- Modify: `src/SluiceBase.Api/Endpoints/QueryEndpoints.cs`
- Modify: `src/SluiceBase.Api/Endpoints/SchemaEndpoint.cs`
- Modify: `src/SluiceBase.Api/Endpoints/UpdateEndpoints.cs`

- [ ] **Step 1: Register CredentialEndpoints and DatabaseEndpoints in EndpointMapper**

```csharp
// src/SluiceBase.Api/Endpoints/EndpointMapper.cs
namespace SluiceBase.Api.Endpoints;

internal static class EndpointMapper
{
    public static IEndpointRouteBuilder MapAllEndpoints(this WebApplication app)
    {
        AuthEndpoints.Map(app);
        HealthEndpoints.Map(app);
        PermissionEndpoints.Map(app);
        ServerEndpoints.Map(app);
        CredentialEndpoints.Map(app);
        DatabaseEndpoints.Map(app);
        SchemaEndpoints.Map(app);
        QueryEndpoints.Map(app);
        UpdateEndpoints.Map(app);

        if (app.Environment.IsDevelopment())
        {
            DevelopmentEndpoints.Map(app);
        }

        return app;
    }
}
```

- [ ] **Step 2: Replace QueryEndpoints.cs**

The only changes from the original: `ServerId` → `DatabaseId`; load `Database` instead of `Server`; check `IsDisabled` by catching `InvalidOperationException` from factory; log with `databaseId`.

```csharp
// src/SluiceBase.Api/Endpoints/QueryEndpoints.cs
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SluiceBase.Api.Auth;
using SluiceBase.Api.Data;
using SluiceBase.Api.Servers;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Queries;
using SluiceBase.Core.Servers;
using SluiceBase.Core.Targets;

namespace SluiceBase.Api.Endpoints;

internal static class QueryEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/query", ExecuteQuery)
            .RequireAuthorization(Permissions.QueryExecute)
            .WithName("ExecuteQuery");
    }

    private static async Task<Results<Ok<QueryResponse>, NotFound, BadRequest<string>>> ExecuteQuery(
        QueryRequest request,
        AppDbContext db,
        IServerConnectionFactory connectionFactory,
        ITargetEngine targetEngine,
        ICurrentUserAccessor currentUser,
        TimeProvider timeProvider,
        IConfiguration configuration,
        CancellationToken ct)
    {
        var user = await currentUser.GetAsync(ct);
        var startedAt = timeProvider.GetUtcNow();

        var database = await db.Databases.AsNoTracking()
            .SingleOrDefaultAsync(d => d.Id == request.DatabaseId, ct);
        if (database is null)
            return TypedResults.NotFound();

        var timeoutSeconds = configuration.GetValue("Query:TimeoutSeconds", 30);
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        QueryResponse response;
        QueryLogStatus logStatus;
        int? rowCount = null;

        try
        {
            var connectionString = await connectionFactory
                .GetConnectionStringAsync(database.Id, CredentialKind.Read, ct);

            var data = await targetEngine.ExecuteQueryAsync(connectionString, request.Sql, linkedCts.Token);
            var durationMs = (int)(timeProvider.GetUtcNow() - startedAt).TotalMilliseconds;
            rowCount = data.Rows.Length;
            logStatus = QueryLogStatus.Success;
            response = new QueryResponse(data.Columns, data.Rows, rowCount.Value, durationMs, null);
        }
        catch (InvalidOperationException ex)
        {
            var durationMs = (int)(timeProvider.GetUtcNow() - startedAt).TotalMilliseconds;
            logStatus = QueryLogStatus.Error;
            response = new QueryResponse(null, null, 0, durationMs, ex.Message);

            var log = QueryLog.Create(user?.Id, database.Id, request.Sql, logStatus, startedAt, durationMs, null, ex.Message);
            db.QueryLogs.Add(log);
            await db.SaveChangesAsync(ct);
            return TypedResults.BadRequest(ex.Message);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            var durationMs = (int)(timeProvider.GetUtcNow() - startedAt).TotalMilliseconds;
            logStatus = QueryLogStatus.Timeout;
            response = new QueryResponse(null, null, 0, durationMs, $"Query timed out after {timeoutSeconds}s.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var durationMs = (int)(timeProvider.GetUtcNow() - startedAt).TotalMilliseconds;
            logStatus = QueryLogStatus.Error;
            response = new QueryResponse(null, null, 0, durationMs, ex.Message);
        }

        var queryLog = QueryLog.Create(user?.Id, database.Id, request.Sql, logStatus, startedAt, response.DurationMs, rowCount, response.Error);
        db.QueryLogs.Add(queryLog);
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(response);
    }

    public sealed record QueryRequest(DatabaseId DatabaseId, string Sql);

    public sealed record QueryResponse(
        string[]? Columns,
        string?[][]? Rows,
        int RowCount,
        int DurationMs,
        string? Error);
}
```

- [ ] **Step 3: Replace SchemaEndpoint.cs**

```csharp
// src/SluiceBase.Api/Endpoints/SchemaEndpoint.cs
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SluiceBase.Api.Data;
using SluiceBase.Api.Servers;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Schemas;
using SluiceBase.Core.Servers;
using SluiceBase.Core.Targets;

namespace SluiceBase.Api.Endpoints;

internal static class SchemaEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/schema/{databaseId}", GetSchema)
            .RequireAuthorization(Permissions.QueryExecute)
            .WithName("GetSchema");
    }

    private static async Task<Results<Ok<SchemaTree>, NotFound, BadRequest<string>>> GetSchema(
        DatabaseId databaseId,
        AppDbContext db,
        IServerConnectionFactory connectionFactory,
        ITargetEngine targetEngine,
        CancellationToken ct)
    {
        var database = await db.Databases.AsNoTracking()
            .SingleOrDefaultAsync(d => d.Id == databaseId, ct);
        if (database is null)
            return TypedResults.NotFound();

        try
        {
            var connectionString = await connectionFactory.GetConnectionStringAsync(databaseId, CredentialKind.Read, ct);
            var tree = await targetEngine.GetSchemaAsync(connectionString, ct);
            return TypedResults.Ok(tree);
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.BadRequest(ex.Message);
        }
    }
}
```

- [ ] **Step 4: Update UpdateEndpoints.cs**

The changes from the original:
1. `SubmitUpdateRequest.ServerId` → `DatabaseId`
2. `Submit`: load `Database` (not `Server`), check `CanWrite` (not `HasWriteCredential`), call `UpdateRequest.Create(database.Id, ...)`
3. `Execute`: check `request.DatabaseId`, load database, check `CanWrite`; call factory with `request.DatabaseId.Value`
4. `LoadDetail`: `.Include(r => r.Server)` → `.Include(r => r.Database)`
5. `List`: `.Include(r => r.Server)` → `.Include(r => r.Database)`, `r.Server?.Name` → `r.Database?.DisplayName`
6. `ToDetail`: `r.ServerId` → `r.DatabaseId`, `r.Server?.Name` → `r.Database?.DisplayName`
7. Response records: `ServerId?` → `DatabaseId?`, `ServerName` → `DatabaseDisplayName`

Make these targeted changes to `UpdateEndpoints.cs`. The state-machine endpoints (Approve, Reject, Cancel) and all their helpers are identical to the original except the `LoadDetail` method:

```csharp
// Updated LoadDetail — change Server nav to Database nav
private static Task<UpdateRequest?> LoadDetail(AppDbContext db, UpdateRequestId id, CancellationToken ct) =>
    db.UpdateRequests
        .AsNoTracking()
        .Include(r => r.Database)
        .Include(r => r.Submitter)
        .Include(r => r.Reviewer)
        .Include(r => r.Executor)
        .Include(r => r.CancelledBy)
        .SingleOrDefaultAsync(r => r.Id == id, ct);

// Updated List helper — change Server to Database
private static async Task<Ok<ListUpdateRequestsResponse>> List(AppDbContext db, CancellationToken ct)
{
    var requests = await db.UpdateRequests
        .Include(r => r.Database)
        .Include(r => r.Submitter)
        .AsNoTracking()
        .OrderByDescending(r => r.SubmittedAt)
        .ToListAsync(ct);

    var items = requests
        .Select(r => new UpdateSummaryItem(
            r.Id,
            r.Database?.DisplayName,
            r.Submitter?.Name ?? r.Submitter?.Email,
            r.Reason,
            r.Status,
            r.SubmittedAt,
            r.ExecSuccess))
        .ToList();

    return TypedResults.Ok(new ListUpdateRequestsResponse(items));
}

// Updated Submit — load Database, check CanWrite
private static async Task<...> Submit(SubmitUpdateRequest req, ...) {
    // ...
    var database = await db.Databases.AsNoTracking()
        .SingleOrDefaultAsync(d => d.Id == req.DatabaseId, ct);
    if (database is null)
        return TypedResults.NotFound();

    if (!database.CanWrite)
        return TypedResults.BadRequest("Database has no write credentials configured.");

    var request = UpdateRequest.Create(database.Id, req.SqlText, req.Reason, new Actioned(user.Id, timeProvider.GetUtcNow()));
    // ...
}

// Updated Execute — check DatabaseId, load database, call factory with databaseId
private static async Task<...> Execute(...) {
    // ...
    if (request.DatabaseId is null)
        return TypedResults.Conflict("Database was deleted. Cannot execute.");

    var database = await db.Databases.AsNoTracking()
        .SingleOrDefaultAsync(d => d.Id == request.DatabaseId, ct);
    if (database is null || !database.CanWrite)
        return TypedResults.Conflict("Database not found or has no write credentials configured.");

    // connection factory call:
    var connectionString = await connectionFactory.GetConnectionStringAsync(request.DatabaseId.Value, CredentialKind.Write, ct);
    // ...
}

// Updated ToDetail
private static UpdateRequestDetailResponse ToDetail(UpdateRequest r) =>
    new(r.Id,
        r.DatabaseId,
        r.Database?.DisplayName,
        r.SubmitterId,
        r.Submitter?.Name ?? r.Submitter?.Email,
        r.SqlText,
        r.Reason,
        r.Status,
        r.ReviewerId,
        r.Reviewer?.Name ?? r.Reviewer?.Email,
        r.ReviewNote,
        r.CancelledById,
        r.CancelledBy?.Name ?? r.CancelledBy?.Email,
        r.CancelNote,
        r.ExecutorId,
        r.Executor?.Name ?? r.Executor?.Email,
        r.SubmittedAt,
        r.ReviewedAt,
        r.ExecutedAt,
        r.CancelledAt,
        r.ExecSuccess,
        r.ExecDurationMs,
        r.ExecAffectedRows,
        r.ExecError);

// Updated request/response records
public sealed record SubmitUpdateRequest(DatabaseId DatabaseId, string SqlText, string Reason);

public sealed record UpdateSummaryItem(
    UpdateRequestId Id,
    string? DatabaseDisplayName,
    string? SubmitterName,
    string Reason,
    UpdateRequestStatus Status,
    DateTimeOffset SubmittedAt,
    bool? ExecSuccess);

public sealed record UpdateRequestDetailResponse(
    UpdateRequestId Id,
    DatabaseId? DatabaseId,
    string? DatabaseDisplayName,
    UserId? SubmitterId,
    string? SubmitterName,
    string SqlText,
    string Reason,
    UpdateRequestStatus Status,
    UserId? ReviewerId,
    string? ReviewerName,
    string? ReviewNote,
    UserId? CancelledById,
    string? CancelledByName,
    string? CancelNote,
    UserId? ExecutorId,
    string? ExecutorName,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? ReviewedAt,
    DateTimeOffset? ExecutedAt,
    DateTimeOffset? CancelledAt,
    bool? ExecSuccess,
    int? ExecDurationMs,
    int? ExecAffectedRows,
    string? ExecError);
```

- [ ] **Step 5: Build the full solution**

```bash
dotnet build src/SluiceBase.Api
```

Expected: `Build succeeded. 0 Error(s)`

If the build succeeds, return to **Task 9 Step 2** and create the migration now.

- [ ] **Step 6: Commit**

```bash
git add src/SluiceBase.Api/Endpoints/EndpointMapper.cs \
        src/SluiceBase.Api/Endpoints/QueryEndpoints.cs \
        src/SluiceBase.Api/Endpoints/SchemaEndpoint.cs \
        src/SluiceBase.Api/Endpoints/UpdateEndpoints.cs
git commit -m "feat: update Query, Schema, Update endpoints — serverId → databaseId throughout"
```

---

## Task 15: Update DevServerSeed

**Files:**
- Modify: `src/AppHost/Extensions/DevServerSeed.cs`

- [ ] **Step 1: Replace DevServerSeed.cs**

The seed now creates a `Server`, then `Credential` rows, then a `server_database` row — one `SeedServerAsync` call per seeded server. Use two separate raw SQL commands per resource (insert, then select-by-name) to handle the idempotency case.

```csharp
// src/AppHost/Extensions/DevServerSeed.cs
using System.Net.Http.Json;
using Npgsql;

namespace AppHost.Extensions;

internal static class DevServerSeed
{
    public static async Task<ExecuteCommandResult> SeedAsync(
        ExecuteCommandContext context,
        IResourceBuilder<ProjectResource> api,
        IResourceBuilder<PostgresDatabaseResource> metadataDb,
        IResourceBuilder<PostgresDatabaseResource> blueDb,
        IResourceBuilder<PostgresDatabaseResource> greenDb)
    {
        var ct = context.CancellationToken;
        try
        {
            var apiUrl = await api.Resource.GetEndpoint("https").GetValueAsync(ct)
                         ?? throw new InvalidOperationException("API endpoint not resolved.");

            var metaConnStr = await metadataDb.Resource.ConnectionStringExpression.GetValueAsync(ct)
                              ?? throw new InvalidOperationException("Metadata connection string not resolved.");
            var blueConnStr = await blueDb.Resource.ConnectionStringExpression.GetValueAsync(ct)
                              ?? throw new InvalidOperationException("blue-appdb connection string not resolved.");
            var greenConnStr = await greenDb.Resource.ConnectionStringExpression.GetValueAsync(ct)
                               ?? throw new InvalidOperationException("green-appdb connection string not resolved.");

            var bluePg = new NpgsqlConnectionStringBuilder(blueConnStr);
            var greenPg = new NpgsqlConnectionStringBuilder(greenConnStr);

            var blueReadEnc = await EncryptAsync(apiUrl, "reader_blue", ct);
            var blueWriteEnc = await EncryptAsync(apiUrl, "writer_blue", ct);
            var greenReadEnc = await EncryptAsync(apiUrl, "reader_green", ct);

            await using var conn = new NpgsqlConnection(metaConnStr);
            await conn.OpenAsync(ct);

            await SeedServerAsync(conn,
                serverName: "Blue",
                kind: "postgres",
                host: bluePg.Host!,
                port: bluePg.Port,
                readLabel: "Read-only role",
                readUser: "reader_blue",
                encReadPass: blueReadEnc,
                writeLabel: "Write role",
                writeUser: "writer_blue",
                encWritePass: blueWriteEnc,
                dbDisplayName: "Blue App DB",
                dbName: "appdb",
                ct);

            await SeedServerAsync(conn,
                serverName: "Green",
                kind: "postgres",
                host: greenPg.Host!,
                port: greenPg.Port,
                readLabel: "Read-only role",
                readUser: "reader_green",
                encReadPass: greenReadEnc,
                writeLabel: null,
                writeUser: null,
                encWritePass: null,
                dbDisplayName: "Green App DB",
                dbName: "appdb",
                ct);

            return CommandResults.Success();
        }
        catch (Exception ex)
        {
            return CommandResults.Failure(ex.Message);
        }
    }

    private static async Task SeedServerAsync(
        NpgsqlConnection conn,
        string serverName,
        string kind,
        string host,
        int port,
        string readLabel,
        string readUser,
        string encReadPass,
        string? writeLabel,
        string? writeUser,
        string? encWritePass,
        string dbDisplayName,
        string dbName,
        CancellationToken ct)
    {
        // Insert server (no-op if already seeded)
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO server (id, name, kind, host, port, is_disabled, created_at, updated_at)
                VALUES (gen_random_uuid(), @name, @kind, @host, @port, false, now(), now())
                ON CONFLICT (name) WHERE deleted_at IS NULL DO NOTHING;
                """;
            cmd.Parameters.AddWithValue("name", serverName);
            cmd.Parameters.AddWithValue("kind", kind);
            cmd.Parameters.AddWithValue("host", host);
            cmd.Parameters.AddWithValue("port", port);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        Guid serverId;
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT id FROM server WHERE name = @name AND deleted_at IS NULL;";
            cmd.Parameters.AddWithValue("name", serverName);
            serverId = (Guid)(await cmd.ExecuteScalarAsync(ct))!;
        }

        // Insert read credential
        Guid readCredId = await UpsertCredentialAsync(conn, serverId, readLabel, readUser, encReadPass, ct);

        // Insert write credential (optional)
        Guid? writeCredId = null;
        if (writeLabel is not null && writeUser is not null && encWritePass is not null)
            writeCredId = await UpsertCredentialAsync(conn, serverId, writeLabel, writeUser, encWritePass, ct);

        // Insert database
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO server_database (
                    id, server_id, display_name, database_name,
                    read_credential_id, write_credential_id,
                    is_disabled, created_at, updated_at)
                VALUES (
                    gen_random_uuid(), @serverId, @displayName, @dbName,
                    @readCredId, @writeCredId,
                    false, now(), now())
                ON CONFLICT DO NOTHING;
                """;
            cmd.Parameters.AddWithValue("serverId", serverId);
            cmd.Parameters.AddWithValue("displayName", dbDisplayName);
            cmd.Parameters.AddWithValue("dbName", dbName);
            cmd.Parameters.AddWithValue("readCredId", readCredId);
            cmd.Parameters.AddWithValue("writeCredId", (object?)writeCredId ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    private static async Task<Guid> UpsertCredentialAsync(
        NpgsqlConnection conn,
        Guid serverId,
        string label,
        string username,
        string encryptedPassword,
        CancellationToken ct)
    {
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO server_credential (id, server_id, label, username, encrypted_password, created_at, updated_at)
                VALUES (gen_random_uuid(), @serverId, @label, @username, @encPass, now(), now())
                ON CONFLICT DO NOTHING;
                """;
            cmd.Parameters.AddWithValue("serverId", serverId);
            cmd.Parameters.AddWithValue("label", label);
            cmd.Parameters.AddWithValue("username", username);
            cmd.Parameters.AddWithValue("encPass", encryptedPassword);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await using var selectCmd = conn.CreateCommand();
        selectCmd.CommandText = "SELECT id FROM server_credential WHERE server_id = @serverId AND username = @username AND deleted_at IS NULL LIMIT 1;";
        selectCmd.Parameters.AddWithValue("serverId", serverId);
        selectCmd.Parameters.AddWithValue("username", username);
        return (Guid)(await selectCmd.ExecuteScalarAsync(ct))!;
    }

    private static async Task<string> EncryptAsync(string apiBaseUrl, string plaintext, CancellationToken ct)
    {
        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        };
        using var http = new HttpClient(handler) { BaseAddress = new Uri(apiBaseUrl) };
        var resp = await http.PostAsJsonAsync("/api/internal/dev/encrypt", new { plaintext }, ct);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<EncryptResponse>(ct);
        return body!.Ciphertext;
    }

    private sealed record EncryptResponse(string Ciphertext);
}
```

- [ ] **Step 2: Commit**

```bash
git add src/AppHost/Extensions/DevServerSeed.cs
git commit -m "feat: update DevServerSeed for three-table schema — server → credentials → database"
```

---

## Task 16: Rewrite ServerEndpointTests

**Files:**
- Modify: `tests/IntegrationTests/ServerEndpointTests.cs`

The central change is the `CreateServerAsync` helper, which now creates server → credential → database and returns a `DatabaseId`. Existing tests are updated to use the new response shape. New test cases cover soft-delete cascade and disabled state.

- [ ] **Step 1: Write the tests**

```csharp
// tests/IntegrationTests/ServerEndpointTests.cs
using System.Net;
using System.Net.Http.Json;
using Aspire.Hosting.Testing;
using IntegrationTests.Supports;
using Npgsql;
using SluiceBase.Api.Endpoints;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Servers;

namespace IntegrationTests;

public class ServerEndpointTests(SluiceBaseStackFactory factory)
{
    private KeycloakLoginHelper LoginHelper => new(factory.InitialisedApp);

    private static string UniqueName() => $"srv-{Guid.NewGuid():N}"[..24];

    private static HttpRequestMessage MutationRequest(
        HttpMethod method, string url, string xsrf, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Add("X-XSRF-TOKEN", xsrf);
        if (body is not null)
            req.Content = JsonContent.Create(body);
        return req;
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private async Task<(AuthenticatedSession session, string xsrf)> AuthorizedSessionAsync(CancellationToken ct)
    {
        var session = await LoginHelper.SignInAsync("alice", "dev", ct);
        var xsrf = await session.FetchXsrfTokenAsync(ct);
        var users = await session.Client.GetFromJsonAsync<ListUserBody>("/api/admin/user", ct);
        var alice = Assert.Single(users!.Users, u => u.Email == "alice@example.com");
        using var grantReq = MutationRequest(HttpMethod.Post, $"/api/admin/user/{alice.Id}/permission", xsrf,
            new { permission = Permissions.ServerManage });
        (await session.Client.SendAsync(grantReq, ct)).EnsureSuccessStatusCode();
        return (session, xsrf);
    }

    // Creates a server with one read credential + one write credential + one database.
    // Returns the DatabaseId needed for query/schema/update tests.
    private static async Task<(ServerEndpoints.ServerResponse server, CredentialEndpoints.CredentialResponse readCred, CredentialEndpoints.CredentialResponse writeCred, DatabaseEndpoints.DatabaseResponse database)>
        CreateServerWithDatabaseAsync(AuthenticatedSession session, string xsrf, string host, int port, CancellationToken ct, string? name = null)
    {
        var serverName = name ?? UniqueName();

        // Create server
        using var sReq = MutationRequest(HttpMethod.Post, "/api/server", xsrf,
            new ServerEndpoints.CreateServerRequest(serverName, "postgres", host, port));
        var sResp = await session.Client.SendAsync(sReq, ct);
        sResp.EnsureSuccessStatusCode();
        var server = (await sResp.Content.ReadFromJsonAsync<ServerEndpoints.ServerResponse>(ct))!;

        // Create read credential
        using var rcReq = MutationRequest(HttpMethod.Post, $"/api/server/{server.Id}/credential", xsrf,
            new CredentialEndpoints.AddCredentialRequest("Read-only role", "reader_blue", "reader_blue"));
        var rcResp = await session.Client.SendAsync(rcReq, ct);
        rcResp.EnsureSuccessStatusCode();
        var readCred = (await rcResp.Content.ReadFromJsonAsync<CredentialEndpoints.CredentialResponse>(ct))!;

        // Create write credential
        using var wcReq = MutationRequest(HttpMethod.Post, $"/api/server/{server.Id}/credential", xsrf,
            new CredentialEndpoints.AddCredentialRequest("Write role", "writer_blue", "writer_blue"));
        var wcResp = await session.Client.SendAsync(wcReq, ct);
        wcResp.EnsureSuccessStatusCode();
        var writeCred = (await wcResp.Content.ReadFromJsonAsync<CredentialEndpoints.CredentialResponse>(ct))!;

        // Create database
        using var dbReq = MutationRequest(HttpMethod.Post, $"/api/server/{server.Id}/database", xsrf,
            new DatabaseEndpoints.AddDatabaseRequest("App DB", "appdb", readCred.Id, writeCred.Id));
        var dbResp = await session.Client.SendAsync(dbReq, ct);
        dbResp.EnsureSuccessStatusCode();
        var database = (await dbResp.Content.ReadFromJsonAsync<DatabaseEndpoints.DatabaseResponse>(ct))!;

        return (server, readCred, writeCred, database);
    }

    // ── anonymous / unauthorized ───────────────────────────────────────────────

    [Fact]
    public async Task ListServers_Anonymous_Returns401()
    {
        using var client = factory.InitialisedApp.CreateHttpClient("api", "https");
        var resp = await client.GetAsync("/api/server", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task ListServers_Bob_Returns403()
    {
        using var session = await LoginHelper.SignInAsync("bob", "dev", TestContext.Current.CancellationToken);
        var resp = await session.Client.GetAsync("/api/server", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    // ── create ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateServer_HappyPath_ReturnsServerWithEmptyCredentialsAndDatabases()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf) = await AuthorizedSessionAsync(ct);
        using var _ = session;

        using var req = MutationRequest(HttpMethod.Post, "/api/server", xsrf,
            new ServerEndpoints.CreateServerRequest(UniqueName(), "postgres", "localhost", 5432));
        var resp = await session.Client.SendAsync(req, ct);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<ServerEndpoints.ServerResponse>(ct);
        Assert.NotNull(body);
        Assert.Empty(body.Credentials);
        Assert.Empty(body.Databases);
        Assert.False(body.IsDisabled);
    }

    [Fact]
    public async Task CreateServer_DuplicateName_Returns409()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf) = await AuthorizedSessionAsync(ct);
        using var _ = session;

        var name = UniqueName();
        var body = new ServerEndpoints.CreateServerRequest(name, "postgres", "localhost", 5432);
        using var req1 = MutationRequest(HttpMethod.Post, "/api/server", xsrf, body);
        (await session.Client.SendAsync(req1, ct)).EnsureSuccessStatusCode();

        using var req2 = MutationRequest(HttpMethod.Post, "/api/server", xsrf, body);
        Assert.Equal(HttpStatusCode.Conflict, (await session.Client.SendAsync(req2, ct)).StatusCode);
    }

    // ── update ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateServer_ChangesNameAndIsDisabled()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf) = await AuthorizedSessionAsync(ct);
        using var _ = session;

        using var cReq = MutationRequest(HttpMethod.Post, "/api/server", xsrf,
            new ServerEndpoints.CreateServerRequest(UniqueName(), "postgres", "localhost", 5432));
        var created = (await (await session.Client.SendAsync(cReq, ct)).Content
            .ReadFromJsonAsync<ServerEndpoints.ServerResponse>(ct))!;

        using var uReq = MutationRequest(HttpMethod.Put, $"/api/server/{created.Id}", xsrf,
            new ServerEndpoints.UpdateServerRequest(created.Name + "-renamed", created.Host, created.Port, created.Kind, true));
        var resp = await session.Client.SendAsync(uReq, ct);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<ServerEndpoints.ServerResponse>(ct);
        Assert.True(body!.IsDisabled);
        Assert.EndsWith("-renamed", body.Name);
    }

    // ── soft-delete ───────────────────────────────────────────────────────────

    [Fact]
    public async Task SoftDeleteServer_RemovesFromList()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf) = await AuthorizedSessionAsync(ct);
        using var _ = session;

        using var cReq = MutationRequest(HttpMethod.Post, "/api/server", xsrf,
            new ServerEndpoints.CreateServerRequest(UniqueName(), "postgres", "localhost", 5432));
        var created = (await (await session.Client.SendAsync(cReq, ct)).Content
            .ReadFromJsonAsync<ServerEndpoints.ServerResponse>(ct))!;

        using var dReq = MutationRequest(HttpMethod.Delete, $"/api/server/{created.Id}", xsrf);
        Assert.Equal(HttpStatusCode.NoContent, (await session.Client.SendAsync(dReq, ct)).StatusCode);

        var list = await session.Client.GetFromJsonAsync<ServerEndpoints.ListServersResponse>("/api/server", ct);
        Assert.DoesNotContain(list!.Servers, s => s.Id == created.Id);
    }

    [Fact]
    public async Task SoftDeleteServer_CascadesToCredentialsAndDatabases()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf) = await AuthorizedSessionAsync(ct);
        using var _ = session;

        var connStr = await factory.InitialisedApp.GetConnectionStringAsync("blue-appdb", ct)
                      ?? throw new InvalidOperationException("blue-appdb not found");
        var pg = new NpgsqlConnectionStringBuilder(connStr);
        var (server, _, _, _) = await CreateServerWithDatabaseAsync(session, xsrf, pg.Host!, pg.Port, ct);

        // Soft-delete the server
        using var dReq = MutationRequest(HttpMethod.Delete, $"/api/server/{server.Id}", xsrf);
        Assert.Equal(HttpStatusCode.NoContent, (await session.Client.SendAsync(dReq, ct)).StatusCode);

        // Server no longer in list
        var list = await session.Client.GetFromJsonAsync<ServerEndpoints.ListServersResponse>("/api/server", ct);
        Assert.DoesNotContain(list!.Servers, s => s.Id == server.Id);

        // Credentials and databases also gone (no longer returned in any server's nested list)
        Assert.Empty(list.Servers.SelectMany(s => s.Credentials).Where(c => c.Id.Value == server.Id.Value));
    }

    // ── response types ────────────────────────────────────────────────────────

    private sealed record ListUserBody(UserRow[] Users);
    private sealed record UserRow(string Id, string Email);
}
```

- [ ] **Step 2: Run the tests**

```bash
dotnet test tests/IntegrationTests/IntegrationTests.csproj \
  --filter "FullyQualifiedName~ServerEndpointTests" -v n
```

Expected: All `ServerEndpointTests` pass.

- [ ] **Step 3: Commit**

```bash
git add tests/IntegrationTests/ServerEndpointTests.cs
git commit -m "test: rewrite ServerEndpointTests for soft-delete and nested credential/database shape"
```

---

## Task 17: Add CredentialEndpointTests

**Files:**
- Create: `tests/IntegrationTests/CredentialEndpointTests.cs`

- [ ] **Step 1: Write the tests**

```csharp
// tests/IntegrationTests/CredentialEndpointTests.cs
using System.Net;
using System.Net.Http.Json;
using IntegrationTests.Supports;
using SluiceBase.Api.Endpoints;
using SluiceBase.Core.Permissions;

namespace IntegrationTests;

public class CredentialEndpointTests(SluiceBaseStackFactory factory)
{
    private KeycloakLoginHelper LoginHelper => new(factory.InitialisedApp);
    private static string UniqueName() => $"srv-{Guid.NewGuid():N}"[..24];

    private static HttpRequestMessage MutationRequest(HttpMethod method, string url, string xsrf, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Add("X-XSRF-TOKEN", xsrf);
        if (body is not null)
            req.Content = JsonContent.Create(body);
        return req;
    }

    private async Task<(AuthenticatedSession session, string xsrf)> AuthorizedSessionAsync(CancellationToken ct)
    {
        var session = await LoginHelper.SignInAsync("alice", "dev", ct);
        var xsrf = await session.FetchXsrfTokenAsync(ct);
        var users = await session.Client.GetFromJsonAsync<ListUserBody>("/api/admin/user", ct);
        var alice = Assert.Single(users!.Users, u => u.Email == "alice@example.com");
        using var grantReq = MutationRequest(HttpMethod.Post, $"/api/admin/user/{alice.Id}/permission", xsrf,
            new { permission = Permissions.ServerManage });
        (await session.Client.SendAsync(grantReq, ct)).EnsureSuccessStatusCode();
        return (session, xsrf);
    }

    private static async Task<ServerEndpoints.ServerResponse> CreateServerAsync(
        AuthenticatedSession session, string xsrf, CancellationToken ct)
    {
        using var req = MutationRequest(HttpMethod.Post, "/api/server", xsrf,
            new ServerEndpoints.CreateServerRequest(UniqueName(), "postgres", "localhost", 5432));
        var resp = await session.Client.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<ServerEndpoints.ServerResponse>(ct))!;
    }

    private static async Task<CredentialEndpoints.CredentialResponse> AddCredentialAsync(
        AuthenticatedSession session, string xsrf, Guid serverId, string label, CancellationToken ct)
    {
        using var req = MutationRequest(HttpMethod.Post, $"/api/server/{serverId}/credential", xsrf,
            new CredentialEndpoints.AddCredentialRequest(label, "user_" + label, "pass_" + label));
        var resp = await session.Client.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<CredentialEndpoints.CredentialResponse>(ct))!;
    }

    [Fact]
    public async Task AddCredential_HappyPath_NeverReturnsPassword()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf) = await AuthorizedSessionAsync(ct);
        using var _ = session;
        var server = await CreateServerAsync(session, xsrf, ct);

        using var req = MutationRequest(HttpMethod.Post, $"/api/server/{server.Id}/credential", xsrf,
            new CredentialEndpoints.AddCredentialRequest("My cred", "alice", "s3cr3t"));
        var resp = await session.Client.SendAsync(req, ct);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

        var raw = await resp.Content.ReadAsStringAsync(ct);
        Assert.DoesNotContain("s3cr3t", raw);
    }

    [Fact]
    public async Task UpdateCredential_ChangesLabel()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf) = await AuthorizedSessionAsync(ct);
        using var _ = session;
        var server = await CreateServerAsync(session, xsrf, ct);
        var cred = await AddCredentialAsync(session, xsrf, server.Id.Value, "original", ct);

        using var req = MutationRequest(HttpMethod.Put, $"/api/server/{server.Id}/credential/{cred.Id}", xsrf,
            new CredentialEndpoints.UpdateCredentialRequest("updated", "user_updated"));
        var resp = await session.Client.SendAsync(req, ct);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<CredentialEndpoints.CredentialResponse>(ct);
        Assert.Equal("updated", body!.Label);
    }

    [Fact]
    public async Task DeleteCredential_ReferencedByActiveDatabase_Returns409()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf) = await AuthorizedSessionAsync(ct);
        using var _ = session;
        var server = await CreateServerAsync(session, xsrf, ct);
        var cred = await AddCredentialAsync(session, xsrf, server.Id.Value, "read", ct);

        // Create a database that references this credential
        using var dbReq = MutationRequest(HttpMethod.Post, $"/api/server/{server.Id}/database", xsrf,
            new DatabaseEndpoints.AddDatabaseRequest("App DB", "appdb", cred.Id));
        (await session.Client.SendAsync(dbReq, ct)).EnsureSuccessStatusCode();

        // Now try to delete the credential — should be blocked
        using var delReq = MutationRequest(HttpMethod.Delete, $"/api/server/{server.Id}/credential/{cred.Id}", xsrf);
        Assert.Equal(HttpStatusCode.Conflict, (await session.Client.SendAsync(delReq, ct)).StatusCode);
    }

    [Fact]
    public async Task DeleteCredential_AfterDatabaseSoftDeleted_Succeeds()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf) = await AuthorizedSessionAsync(ct);
        using var _ = session;
        var server = await CreateServerAsync(session, xsrf, ct);
        var cred = await AddCredentialAsync(session, xsrf, server.Id.Value, "read", ct);

        // Create then soft-delete the database
        using var dbReq = MutationRequest(HttpMethod.Post, $"/api/server/{server.Id}/database", xsrf,
            new DatabaseEndpoints.AddDatabaseRequest("App DB", "appdb", cred.Id));
        var dbResp = await session.Client.SendAsync(dbReq, ct);
        var db = (await dbResp.Content.ReadFromJsonAsync<DatabaseEndpoints.DatabaseResponse>(ct))!;

        using var dbDelReq = MutationRequest(HttpMethod.Delete, $"/api/server/{server.Id}/database/{db.Id}", xsrf);
        (await session.Client.SendAsync(dbDelReq, ct)).EnsureSuccessStatusCode();

        // Now the credential can be soft-deleted
        using var credDelReq = MutationRequest(HttpMethod.Delete, $"/api/server/{server.Id}/credential/{cred.Id}", xsrf);
        Assert.Equal(HttpStatusCode.NoContent, (await session.Client.SendAsync(credDelReq, ct)).StatusCode);
    }

    private sealed record ListUserBody(UserRow[] Users);
    private sealed record UserRow(string Id, string Email);
}
```

- [ ] **Step 2: Run tests**

```bash
dotnet test tests/IntegrationTests/IntegrationTests.csproj \
  --filter "FullyQualifiedName~CredentialEndpointTests" -v n
```

Expected: All `CredentialEndpointTests` pass.

- [ ] **Step 3: Commit**

```bash
git add tests/IntegrationTests/CredentialEndpointTests.cs
git commit -m "test: add CredentialEndpointTests — 409 guard and soft-delete lifecycle"
```

---

## Task 18: Add DatabaseEndpointTests

**Files:**
- Create: `tests/IntegrationTests/DatabaseEndpointTests.cs`

- [ ] **Step 1: Write the tests**

```csharp
// tests/IntegrationTests/DatabaseEndpointTests.cs
using System.Net;
using System.Net.Http.Json;
using Aspire.Hosting.Testing;
using IntegrationTests.Supports;
using Npgsql;
using SluiceBase.Api.Endpoints;
using SluiceBase.Core.Permissions;

namespace IntegrationTests;

public class DatabaseEndpointTests(SluiceBaseStackFactory factory)
{
    private KeycloakLoginHelper LoginHelper => new(factory.InitialisedApp);
    private static string UniqueName() => $"srv-{Guid.NewGuid():N}"[..24];

    private static HttpRequestMessage MutationRequest(HttpMethod method, string url, string xsrf, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Add("X-XSRF-TOKEN", xsrf);
        if (body is not null)
            req.Content = JsonContent.Create(body);
        return req;
    }

    private async Task<(AuthenticatedSession session, string xsrf)> AuthorizedSessionAsync(CancellationToken ct)
    {
        var session = await LoginHelper.SignInAsync("alice", "dev", ct);
        var xsrf = await session.FetchXsrfTokenAsync(ct);
        var users = await session.Client.GetFromJsonAsync<ListUserBody>("/api/admin/user", ct);
        var alice = Assert.Single(users!.Users, u => u.Email == "alice@example.com");
        using var grantReq = MutationRequest(HttpMethod.Post, $"/api/admin/user/{alice.Id}/permission", xsrf,
            new { permission = Permissions.ServerManage });
        (await session.Client.SendAsync(grantReq, ct)).EnsureSuccessStatusCode();
        return (session, xsrf);
    }

    // Helper: server + two credentials + one read-write database against the real blue-appdb
    private async Task<(ServerEndpoints.ServerResponse server, CredentialEndpoints.CredentialResponse readCred, CredentialEndpoints.CredentialResponse writeCred, DatabaseEndpoints.DatabaseResponse database)>
        SetupBlueServerAsync(AuthenticatedSession session, string xsrf, CancellationToken ct)
    {
        var connStr = await factory.InitialisedApp.GetConnectionStringAsync("blue-appdb", ct)
                      ?? throw new InvalidOperationException("blue-appdb not found");
        var pg = new NpgsqlConnectionStringBuilder(connStr);

        using var sReq = MutationRequest(HttpMethod.Post, "/api/server", xsrf,
            new ServerEndpoints.CreateServerRequest(UniqueName(), "postgres", pg.Host!, pg.Port));
        var server = (await (await session.Client.SendAsync(sReq, ct)).Content.ReadFromJsonAsync<ServerEndpoints.ServerResponse>(ct))!;

        using var rcReq = MutationRequest(HttpMethod.Post, $"/api/server/{server.Id}/credential", xsrf,
            new CredentialEndpoints.AddCredentialRequest("read", "reader_blue", "reader_blue"));
        var readCred = (await (await session.Client.SendAsync(rcReq, ct)).Content.ReadFromJsonAsync<CredentialEndpoints.CredentialResponse>(ct))!;

        using var wcReq = MutationRequest(HttpMethod.Post, $"/api/server/{server.Id}/credential", xsrf,
            new CredentialEndpoints.AddCredentialRequest("write", "writer_blue", "writer_blue"));
        var writeCred = (await (await session.Client.SendAsync(wcReq, ct)).Content.ReadFromJsonAsync<CredentialEndpoints.CredentialResponse>(ct))!;

        using var dbReq = MutationRequest(HttpMethod.Post, $"/api/server/{server.Id}/database", xsrf,
            new DatabaseEndpoints.AddDatabaseRequest("App DB", "appdb", readCred.Id, writeCred.Id));
        var database = (await (await session.Client.SendAsync(dbReq, ct)).Content.ReadFromJsonAsync<DatabaseEndpoints.DatabaseResponse>(ct))!;

        return (server, readCred, writeCred, database);
    }

    [Fact]
    public async Task AddDatabase_HappyPath_CanWrite_IsTrue()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf) = await AuthorizedSessionAsync(ct);
        using var _ = session;
        var (_, _, _, database) = await SetupBlueServerAsync(session, xsrf, ct);

        Assert.True(database.CanWrite);
        Assert.False(database.IsDisabled);
    }

    [Fact]
    public async Task CreateDatabase_WithSharedCredential_Succeeds()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf) = await AuthorizedSessionAsync(ct);
        using var _ = session;

        using var sReq = MutationRequest(HttpMethod.Post, "/api/server", xsrf,
            new ServerEndpoints.CreateServerRequest(UniqueName(), "postgres", "localhost", 5432));
        var server = (await (await session.Client.SendAsync(sReq, ct)).Content.ReadFromJsonAsync<ServerEndpoints.ServerResponse>(ct))!;

        using var rcReq = MutationRequest(HttpMethod.Post, $"/api/server/{server.Id}/credential", xsrf,
            new CredentialEndpoints.AddCredentialRequest("shared", "shared_user", "pass"));
        var sharedCred = (await (await session.Client.SendAsync(rcReq, ct)).Content.ReadFromJsonAsync<CredentialEndpoints.CredentialResponse>(ct))!;

        // Two databases on same server, same credential
        using var db1Req = MutationRequest(HttpMethod.Post, $"/api/server/{server.Id}/database", xsrf,
            new DatabaseEndpoints.AddDatabaseRequest("DB One", "db1", sharedCred.Id));
        using var db2Req = MutationRequest(HttpMethod.Post, $"/api/server/{server.Id}/database", xsrf,
            new DatabaseEndpoints.AddDatabaseRequest("DB Two", "db2", sharedCred.Id));

        Assert.Equal(HttpStatusCode.Created, (await session.Client.SendAsync(db1Req, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await session.Client.SendAsync(db2Req, ct)).StatusCode);
    }

    [Fact]
    public async Task TestConnection_MovedToDatabaseLevel_ReturnsReadAndWrite()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf) = await AuthorizedSessionAsync(ct);
        using var _ = session;
        var (server, _, _, database) = await SetupBlueServerAsync(session, xsrf, ct);

        using var req = MutationRequest(HttpMethod.Post, $"/api/server/{server.Id}/database/{database.Id}/test", xsrf);
        var resp = await session.Client.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        var body = await resp.Content.ReadFromJsonAsync<DatabaseEndpoints.TestConnectionResponse>(ct);
        Assert.True(body!.Read.Ok, body.Read.Error);
        Assert.True(body.Write?.Ok, body.Write?.Error);
    }

    [Fact]
    public async Task TestConnection_ReadOnlyDatabase_WriteIsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf) = await AuthorizedSessionAsync(ct);
        using var _ = session;

        using var sReq = MutationRequest(HttpMethod.Post, "/api/server", xsrf,
            new ServerEndpoints.CreateServerRequest(UniqueName(), "postgres", "localhost", 5432));
        var server = (await (await session.Client.SendAsync(sReq, ct)).Content.ReadFromJsonAsync<ServerEndpoints.ServerResponse>(ct))!;

        using var rcReq = MutationRequest(HttpMethod.Post, $"/api/server/{server.Id}/credential", xsrf,
            new CredentialEndpoints.AddCredentialRequest("read", "reader", "pass"));
        var readCred = (await (await session.Client.SendAsync(rcReq, ct)).Content.ReadFromJsonAsync<CredentialEndpoints.CredentialResponse>(ct))!;

        using var dbReq = MutationRequest(HttpMethod.Post, $"/api/server/{server.Id}/database", xsrf,
            new DatabaseEndpoints.AddDatabaseRequest("Read-only DB", "mydb", readCred.Id));
        var database = (await (await session.Client.SendAsync(dbReq, ct)).Content.ReadFromJsonAsync<DatabaseEndpoints.DatabaseResponse>(ct))!;

        using var testReq = MutationRequest(HttpMethod.Post, $"/api/server/{server.Id}/database/{database.Id}/test", xsrf);
        var body = await (await session.Client.SendAsync(testReq, ct)).Content
            .ReadFromJsonAsync<DatabaseEndpoints.TestConnectionResponse>(ct);
        Assert.Null(body!.Write);
    }

    private sealed record ListUserBody(UserRow[] Users);
    private sealed record UserRow(string Id, string Email);
}
```

- [ ] **Step 2: Run tests**

```bash
dotnet test tests/IntegrationTests/IntegrationTests.csproj \
  --filter "FullyQualifiedName~DatabaseEndpointTests" -v n
```

Expected: All `DatabaseEndpointTests` pass.

- [ ] **Step 3: Commit**

```bash
git add tests/IntegrationTests/DatabaseEndpointTests.cs
git commit -m "test: add DatabaseEndpointTests — shared credentials, test-connection at database level"
```

---

## Task 19: Update QueryEndpointTests, SchemaEndpointTests, UpdateEndpointTests

**Files:**
- Modify: `tests/IntegrationTests/QueryEndpointTest.cs`
- Modify: `tests/IntegrationTests/SchemaEndpointTests.cs`
- Modify: `tests/IntegrationTests/UpdateEndpointTests.cs`

All three files share the same structural change: the `AuthorizedSessionWithBlueServerAsync` (or `AliceWithBlueServerAsync`) helpers must now return a `DatabaseId` instead of a `ServerId`. The helper now calls the three-step creation sequence (server → credentials → database) using the real `blue-appdb` connection string.

- [ ] **Step 1: Update QueryEndpointTest.cs helper and request shape**

Find `AuthorizedSessionWithBlueServerAsync` and update it to return a `DatabaseId`. Replace the single `CreateServerAsync` call with the three-step sequence:

```csharp
// Updated helper signature and body in QueryEndpointTest.cs:
private async Task<(AuthenticatedSession session, string xsrf, DatabaseId databaseId)>
    AuthorizedSessionWithBlueServerAsync(CancellationToken ct)
{
    // ... grant query:execute permission to alice ...
    var connStr = await factory.InitialisedApp.GetConnectionStringAsync("blue-appdb", ct)
                  ?? throw new InvalidOperationException("blue-appdb not found");
    var pg = new NpgsqlConnectionStringBuilder(connStr);

    // Grant server:manage temporarily to create the server, then revoke (or keep — doesn't affect query tests)
    // Simpler: grant both permissions
    // ... create server, create read credential, create database ...
    // Return the DatabaseId
}
```

Replace all references to `ServerId` in `QueryRequest` with `DatabaseId`:
```csharp
// Old:
new QueryEndpoints.QueryRequest(serverId, "SELECT 1")
// New:
new QueryEndpoints.QueryRequest(databaseId, "SELECT 1")
```

Add new test cases for disabled database/server:

```csharp
[Fact]
public async Task Query_DisabledDatabase_Returns400()
{
    var ct = TestContext.Current.CancellationToken;
    var (session, xsrf, databaseId) = await AuthorizedSessionWithBlueServerAsync(ct);
    using var _ = session;

    // Disable the database — find its serverId and databaseId from the list
    var list = await session.Client.GetFromJsonAsync<ServerEndpoints.ListServersResponse>("/api/server", ct);
    var srv = list!.Servers.First(s => s.Databases.Any(d => d.Id == databaseId));
    var db = srv.Databases.First(d => d.Id == databaseId);

    using var disableReq = MutationRequest(HttpMethod.Put, $"/api/server/{srv.Id}/database/{db.Id}", xsrf,
        new DatabaseEndpoints.UpdateDatabaseRequest(db.DisplayName, db.DatabaseName, db.ReadCredentialId, db.WriteCredentialId, true));
    (await session.Client.SendAsync(disableReq, ct)).EnsureSuccessStatusCode();

    using var queryReq = MutationRequest(HttpMethod.Post, "/api/query", xsrf,
        new QueryEndpoints.QueryRequest(databaseId, "SELECT 1"));
    Assert.Equal(HttpStatusCode.BadRequest, (await session.Client.SendAsync(queryReq, ct)).StatusCode);
}

[Fact]
public async Task Query_DisabledServer_Returns400()
{
    var ct = TestContext.Current.CancellationToken;
    var (session, xsrf, databaseId) = await AuthorizedSessionWithBlueServerAsync(ct);
    using var _ = session;

    var list = await session.Client.GetFromJsonAsync<ServerEndpoints.ListServersResponse>("/api/server", ct);
    var srv = list!.Servers.First(s => s.Databases.Any(d => d.Id == databaseId));

    using var disableReq = MutationRequest(HttpMethod.Put, $"/api/server/{srv.Id}", xsrf,
        new ServerEndpoints.UpdateServerRequest(srv.Name, srv.Host, srv.Port, srv.Kind, true));
    (await session.Client.SendAsync(disableReq, ct)).EnsureSuccessStatusCode();

    using var queryReq = MutationRequest(HttpMethod.Post, "/api/query", xsrf,
        new QueryEndpoints.QueryRequest(databaseId, "SELECT 1"));
    Assert.Equal(HttpStatusCode.BadRequest, (await session.Client.SendAsync(queryReq, ct)).StatusCode);
}
```

- [ ] **Step 2: Update SchemaEndpointTests.cs helper**

Same structural change as QueryEndpointTest: update `AuthorizedSessionWithBlueServerAsync` to return `DatabaseId`. Update the path `/api/schema/{serverId}` → `/api/schema/{databaseId}` in the test request.

- [ ] **Step 3: Update UpdateEndpointTests.cs helper**

Update `AliceWithBlueServerAsync` to return a `DatabaseId`. Replace `SubmitUpdateRequest.ServerId` with `DatabaseId`. Replace `UpdateRequestDetailResponse.ServerId`/`ServerName` checks with `DatabaseId`/`DatabaseDisplayName`.

Also add `Query_DisabledServer_Returns400` equivalent for updates — `Submit_DisabledDatabase_Returns400`:

```csharp
[Fact]
public async Task Submit_DisabledDatabase_Returns400()
{
    var ct = TestContext.Current.CancellationToken;
    var (session, xsrf, databaseId) = await AliceWithBlueServerAsync(ct);
    using var _ = session;

    var list = await session.Client.GetFromJsonAsync<ServerEndpoints.ListServersResponse>("/api/server", ct);
    var srv = list!.Servers.First(s => s.Databases.Any(d => d.Id == databaseId));
    var db = srv.Databases.First(d => d.Id == databaseId);

    using var disableReq = MutationRequest(HttpMethod.Put, $"/api/server/{srv.Id}/database/{db.Id}", xsrf,
        new DatabaseEndpoints.UpdateDatabaseRequest(db.DisplayName, db.DatabaseName, db.ReadCredentialId, db.WriteCredentialId, true));
    (await session.Client.SendAsync(disableReq, ct)).EnsureSuccessStatusCode();

    using var submitReq = MutationRequest(HttpMethod.Post, "/api/update", xsrf,
        new UpdateEndpoints.SubmitUpdateRequest(databaseId, "UPDATE foo SET bar = 1", "test"));
    Assert.Equal(HttpStatusCode.BadRequest, (await session.Client.SendAsync(submitReq, ct)).StatusCode);
}
```

- [ ] **Step 4: Run all integration tests**

```bash
dotnet test tests/IntegrationTests/IntegrationTests.csproj -v n
```

Expected: All tests pass.

- [ ] **Step 5: Commit**

```bash
git add tests/IntegrationTests/QueryEndpointTest.cs \
        tests/IntegrationTests/SchemaEndpointTests.cs \
        tests/IntegrationTests/UpdateEndpointTests.cs
git commit -m "test: update Query, Schema, Update endpoint tests for DatabaseId and disabled-entity 400 cases"
```

---

## Task 20: Regenerate schema.ts and update hooks.ts

**Files:**
- Regenerate: `src/frontend/src/api/schema.ts` (auto-generated — run openapi-typescript)
- Modify: `src/frontend/src/api/hooks.ts`

- [ ] **Step 1: Write failing unit tests for new hooks first**

New hooks (`useCreateCredential`, `useDeleteCredential`, `useCreateDatabase`, `useDeleteDatabase`) don't exist yet. Write the test stubs so they fail at import:

```typescript
// In src/frontend/src/api/__tests__/server-hooks.test.ts — add at the bottom:

describe("useCreateCredential", () => {
  it("posts to /api/server/{serverId}/credential and invalidates ['server']", async () => {
    vi.mocked(apiRequest).mockResolvedValue({
      id: "cred-1",
      label: "read",
      username: "reader",
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
    });

    const { result } = renderHook(() => useCreateCredential("srv-1"), { wrapper });
    result.current.mutate({ label: "read", username: "reader", password: "pass" });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(apiRequest).toHaveBeenCalledWith(
      "/api/server/srv-1/credential",
      expect.objectContaining({ method: "POST" }),
    );
  });
});

describe("useDeleteCredential", () => {
  it("deletes /api/server/{serverId}/credential/{credentialId} and invalidates ['server']", async () => {
    vi.mocked(apiRequest).mockResolvedValue(undefined);
    const { result } = renderHook(() => useDeleteCredential("srv-1"), { wrapper });
    result.current.mutate("cred-1");
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(apiRequest).toHaveBeenCalledWith(
      "/api/server/srv-1/credential/cred-1",
      expect.objectContaining({ method: "DELETE" }),
    );
  });
});

describe("useCreateDatabase", () => {
  it("posts to /api/server/{serverId}/database and invalidates ['server']", async () => {
    vi.mocked(apiRequest).mockResolvedValue({
      id: "db-1", displayName: "App DB", databaseName: "appdb",
      readCredentialId: "cred-1", writeCredentialId: null,
      canWrite: false, isDisabled: false,
      createdAt: new Date().toISOString(), updatedAt: new Date().toISOString(),
    });
    const { result } = renderHook(() => useCreateDatabase("srv-1"), { wrapper });
    result.current.mutate({ displayName: "App DB", databaseName: "appdb", readCredentialId: "cred-1" });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(apiRequest).toHaveBeenCalledWith(
      "/api/server/srv-1/database",
      expect.objectContaining({ method: "POST" }),
    );
  });
});

describe("useDeleteDatabase", () => {
  it("deletes /api/server/{serverId}/database/{databaseId} and invalidates ['server']", async () => {
    vi.mocked(apiRequest).mockResolvedValue(undefined);
    const { result } = renderHook(() => useDeleteDatabase("srv-1"), { wrapper });
    result.current.mutate("db-1");
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(apiRequest).toHaveBeenCalledWith(
      "/api/server/srv-1/database/db-1",
      expect.objectContaining({ method: "DELETE" }),
    );
  });
});
```

Run to see them fail:
```bash
cd src/frontend && npm test -- --reporter=verbose 2>&1 | grep -A2 "FAIL\|useCreateCredential\|useDeleteCredential\|useCreateDatabase\|useDeleteDatabase"
```

Expected: tests fail with `useCreateCredential is not a function` (or similar import error).

- [ ] **Step 2: Regenerate schema.ts**

The backend must be running (via Aspire) for openapi-typescript to fetch the spec. If not running, start it first in another terminal (`dotnet run --project src/AppHost`), then:

```bash
cd src/frontend && npm run generate-api
```

Expected: `src/frontend/src/api/schema.ts` is updated with new paths including `/api/server/{serverId}/credential` and `/api/server/{serverId}/database/{databaseId}`.

- [ ] **Step 3: Update existing hooks in hooks.ts**

Make these targeted changes to the server-related hooks (keep all other hooks unchanged):

```typescript
// hooks.ts — update useServers return type annotation (auto-inferred from new schema.ts)
// useSchema: change parameter name and path
export function useSchema(databaseId: string | null) {
  return useQuery({
    queryKey: ["schema", databaseId] as const,
    enabled: databaseId !== null,
    queryFn: () =>
      apiRequest<void, paths["/api/schema/{databaseId}"]["get"]["responses"][200]["content"]["application/json"]>(
        `/api/schema/${databaseId}`,
      ),
  });
}

// useExecuteQuery: change body field name
export function useExecuteQuery() {
  return useMutation({
    mutationFn: ({ databaseId, sql }: { databaseId: string; sql: string }) =>
      apiRequest<
        paths["/api/query"]["post"]["requestBody"]["content"]["application/json"],
        paths["/api/query"]["post"]["responses"][200]["content"]["application/json"]
      >("/api/query", { method: "POST", body: { databaseId, sql } }),
  });
}

// useSubmitUpdate: change body field name
export function useSubmitUpdate() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ databaseId, sqlText, reason }: { databaseId: string; sqlText: string; reason: string }) =>
      apiRequest<
        paths["/api/update"]["post"]["requestBody"]["content"]["application/json"],
        paths["/api/update"]["post"]["responses"][201]["content"]["application/json"]
      >("/api/update", { method: "POST", body: { databaseId, sqlText, reason } }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["update"] }),
  });
}
```

- [ ] **Step 4: Add new hooks to hooks.ts**

```typescript
// Add after useDeleteServer:

export function useCreateCredential(serverId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: paths["/api/server/{serverId}/credential"]["post"]["requestBody"]["content"]["application/json"]) =>
      apiRequest<typeof req, paths["/api/server/{serverId}/credential"]["post"]["responses"][201]["content"]["application/json"]>(
        `/api/server/${serverId}/credential`,
        { method: "POST", body: req },
      ),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["server"] }),
  });
}

export function useUpdateCredential(serverId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ credentialId, ...req }: { credentialId: string } & paths["/api/server/{serverId}/credential/{credentialId}"]["put"]["requestBody"]["content"]["application/json"]) =>
      apiRequest<typeof req, paths["/api/server/{serverId}/credential/{credentialId}"]["put"]["responses"][200]["content"]["application/json"]>(
        `/api/server/${serverId}/credential/${credentialId}`,
        { method: "PUT", body: req },
      ),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["server"] }),
  });
}

export function useDeleteCredential(serverId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (credentialId: string) =>
      apiRequest<void, void>(`/api/server/${serverId}/credential/${credentialId}`, { method: "DELETE" }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["server"] }),
  });
}

export function useCreateDatabase(serverId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: paths["/api/server/{serverId}/database"]["post"]["requestBody"]["content"]["application/json"]) =>
      apiRequest<typeof req, paths["/api/server/{serverId}/database"]["post"]["responses"][201]["content"]["application/json"]>(
        `/api/server/${serverId}/database`,
        { method: "POST", body: req },
      ),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["server"] }),
  });
}

export function useUpdateDatabase(serverId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ databaseId, ...req }: { databaseId: string } & paths["/api/server/{serverId}/database/{databaseId}"]["put"]["requestBody"]["content"]["application/json"]) =>
      apiRequest<typeof req, paths["/api/server/{serverId}/database/{databaseId}"]["put"]["responses"][200]["content"]["application/json"]>(
        `/api/server/${serverId}/database/${databaseId}`,
        { method: "PUT", body: req },
      ),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["server"] }),
  });
}

export function useDeleteDatabase(serverId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (databaseId: string) =>
      apiRequest<void, void>(`/api/server/${serverId}/database/${databaseId}`, { method: "DELETE" }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["server"] }),
  });
}

export function useTestDatabaseConnection(serverId: string) {
  return useMutation({
    mutationFn: (databaseId: string) =>
      apiRequest<void, paths["/api/server/{serverId}/database/{databaseId}/test"]["post"]["responses"][200]["content"]["application/json"]>(
        `/api/server/${serverId}/database/${databaseId}/test`,
        { method: "POST" },
      ),
  });
}
```

- [ ] **Step 5: Run unit tests**

```bash
cd src/frontend && npm test
```

Expected: All hook tests pass including the new `useCreateCredential`, `useDeleteCredential`, `useCreateDatabase`, `useDeleteDatabase` tests.

- [ ] **Step 6: Commit**

```bash
git add src/frontend/src/api/schema.ts src/frontend/src/api/hooks.ts \
        src/frontend/src/api/__tests__/server-hooks.test.ts
git commit -m "feat: regenerate schema.ts and update hooks — databaseId, new credential/database hooks"
```

---

## Task 21: Refactor server.tsx — hierarchical UI

**Files:**
- Modify: `src/frontend/src/routes/_authed/server.tsx`

- [ ] **Step 1: Redesign the component**

The flat single-form table becomes a hierarchical view with expandable server rows. Each expanded row shows two sub-tables: Credentials and Databases.

Key state changes:
- `expandedServerId: string | null` — tracks which server row is expanded
- `credentialModal` state for add/edit credential form
- `databaseModal` state for add/edit database form

Key behavior:
- Server table columns: `Name`, `Kind`, `Host:Port`, `Disabled` badge, Expand button, Edit/Delete actions
- Credentials sub-table columns: `Label`, `Username`, Edit/Delete (disabled if any active database references it — check `server.databases.some(d => d.readCredentialId === cred.id || d.writeCredentialId === cred.id)`)
- Databases sub-table columns: `Display Name`, `DB Name`, `Read Cred`, `Write Cred`, `Disabled` badge, Toggle Disabled/Delete/Test Connection actions
- Add Server form: `name`, `kind`, `host`, `port` only
- Add Credential form (per server): `label`, `username`, `password`
- Add/Edit Database form (per server): `displayName`, `databaseName`, `readCredentialId` (Select from server's active credentials), `writeCredentialId` (optional Select), `isDisabled`
- Test Connection result shows `read: OK/Failed` and `write: OK/Failed/N/A`

```typescript
// src/frontend/src/routes/_authed/server.tsx
// Full rewrite — use useServers, useCreateServer, useUpdateServer, useDeleteServer,
// useCreateCredential, useUpdateCredential, useDeleteCredential,
// useCreateDatabase, useUpdateDatabase, useDeleteDatabase, useTestDatabaseConnection
//
// Pattern: for credential/database hooks that require serverId, instantiate them
// inside the expanded-row component (a sub-component) that receives serverId as a prop.
```

The cleanest implementation pattern: extract `ServerRow` as an inner component that receives `server` (one item from `useServers` data) as a prop and instantiates all the credential/database hooks itself.

```typescript
function ServerRow({ server, xsrf, onEdit, onDelete }: {
  server: ServerItem;
  xsrf: string;
  onEdit: (s: ServerItem) => void;
  onDelete: (s: ServerItem) => void;
}) {
  const [expanded, setExpanded] = useState(false);
  const createCred = useCreateCredential(server.id);
  const deleteCred = useDeleteCredential(server.id);
  const createDb = useCreateDatabase(server.id);
  const updateDb = useUpdateDatabase(server.id);
  const deleteDb = useDeleteDatabase(server.id);
  const testConn = useTestDatabaseConnection(server.id);
  // ... render table row + collapsible sub-tables
}
```

- [ ] **Step 2: Verify in browser**

Start Aspire and navigate to `/server`. Confirm:
1. Server table lists seeded Blue and Green servers with expanded credential and database sub-tables
2. Adding a server creates it with empty sub-tables
3. Adding a credential appears in the credentials sub-table immediately
4. Adding a database with `readCredentialId` populated appears in the databases sub-table
5. Deleting a credential that's referenced by a database shows an error toast (409)
6. Test connection on a database shows read/write results

- [ ] **Step 3: Commit**

```bash
git add src/frontend/src/routes/_authed/server.tsx
git commit -m "feat: refactor server management page — hierarchical server/credential/database view"
```

---

## Task 22: Update query.tsx and update/new.tsx

**Files:**
- Modify: `src/frontend/src/routes/_authed/query.tsx`
- Modify: `src/frontend/src/routes/_authed/update/new.tsx`

- [ ] **Step 1: Update query.tsx**

Changes from the current implementation:
1. Replace `selectedServerId` state with `selectedDatabaseId`
2. Build the database dropdown by flattening `servers.flatMap(s => s.databases)`, filtered to `!s.isDisabled && !d.isDisabled`
3. Dropdown `value` is `database.id`, label is `database.displayName`
4. `useSchema(selectedDatabaseId)` (was `useSchema(selectedServerId)`)
5. `executeQuery.mutate({ databaseId: selectedDatabaseId, sql })` (was `serverId`)
6. Placeholder text: `"Select a database"`

```typescript
// Key state change:
const [selectedDatabaseId, setSelectedDatabaseId] = useState<string | null>(null);

// Dropdown options:
const databaseOptions = (servers.data?.servers ?? [])
  .filter(s => !s.isDisabled)
  .flatMap(s => s.databases.filter(d => !d.isDisabled)
    .map(d => ({ value: d.id, label: `${s.name} — ${d.displayName}` })));

// Schema hook:
const schema = useSchema(selectedDatabaseId);

// Execute mutation call:
executeQuery.mutate({ databaseId: selectedDatabaseId!, sql });
```

- [ ] **Step 2: Update update/new.tsx**

Changes from the current implementation:
1. Replace `serverId` state with `databaseId`
2. Build the database dropdown filtering to `database.canWrite && !server.isDisabled && !database.isDisabled`
3. `submit.mutate({ databaseId, sqlText, reason })` (was `serverId`)
4. Label: `"Database"` (was `"Server"`)

```typescript
const [databaseId, setDatabaseId] = useState<string | null>(null);

const databaseOptions = (servers.data?.servers ?? [])
  .filter(s => !s.isDisabled)
  .flatMap(s => s.databases.filter(d => d.canWrite && !d.isDisabled)
    .map(d => ({ value: d.id, label: `${s.name} — ${d.displayName}` })));

// Submit:
submit.mutate({ databaseId: databaseId!, sqlText, reason });
```

- [ ] **Step 3: Verify in browser**

1. Navigate to `/query` — confirm database dropdown shows Blue App DB and Green App DB (if Green has a read credential)
2. Select a database, execute a query — confirm results
3. Navigate to `/update/new` — confirm only write-capable databases appear
4. Submit an update request — confirm success

- [ ] **Step 4: Commit**

```bash
git add src/frontend/src/routes/_authed/query.tsx \
        src/frontend/src/routes/_authed/update/new.tsx
git commit -m "feat: replace server selector with database selector in query and update-new pages"
```

---

## Task 23: Update all frontend unit tests

**Files:**
- Modify: `src/frontend/src/api/__tests__/server-hooks.test.ts`
- Modify: `src/frontend/src/api/__tests__/query-hooks.test.ts`
- Modify: `src/frontend/src/api/__tests__/schema-hooks.test.ts`
- Modify: `src/frontend/src/api/__tests__/update-hooks.test.ts`

- [ ] **Step 1: Update server-hooks.test.ts — useServers mock data shape**

```typescript
// Old mockData shape (remove these fields):
//   database, readUsername, hasReadPassword, writeUsername, hasWritePassword, isEnabled
// New mockData shape:
const mockData = {
  servers: [
    {
      id: "abc",
      name: "Blue",
      kind: "postgres",
      host: "localhost",
      port: 5432,
      isDisabled: false,
      credentials: [
        { id: "cred-1", label: "Read-only role", username: "reader_blue",
          createdAt: new Date().toISOString(), updatedAt: new Date().toISOString() },
      ],
      databases: [
        { id: "db-1", displayName: "Blue App DB", databaseName: "appdb",
          readCredentialId: "cred-1", writeCredentialId: "cred-2",
          canWrite: true, isDisabled: false,
          createdAt: new Date().toISOString(), updatedAt: new Date().toISOString() },
      ],
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
    },
  ],
};
```

Update assertions: remove `"readPassword" in server` and `"writePassword" in server` checks. Replace with `"credentials" in server` and `"databases" in server`.

Update `useCreateServer` test: new request body is `{ name, kind, host, port }` (no credential/database fields).

- [ ] **Step 2: Update query-hooks.test.ts**

Replace `serverId` with `databaseId` in all mock calls and mutation arguments:

```typescript
// Old:
result.current.mutate({ serverId: "srv-1", sql: "SELECT 1" });
expect(apiRequest).toHaveBeenCalledWith("/api/query", expect.objectContaining({
  body: expect.objectContaining({ serverId: "srv-1" }),
}));

// New:
result.current.mutate({ databaseId: "db-1", sql: "SELECT 1" });
expect(apiRequest).toHaveBeenCalledWith("/api/query", expect.objectContaining({
  body: expect.objectContaining({ databaseId: "db-1" }),
}));
```

- [ ] **Step 3: Update schema-hooks.test.ts**

Replace `serverId` parameter and path with `databaseId`:

```typescript
// Old:
const { result } = renderHook(() => useSchema("srv-1"), { wrapper });
expect(apiRequest).toHaveBeenCalledWith("/api/schema/srv-1");

// New:
const { result } = renderHook(() => useSchema("db-1"), { wrapper });
expect(apiRequest).toHaveBeenCalledWith("/api/schema/db-1");
```

- [ ] **Step 4: Update update-hooks.test.ts**

Replace `serverId` with `databaseId` in `useSubmitUpdate` test:

```typescript
// Old:
result.current.mutate({ serverId: "srv-1", sqlText: "UPDATE ...", reason: "test" });
// New:
result.current.mutate({ databaseId: "db-1", sqlText: "UPDATE ...", reason: "test" });
```

- [ ] **Step 5: Run all frontend tests**

```bash
cd src/frontend && npm test
```

Expected: All tests pass.

- [ ] **Step 6: Run TypeScript type check**

```bash
cd src/frontend && npm run tsc
```

Expected: No type errors.

- [ ] **Step 7: Commit**

```bash
git add src/frontend/src/api/__tests__/server-hooks.test.ts \
        src/frontend/src/api/__tests__/query-hooks.test.ts \
        src/frontend/src/api/__tests__/schema-hooks.test.ts \
        src/frontend/src/api/__tests__/update-hooks.test.ts
git commit -m "test: update all frontend unit tests for new schema — databaseId, nested server response"
```

---

## Self-Review

### Spec coverage

| Spec requirement | Task |
|-----------------|------|
| `Server` entity: `Id`, `Name`, `Kind`, `Host`, `Port`, `IsDisabled`, `DeletedAt`, `CreatedAt`, `UpdatedAt` | Task 4 |
| `Credential` entity with `Label`, `Username`, `EncryptedPassword`, `DeletedAt` | Task 2 |
| `Database` entity with `DisplayName`, `DatabaseName`, `ReadCredentialId`, `WriteCredentialId?`, `IsDisabled`, `DeletedAt` | Task 3 |
| `CanWrite => WriteCredentialId.HasValue` | Task 3 |
| Soft-delete cascades via `Server.SoftDelete()` | Task 4 |
| Individual credential delete rejected if active database references it | Task 12 |
| `IsDisabled` default `false`; replaces `IsEnabled` | Tasks 4, 11 |
| Partial unique index on `server.name WHERE deleted_at IS NULL` | Task 6 |
| `QueryLog.DatabaseId` / `UpdateRequest.DatabaseId` | Task 7 |
| `OnDelete(DeleteBehavior.Restrict)` on both FK columns | Tasks 6, 8 |
| Connection factory: loads `Database → Server + Credential`, checks `IsDisabled` | Task 10 |
| `GET /api/server` nested `credentials[]` + `databases[]` | Task 11 |
| `POST/PUT/DELETE /api/server` simplified (server fields only) | Task 11 |
| `POST/PUT/DELETE /api/server/{id}/credential` | Task 12 |
| `POST/PUT/DELETE /api/server/{id}/database` | Task 13 |
| `POST /api/server/{id}/database/{dbId}/test` (moved from server level) | Task 13 |
| `POST /api/query` body: `databaseId` | Task 14 |
| `GET /api/schema/{databaseId}` | Task 14 |
| `POST /api/update` body: `databaseId` | Task 14 |
| `Query_DisabledDatabase_Returns400`, `Query_DisabledServer_Returns400` | Task 19 |
| `DeleteCredential_ReferencedByActiveDatabase_Returns409` | Task 17 |
| `DeleteCredential_AfterDatabaseSoftDeleted_Succeeds` | Task 17 |
| `SoftDeleteServer_CascadesToCredentialsAndDatabases` | Task 16 |
| `TestConnection_MovedToDatabaseLevel` | Task 18 |
| `CreateDatabase_WithSharedCredential_Succeeds` | Task 18 |
| DevServerSeed: server → credential(s) → database | Task 15 |
| Drop + recreate migrations | Task 9 |
| Frontend: database dropdown in query page | Task 22 |
| Frontend: writable database dropdown in update/new | Task 22 |
| Frontend: hierarchical server management page | Task 21 |
| Frontend hooks: new credential/database hooks | Task 20 |
| Frontend unit tests | Task 23 |

### Placeholder scan

No TODOs, TBDs, or "implement later" in any task. All code blocks are complete. ✓

### Type consistency

- `CredentialId` / `DatabaseId` introduced in Task 1, used consistently in Tasks 2–14.
- `Server.SoftDelete(DateTimeOffset)` defined in Task 4, called in Task 11.
- `Credential.Update(label, username, string?, DateTimeOffset)` defined in Task 2, called in Task 12.
- `Database.Update(displayName, databaseName, CredentialId, CredentialId?, bool, DateTimeOffset)` defined in Task 3, called in Task 13.
- `QueryLog.Create(UserId?, DatabaseId?, ...)` defined in Task 7, called in Task 14.
- `UpdateRequest.Create(DatabaseId, ...)` defined in Task 7, called in Task 14.
- `ServerConnectionFactory.GetConnectionStringAsync(DatabaseId, CredentialKind, CancellationToken)` defined in Task 10, called in Tasks 13, 14.
- Frontend `useSchema(databaseId)` defined in Task 20, used in Task 22.
- Frontend `useExecuteQuery` takes `{ databaseId, sql }` defined in Task 20, used in Task 22.
- Frontend `useSubmitUpdate` takes `{ databaseId, sqlText, reason }` defined in Task 20, used in Task 22.
