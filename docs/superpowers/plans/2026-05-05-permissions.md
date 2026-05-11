# Permissions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add per-user permissions stored in the metadata DB, enforced by ASP.NET policies, managed via an in-app admin UI.

**Architecture:** Vogen-typed `User` and `UserPermission` domain models live in `SluiceBase.Core`; EF `IEntityTypeConfiguration<T>` + a single migration live in `SluiceBase.Api`; a `UserLoginRecorder` upserts the user row on every OIDC login and re-applies bootstrap grants from config; a request-scoped `CurrentUserAccessor` issues one indexed DB read per request; a scoped `PermissionAuthorizationHandler` gates each policy check against that cached result; three new admin endpoints (`/api/permission/catalog`, `/api/admin/user`, POST/DELETE `/api/admin/user/{userId}/permission`) are gated by `permission:manage`; a Mantine admin page at `/admin/permission` lets holders of `permission:manage` toggle grants for any user.

**Tech Stack:** .NET 10 / ASP.NET Core 10 / EF Core 10 / Vogen / Npgsql; React 19 / TanStack Router + Query / Mantine v9 / @mantine/modals / Vitest / Playwright.

---

## File Structure

### New files
- `src/SluiceBase.Core/AssemblyAttributes.cs` — Vogen project-level defaults for Core
- `src/SluiceBase.Core/Users/UserId.cs` — Vogen-typed user PK
- `src/SluiceBase.Core/Users/User.cs` — rich User domain model
- `src/SluiceBase.Core/Permissions/UserPermissionId.cs` — Vogen-typed permission PK
- `src/SluiceBase.Core/Permissions/UserPermission.cs` — grant record domain model
- `src/SluiceBase.Core/Permissions/Permissions.cs` — fixed catalog of 6 permission strings
- `src/SluiceBase.Api/AssemblyAttributes.cs` — Vogen project-level defaults for Api
- `src/SluiceBase.Api/Data/Configurations/UserConfiguration.cs` — EF config for User
- `src/SluiceBase.Api/Data/Configurations/UserPermissionConfiguration.cs` — EF config for UserPermission
- `src/SluiceBase.Api/Auth/BootstrapAdminOptions.cs` — options bound from `Permissions:Bootstrap`
- `src/SluiceBase.Api/Auth/UserLoginRecorder.cs` — upserts user + applies bootstrap grants
- `src/SluiceBase.Api/Auth/PermissionRequirement.cs` — ASP.NET IAuthorizationRequirement
- `src/SluiceBase.Api/Auth/CurrentUserAccessor.cs` — request-scoped user loader
- `src/SluiceBase.Api/Auth/PermissionAuthorizationHandler.cs` — evaluates PermissionRequirement
- `src/SluiceBase.Api/Endpoints/PermissionsEndpoints.cs` — catalog + admin endpoints
- `tests/IntegrationTests/Supports/KeycloakLoginHelper.cs` — drives OIDC code-flow in tests
- `tests/IntegrationTests/MeEndpointTests.cs` — /api/me integration tests
- `tests/IntegrationTests/PermissionCatalogTests.cs` — /api/permission/catalog tests
- `tests/IntegrationTests/AdminPermissionsTests.cs` — admin grant/revoke tests
- `src/frontend/src/auth/permissions.ts` — useHasPermission hook
- `src/frontend/src/auth/__tests__/permission.test.ts` — Vitest unit tests for useHasPermission
- `src/frontend/src/routes/_authed/admin/permission.tsx` — admin page UI
- `src/frontend/e2e/admin-permission.spec.ts` — Playwright E2E for grant flow

### Modified files
- `src/SluiceBase.Core/SluiceBase.Core.csproj` — add Vogen package
- `src/SluiceBase.Api/SluiceBase.Api.csproj` — add Vogen + Vogen.EntityFrameworkCore
- `src/SluiceBase.Api/Data/AppDbContext.cs` — add Users/UserPermissions DbSets + OnModelCreating
- `src/SluiceBase.Api/Auth/AuthSetup.cs` — add OnTokenValidated, policy registration, DI for auth types
- `src/SluiceBase.Api/Program.cs` — add DI registrations, fix antiforgery cookie name
- `src/SluiceBase.Api/appsettings.Development.json` — add bootstrap admin list
- `src/SluiceBase.Api/Endpoints/AuthEndpoints.cs` — update /api/me response shape
- `src/SluiceBase.Api/Endpoints/EndpointMapper.cs` — register PermissionsEndpoints
- `src/frontend/src/api/hooks.ts` — update MeResponse; add admin query/mutation hooks
- `src/frontend/src/routes/_authed.tsx` — fix display name fallback; add conditional Permission nav link
- `src/frontend/src/routes/_authed/index.tsx` — fix display name fallback
- `src/frontend/src/main.tsx` — add ModalsProvider
- `src/frontend/package.json` — add @mantine/modals

### Auto-generated
- `src/SluiceBase.Api/Data/Migrations/*_AddUserAndPermission.cs` — created by `dotnet ef migrations add`

---

## Task 1: Add Vogen packages and assembly defaults

**Files:**
- Modify: `src/SluiceBase.Core/SluiceBase.Core.csproj`
- Modify: `src/SluiceBase.Api/SluiceBase.Api.csproj`
- Create: `src/SluiceBase.Core/AssemblyAttributes.cs`
- Create: `src/SluiceBase.Api/AssemblyAttributes.cs`

- [ ] **Step 1: Add Vogen to Core project**

Run from repo root:
```bash
dotnet add src/SluiceBase.Core/SluiceBase.Core.csproj package Vogen
```

- [ ] **Step 2: Add Vogen packages to Api project**

```bash
dotnet add src/SluiceBase.Api/SluiceBase.Api.csproj package Vogen
dotnet add src/SluiceBase.Api/SluiceBase.Api.csproj package Vogen.EntityFrameworkCore
```

- [ ] **Step 3: Create `src/SluiceBase.Core/AssemblyAttributes.cs`**

```csharp
using Vogen;

[assembly: VogenDefaults(
    conversions: Conversions.SystemTextJson | Conversions.TypeConverter)]
```

- [ ] **Step 4: Create `src/SluiceBase.Api/AssemblyAttributes.cs`**

```csharp
using Vogen;

[assembly: VogenDefaults(
    conversions: Conversions.SystemTextJson | Conversions.TypeConverter)]
```

- [ ] **Step 5: Verify build**

```bash
dotnet build SluiceBase.slnx
```

Expected: Build succeeded with no errors.

- [ ] **Step 6: Commit**

```bash
git add src/SluiceBase.Core/SluiceBase.Core.csproj \
        src/SluiceBase.Api/SluiceBase.Api.csproj \
        src/SluiceBase.Core/AssemblyAttributes.cs \
        src/SluiceBase.Api/AssemblyAttributes.cs
git commit -m "chore: add Vogen and Vogen.EntityFrameworkCore packages"
```

---

## Task 2: Core domain models

**Files:**
- Create: `src/SluiceBase.Core/Users/UserId.cs`
- Create: `src/SluiceBase.Core/Users/User.cs`
- Create: `src/SluiceBase.Core/Permissions/UserPermissionId.cs`
- Create: `src/SluiceBase.Core/Permissions/UserPermission.cs`
- Create: `src/SluiceBase.Core/Permissions/Permissions.cs`

- [ ] **Step 1: Create `src/SluiceBase.Core/Users/UserId.cs`**

```csharp
using Vogen;

namespace SluiceBase.Core.Users;

[ValueObject<Guid>]
public readonly partial struct UserId;
```

- [ ] **Step 2: Create `src/SluiceBase.Core/Permissions/UserPermissionId.cs`**

```csharp
using Vogen;

namespace SluiceBase.Core.Permissions;

[ValueObject<Guid>]
public readonly partial struct UserPermissionId;
```

- [ ] **Step 3: Create `src/SluiceBase.Core/Users/User.cs`**

```csharp
using SluiceBase.Core.Permissions;

namespace SluiceBase.Core.Users;

public sealed class User
{
    private readonly List<UserPermission> _permissions = [];

    private User() { }

    private User(UserId id, string sub, string email, string? name, DateTimeOffset at)
    {
        Id = id;
        Sub = sub;
        Email = email;
        Name = name;
        LastLoginAt = at;
    }

    public UserId Id { get; private set; }
    public string Sub { get; private set; } = "";
    public string Email { get; private set; } = "";
    public string? Name { get; private set; }
    public DateTimeOffset LastLoginAt { get; private set; }

    public IReadOnlyList<UserPermission> Permissions => _permissions;

    public static User Create(string sub, string email, string? name, DateTimeOffset at) =>
        new(UserId.From(Guid.NewGuid()), sub, email, name, at);

    public void RecordLogin(string email, string? name, DateTimeOffset at)
    {
        Email = email;
        Name = name;
        LastLoginAt = at;
    }

    public bool HasPermission(string permission) =>
        _permissions.Any(p => p.Permission == permission);
}
```

- [ ] **Step 4: Create `src/SluiceBase.Core/Permissions/UserPermission.cs`**

```csharp
using SluiceBase.Core.Users;

namespace SluiceBase.Core.Permissions;

public sealed class UserPermission
{
    private UserPermission() { }

    private UserPermission(
        UserPermissionId id, UserId userId, string permission,
        UserId? grantedById, DateTimeOffset at)
    {
        Id = id;
        UserId = userId;
        Permission = permission;
        GrantedById = grantedById;
        GrantedAt = at;
    }

    public UserPermissionId Id { get; private set; }
    public UserId UserId { get; private set; }
    public string Permission { get; private set; } = "";
    public DateTimeOffset GrantedAt { get; private set; }
    public UserId? GrantedById { get; private set; }

    public static UserPermission Grant(
        UserId userId, string permission, UserId? grantedById, DateTimeOffset at) =>
        new(UserPermissionId.From(Guid.NewGuid()), userId, permission, grantedById, at);
}
```

- [ ] **Step 5: Create `src/SluiceBase.Core/Permissions/Permissions.cs`**

```csharp
namespace SluiceBase.Core.Permissions;

public static class Permissions
{
    public const string PermissionManage = "permission:manage";
    public const string ServerManage = "server:manage";
    public const string QueryExecute = "query:execute";
    public const string UpdateSubmit = "update:submit";
    public const string UpdateApprove = "update:approve";
    public const string UpdateExecute = "update:execute";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        PermissionManage,
        ServerManage,
        QueryExecute,
        UpdateSubmit,
        UpdateApprove,
        UpdateExecute,
    };
}
```

- [ ] **Step 6: Build Core to verify**

```bash
dotnet build src/SluiceBase.Core/SluiceBase.Core.csproj
```

Expected: Build succeeded.

- [ ] **Step 7: Commit**

```bash
git add src/SluiceBase.Core/
git commit -m "feat: add User, UserPermission domain models and Permissions catalog"
```

---

## Task 3: EF entity configurations and AppDbContext update

**Files:**
- Create: `src/SluiceBase.Api/Data/Configurations/UserConfiguration.cs`
- Create: `src/SluiceBase.Api/Data/Configurations/UserPermissionConfiguration.cs`
- Modify: `src/SluiceBase.Api/Data/AppDbContext.cs`

- [ ] **Step 1: Create `src/SluiceBase.Api/Data/Configurations/UserConfiguration.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SluiceBase.Core.Users;
using SluiceBase.Core.Permissions;
using Vogen.EntityFrameworkCore;

namespace SluiceBase.Api.Data.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.HasKey(u => u.Id);
        b.Property(u => u.Id).HasVogenConversion();
        b.Property(u => u.Sub).HasMaxLength(255).IsRequired();
        b.HasIndex(u => u.Sub).IsUnique();
        b.Property(u => u.Email).HasMaxLength(320).IsRequired();
        b.Property(u => u.Name).HasMaxLength(255);
        b.HasMany(u => u.Permissions).WithOne()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

- [ ] **Step 2: Create `src/SluiceBase.Api/Data/Configurations/UserPermissionConfiguration.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Users;
using Vogen.EntityFrameworkCore;

namespace SluiceBase.Api.Data.Configurations;

internal sealed class UserPermissionConfiguration : IEntityTypeConfiguration<UserPermission>
{
    public void Configure(EntityTypeBuilder<UserPermission> b)
    {
        b.HasKey(p => p.Id);
        b.Property(p => p.Id).HasVogenConversion();
        b.Property(p => p.UserId).HasVogenConversion();
        b.Property(p => p.Permission).HasMaxLength(64).IsRequired();
        b.HasIndex(p => new { p.UserId, p.Permission }).IsUnique();
        b.Property(p => p.GrantedAt).IsRequired();
        b.Property(p => p.GrantedById).HasVogenConversion();
        b.HasOne<User>()
            .WithMany()
            .HasForeignKey(p => p.GrantedById)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
```

- [ ] **Step 3: Update `src/SluiceBase.Api/Data/AppDbContext.cs`**

Replace the entire file:

```csharp
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Users;

namespace SluiceBase.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserPermission> UserPermissions => Set<UserPermission>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
        configurationBuilder.Conventions.Remove<TableNameFromDbSetConvention>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
```

- [ ] **Step 4: Build to verify**

```bash
dotnet build src/SluiceBase.Api/SluiceBase.Api.csproj
```

Expected: Build succeeded.

---

## Task 4: Generate EF migration

**Files:**
- Auto-create: `src/SluiceBase.Api/Data/Migrations/*_AddUserAndPermission.cs`

- [ ] **Step 1: Add migration**

```bash
dotnet ef migrations add AddUserAndPermission --project src/SluiceBase.Api
```

Expected: A new migration file appears in `src/SluiceBase.Api/Data/Migrations/`.

- [ ] **Step 2: Verify migration content**

Open the generated file and confirm it creates two tables. It should contain:
- `migrationBuilder.CreateTable(name: "user", ...)` with columns: `id` (uuid), `sub` (varchar 255, not null), `email` (varchar 320, not null), `name` (varchar 255, nullable), `last_login_at` (timestamptz, not null)
- `migrationBuilder.CreateTable(name: "user_permission", ...)` with columns: `id` (uuid), `user_id` (uuid FK), `permission` (varchar 64, not null), `granted_at` (timestamptz, not null), `granted_by_id` (uuid, nullable FK)
- Unique index on `user.sub`
- Unique index on `user_permission(user_id, permission)`
- FK from `user_permission.user_id` → `user.id` (CASCADE)
- FK from `user_permission.granted_by_id` → `user.id` (SET NULL)

If any columns are missing, fix the domain model or EF configuration in Tasks 2–3 and re-add the migration.

- [ ] **Step 3: Commit**

```bash
git add src/SluiceBase.Api/Data/
git commit -m "feat: add EF migration AddUserAndPermission with user and user_permission tables"
```

---

## Task 5: Bootstrap config and login recorder

**Files:**
- Create: `src/SluiceBase.Api/Auth/BootstrapAdminOptions.cs`
- Create: `src/SluiceBase.Api/Auth/UserLoginRecorder.cs`
- Modify: `src/SluiceBase.Api/appsettings.Development.json`

- [ ] **Step 1: Create `src/SluiceBase.Api/Auth/BootstrapAdminOptions.cs`**

```csharp
namespace SluiceBase.Api.Auth;

public sealed class BootstrapAdminOptions
{
    public const string SectionName = "Permissions:Bootstrap";
    public IList<string> Admins { get; set; } = [];
}
```

- [ ] **Step 2: Create `src/SluiceBase.Api/Auth/UserLoginRecorder.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SluiceBase.Api.Data;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Users;

namespace SluiceBase.Api.Auth;

internal interface IUserLoginRecorder
{
    Task<User> RecordLoginAsync(
        string sub, string email, string? name,
        DateTimeOffset at, CancellationToken ct);
}

internal sealed class UserLoginRecorder(
    AppDbContext db,
    IOptions<BootstrapAdminOptions> bootstrap) : IUserLoginRecorder
{
    public async Task<User> RecordLoginAsync(
        string sub, string email, string? name,
        DateTimeOffset at, CancellationToken ct)
    {
        var user = await db.Users
            .Include(u => u.Permissions)
            .SingleOrDefaultAsync(u => u.Sub == sub, ct);

        if (user is null)
        {
            user = User.Create(sub, email, name, at);
            db.Users.Add(user);
        }
        else
        {
            user.RecordLogin(email, name, at);
        }

        var emailMatchesBootstrap = bootstrap.Value.Admins.Any(b =>
            string.Equals(b, email, StringComparison.OrdinalIgnoreCase));

        if (emailMatchesBootstrap && !user.HasPermission(Permissions.PermissionManage))
        {
            db.UserPermissions.Add(UserPermission.Grant(
                user.Id, Permissions.PermissionManage, grantedById: null, at));
        }

        await db.SaveChangesAsync(ct);
        return user;
    }
}
```

- [ ] **Step 3: Update `src/SluiceBase.Api/appsettings.Development.json`**

Replace entire file:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Information",
      "Microsoft.EntityFrameworkCore": "Warning"
    }
  },
  "Migrations": {
    "AutoApply": true
  },
  "ConnectionStrings": {
    "Metadata": "Host=localhost;Port=5432;Database=sluicebase_dev_design;Username=postgres;Password=postgres"
  },
  "Permissions": {
    "Bootstrap": {
      "Admins": ["alice@example.com"]
    }
  }
}
```

- [ ] **Step 4: Build to verify**

```bash
dotnet build src/SluiceBase.Api/SluiceBase.Api.csproj
```

Expected: Build succeeded.

---

## Task 6: Permission authorization infrastructure

**Files:**
- Create: `src/SluiceBase.Api/Auth/PermissionRequirement.cs`
- Create: `src/SluiceBase.Api/Auth/CurrentUserAccessor.cs`
- Create: `src/SluiceBase.Api/Auth/PermissionAuthorizationHandler.cs`

- [ ] **Step 1: Create `src/SluiceBase.Api/Auth/PermissionRequirement.cs`**

```csharp
using Microsoft.AspNetCore.Authorization;

namespace SluiceBase.Api.Auth;

internal sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}
```

- [ ] **Step 2: Create `src/SluiceBase.Api/Auth/CurrentUserAccessor.cs`**

```csharp
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SluiceBase.Api.Data;
using SluiceBase.Core.Users;

namespace SluiceBase.Api.Auth;

internal interface ICurrentUserAccessor
{
    Task<User?> GetAsync(CancellationToken ct);
}

internal sealed class CurrentUserAccessor(
    IHttpContextAccessor http,
    AppDbContext db) : ICurrentUserAccessor
{
    private User? _cached;
    private bool _loaded;

    public async Task<User?> GetAsync(CancellationToken ct)
    {
        if (_loaded) return _cached;
        _loaded = true;

        var sub = http.HttpContext?.User.FindFirstValue("sub");
        if (string.IsNullOrEmpty(sub)) return null;

        _cached = await db.Users
            .Include(u => u.Permissions)
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.Sub == sub, ct);
        return _cached;
    }
}
```

- [ ] **Step 3: Create `src/SluiceBase.Api/Auth/PermissionAuthorizationHandler.cs`**

> **Note:** The spec listed this as `AddSingleton`, but `ICurrentUserAccessor` is scoped. Registering a singleton that depends on a scoped service throws at startup. This handler is correctly registered as `AddScoped` in Task 7.

```csharp
using Microsoft.AspNetCore.Authorization;

namespace SluiceBase.Api.Auth;

internal sealed class PermissionAuthorizationHandler(
    ICurrentUserAccessor currentUser) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext ctx, PermissionRequirement req)
    {
        var user = await currentUser.GetAsync(CancellationToken.None);
        if (user?.HasPermission(req.Permission) == true)
        {
            ctx.Succeed(req);
        }
    }
}
```

- [ ] **Step 4: Build to verify**

```bash
dotnet build src/SluiceBase.Api/SluiceBase.Api.csproj
```

Expected: Build succeeded.

---

## Task 7: Wire up AuthSetup — OnTokenValidated, policies, DI

**Files:**
- Modify: `src/SluiceBase.Api/Auth/AuthSetup.cs`

Replace the entire file. The key changes are:
1. Add `OnTokenValidated` inside `.AddOpenIdConnect(...)` to call `IUserLoginRecorder`.
2. Replace the bare `services.AddAuthorization()` at the bottom with one that registers a policy per permission string.
3. Register `IUserLoginRecorder`, `ICurrentUserAccessor`, `IAuthorizationHandler`, and `IHttpContextAccessor`.
4. Bind `BootstrapAdminOptions` from config.

- [ ] **Step 1: Replace `src/SluiceBase.Api/Auth/AuthSetup.cs`**

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using SluiceBase.Core.Permissions;

namespace SluiceBase.Api.Auth;

internal static class AuthSetup
{
    private const string CookieName = "sb.auth";

    public static IHostApplicationBuilder AddSluiceBaseAuth(
        this IHostApplicationBuilder builder)
    {
        var services = builder.Services;
        var config = builder.Configuration;

        services
            .AddAuthentication(options =>
            {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
            })
            .AddCookie(options =>
            {
                options.Cookie.Name = CookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.SlidingExpiration = true;
                options.ExpireTimeSpan = TimeSpan.FromHours(8);

                options.Events.OnRedirectToLogin = ctx =>
                {
                    if (ctx.Request.Path.StartsWithSegments("/api"))
                    {
                        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return Task.CompletedTask;
                    }

                    ctx.Response.Redirect(ctx.RedirectUri);
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = ctx =>
                {
                    if (ctx.Request.Path.StartsWithSegments("/api"))
                    {
                        ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                        return Task.CompletedTask;
                    }

                    ctx.Response.Redirect(ctx.RedirectUri);
                    return Task.CompletedTask;
                };
            })
            .AddOpenIdConnect(options =>
            {
                options.Authority = config["Oidc:Authority"];
                options.ClientId = config["Oidc:ClientId"];
                options.ClientSecret = config["Oidc:ClientSecret"];
                options.ResponseType = "code";
                options.UsePkce = true;
                options.SaveTokens = true;
                options.GetClaimsFromUserInfoEndpoint = true;

                options.Scope.Clear();
                options.Scope.Add("openid");
                options.Scope.Add("profile");
                options.Scope.Add("email");

                options.CallbackPath = "/signin-oidc";
                options.SignedOutCallbackPath = "/signout-callback-oidc";

                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    NameClaimType = "preferred_username",
                    RoleClaimType = "role"
                };

                options.ClaimActions.MapJsonKey("preferred_username", "preferred_username");
                options.ClaimActions.MapJsonSubKey("role", "realm_access", "roles");

                options.Events.OnRedirectToIdentityProvider = ctx =>
                {
                    if (ctx.Request.Path.StartsWithSegments("/api") &&
                        !ctx.Request.Path.StartsWithSegments("/api/auth/login"))
                    {
                        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        ctx.HandleResponse();
                        return Task.CompletedTask;
                    }

                    return Task.CompletedTask;
                };

                options.Events.OnTokenValidated = async ctx =>
                {
                    var requestServices = ctx.HttpContext.RequestServices;
                    var recorder = requestServices.GetRequiredService<IUserLoginRecorder>();
                    var clock = requestServices.GetRequiredService<TimeProvider>();

                    var sub = ctx.Principal!.FindFirstValue("sub")!;
                    var email = ctx.Principal.FindFirstValue("email") ?? "";
                    var name = ctx.Principal.FindFirstValue("name");

                    await recorder.RecordLoginAsync(
                        sub, email, name, clock.GetUtcNow(),
                        ctx.HttpContext.RequestAborted);
                };
            });

        services.AddAuthorization(options =>
        {
            foreach (var permission in Permissions.All)
            {
                options.AddPolicy(permission,
                    policy => policy.Requirements.Add(new PermissionRequirement(permission)));
            }
        });

        services.AddScoped<IUserLoginRecorder, UserLoginRecorder>();
        services.AddScoped<ICurrentUserAccessor, CurrentUserAccessor>();
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddHttpContextAccessor();
        services.Configure<BootstrapAdminOptions>(
            config.GetSection(BootstrapAdminOptions.SectionName));

        return builder;
    }
}
```

- [ ] **Step 2: Build to verify**

```bash
dotnet build src/SluiceBase.Api/SluiceBase.Api.csproj
```

Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/SluiceBase.Api/Auth/ src/SluiceBase.Api/appsettings.Development.json
git commit -m "feat: add UserLoginRecorder, bootstrap config, permission authorization handler"
```

---

## Task 8: Update Program.cs and API endpoints

**Files:**
- Modify: `src/SluiceBase.Api/Program.cs`
- Modify: `src/SluiceBase.Api/Endpoints/AuthEndpoints.cs`
- Create: `src/SluiceBase.Api/Endpoints/PermissionsEndpoints.cs`
- Modify: `src/SluiceBase.Api/Endpoints/EndpointMapper.cs`

- [ ] **Step 1: Update `src/SluiceBase.Api/Program.cs`**

Replace the entire file:

```csharp
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using SluiceBase.Api.Auth;
using SluiceBase.Api.Data;
using SluiceBase.Api.Endpoints;
using SluiceBase.Api.Targets;
using SluiceBase.Core.Targets;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.AddNpgsqlDbContext<AppDbContext>("Metadata",
    configureDbContextOptions: opt => { opt.UseSnakeCaseNamingConvention(); });

builder.Services.AddDataProtection()
    .PersistKeysToDbContext<AppDbContext>();

builder.AddSluiceBaseAuth();

builder.Services.AddAntiforgery(o =>
{
    o.HeaderName = "X-XSRF-TOKEN";
    o.Cookie.Name = "XSRF-TOKEN";
});

builder.Services.AddOpenApi();

builder.Services.AddSingleton<ITargetEngine, PostgresTargetEngine>();
builder.Services.AddSingleton(TimeProvider.System);

var app = builder.Build();

if (app.Environment.IsDevelopment()
    && builder.Configuration.GetValue("Migrations:AutoApply", false))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapOpenApi();
app.MapDefaultEndpoints();
app.MapAllEndpoints();

app.Run();

public partial class Program;
```

> `TimeProvider.System` must be registered so that `UserLoginRecorder` can receive `TimeProvider` via DI.

- [ ] **Step 2: Update `src/SluiceBase.Api/Endpoints/AuthEndpoints.cs`**

Replace the entire file. The `/api/me` endpoint now loads the user from `ICurrentUserAccessor` and returns a typed `MeResponse` record (with `Id` added so the frontend can identify the current user's row in the admin table):

```csharp
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using SluiceBase.Api.Auth;
using SluiceBase.Core.Users;

namespace SluiceBase.Api.Endpoints;

internal static class AuthEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/login",
                (string? returnUrl) =>
                {
                    var redirectUri = string.IsNullOrEmpty(returnUrl) ? "/" : returnUrl;
                    return Results.Challenge(
                        new AuthenticationProperties { RedirectUri = redirectUri },
                        authenticationSchemes: [OpenIdConnectDefaults.AuthenticationScheme]);
                })
            .WithName("Login")
            .AllowAnonymous();

        app.MapGet("/logout",
                () =>
                    Results.SignOut(
                        new AuthenticationProperties { RedirectUri = "/" },
                        authenticationSchemes:
                        [
                            CookieAuthenticationDefaults.AuthenticationScheme,
                            OpenIdConnectDefaults.AuthenticationScheme
                        ]))
            .WithName("Logout")
            .AllowAnonymous();

        app.MapGet("/api/me",
                async (ICurrentUserAccessor currentUser, CancellationToken ct) =>
                {
                    var user = await currentUser.GetAsync(ct);
                    if (user is null) return Results.Unauthorized();

                    return Results.Ok(new MeResponse(
                        Id: user.Id,
                        Sub: user.Sub,
                        Email: user.Email,
                        Name: user.Name,
                        Permissions: user.Permissions.Select(p => p.Permission).ToArray()));
                })
            .WithName("Me")
            .RequireAuthorization();

        app.MapGet("/api/antiforgery-token",
                (HttpContext ctx, IAntiforgery antiforgery) =>
                {
                    var tokens = antiforgery.GetAndStoreTokens(ctx);
                    return Results.Ok(new { headerName = tokens.HeaderName });
                })
            .WithName("AntiforgeryToken")
            .RequireAuthorization();
    }
}

internal sealed record MeResponse(
    UserId Id,
    string Sub,
    string Email,
    string? Name,
    string[] Permissions);
```

- [ ] **Step 3: Create `src/SluiceBase.Api/Endpoints/PermissionsEndpoints.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using SluiceBase.Api.Auth;
using SluiceBase.Api.Data;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Users;

namespace SluiceBase.Api.Endpoints;

internal static class PermissionsEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/permission/catalog",
                () => Results.Ok(new PermissionCatalogResponse(Permissions.All.ToArray())))
            .WithName("PermissionCatalog")
            .RequireAuthorization();

        var admin = app.MapGroup("/api/admin")
            .RequireAuthorization(Permissions.PermissionManage);

        admin.MapGet("/user", ListUsers).WithName("ListUsers");
        admin.MapPost("/user/{userId}/permission", GrantPermission)
            .WithName("GrantPermission");
        admin.MapDelete("/user/{userId}/permission/{permission}", RevokePermission)
            .WithName("RevokePermission");
    }

    private static async Task<IResult> ListUsers(AppDbContext db, CancellationToken ct)
    {
        var users = await db.Users
            .AsNoTracking()
            .OrderBy(u => u.Email)
            .Select(u => new UserSummaryResponse(
                u.Id, u.Sub, u.Email, u.Name, u.LastLoginAt,
                u.Permissions.Select(p => p.Permission).ToArray()))
            .ToListAsync(ct);
        return Results.Ok(new ListUsersResponse(users));
    }

    private static async Task<IResult> GrantPermission(
        UserId userId,
        GrantPermissionRequest req,
        AppDbContext db,
        ICurrentUserAccessor currentUser,
        TimeProvider clock,
        CancellationToken ct)
    {
        if (!Permissions.All.Contains(req.Permission))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["permission"] = [$"'{req.Permission}' is not a known permission."]
            });
        }

        var user = await db.Users
            .Include(u => u.Permissions)
            .SingleOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return Results.NotFound();

        if (user.HasPermission(req.Permission)) return Results.Ok();

        var actor = await currentUser.GetAsync(ct);
        db.UserPermissions.Add(UserPermission.Grant(
            user.Id, req.Permission, actor?.Id, clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);

        return Results.Created(
            $"/api/admin/user/{userId}/permission/{req.Permission}", null);
    }

    private static async Task<IResult> RevokePermission(
        UserId userId,
        string permission,
        AppDbContext db,
        CancellationToken ct)
    {
        var grant = await db.UserPermissions.SingleOrDefaultAsync(
            p => p.UserId == userId && p.Permission == permission, ct);
        if (grant is not null)
        {
            db.UserPermissions.Remove(grant);
            await db.SaveChangesAsync(ct);
        }
        return Results.NoContent();
    }
}

internal sealed record PermissionCatalogResponse(string[] Permissions);
internal sealed record GrantPermissionRequest(string Permission);
internal sealed record UserSummaryResponse(
    UserId Id, string Sub, string Email, string? Name,
    DateTimeOffset LastLoginAt, string[] Permissions);
internal sealed record ListUsersResponse(IReadOnlyList<UserSummaryResponse> Users);
```

- [ ] **Step 4: Update `src/SluiceBase.Api/Endpoints/EndpointMapper.cs`**

```csharp
namespace SluiceBase.Api.Endpoints;

internal static class EndpointMapper
{
    public static IEndpointRouteBuilder MapAllEndpoints(this IEndpointRouteBuilder app)
    {
        AuthEndpoints.Map(app);
        HealthEndpoints.Map(app);
        PermissionsEndpoints.Map(app);
        return app;
    }
}
```

- [ ] **Step 5: Build and verify whole solution**

```bash
dotnet build SluiceBase.slnx
```

Expected: Build succeeded with no errors.

- [ ] **Step 6: Commit**

```bash
git add src/SluiceBase.Api/
git commit -m "feat: add permission endpoints and update /api/me response shape"
```

---

## Task 9: Frontend — update MeResponse and fix display names

**Files:**
- Modify: `src/frontend/src/api/hooks.ts`
- Modify: `src/frontend/src/routes/_authed.tsx`
- Modify: `src/frontend/src/routes/_authed/index.tsx`

The existing `MeResponse` has `preferredUsername` and `roles` which the new `/api/me` no longer returns. Adding `id` and `permissions`.

- [ ] **Step 1: Update `src/frontend/src/api/hooks.ts`**

Replace the entire file:

```ts
import { QueryCache, QueryClient, queryOptions, useQuery } from "@tanstack/react-query";
import { ApiError, apiRequest } from "@/api/client";

export interface MeResponse {
  id: string;
  sub: string | null;
  email: string | null;
  name: string | null;
  permissions: Array<string>;
}

export interface AuthedHealthResponse {
  status: string;
  user: string | null;
}

export function createAppQueryClient(): QueryClient {
  return new QueryClient({
    queryCache: new QueryCache({
      onError: (error) => {
        if (error instanceof ApiError && error.status === 401) {
          window.location.assign("/login");
        }
      },
    }),
    defaultOptions: {
      queries: {
        retry: (failureCount, error) => {
          if (error instanceof ApiError && error.status === 401) {
            return false;
          }
          return failureCount < 2;
        },
        staleTime: 30_000,
      },
    },
  });
}

export const meQueryOptions = queryOptions({
  queryKey: ["me"] as const,
  queryFn: () => apiRequest<MeResponse>("/api/me"),
});

export function useMe() {
  return useQuery(meQueryOptions);
}

export function useAuthedHealth() {
  return useQuery({
    queryKey: ["health-authed"] as const,
    queryFn: () => apiRequest<AuthedHealthResponse>("/api/health/authed"),
  });
}
```

- [ ] **Step 2: Fix display name in `src/frontend/src/routes/_authed.tsx`**

Change line 39 from:
```tsx
const displayName = me.data.name ?? me.data.preferredUsername ?? me.data.email ?? "user";
```
to:
```tsx
const displayName = me.data.name ?? me.data.email ?? "user";
```

- [ ] **Step 3: Fix display name in `src/frontend/src/routes/_authed/index.tsx`**

Change line 12 from:
```tsx
const displayName = user.name ?? user.preferredUsername ?? user.email ?? "stranger";
```
to:
```tsx
const displayName = user.name ?? user.email ?? "stranger";
```

- [ ] **Step 4: Build to verify TypeScript**

Run from `src/frontend/`:
```bash
npm run build
```

Expected: TypeScript compiles clean, Vite build succeeds.

---

## Task 10: Vitest tests for useHasPermission (TDD — write test first)

**Files:**
- Create: `src/frontend/src/auth/__tests__/permission.test.ts`

Write the test before creating `permissions.ts`. The file will fail to compile until Task 11.

- [ ] **Step 1: Create `src/frontend/src/auth/__tests__/permission.test.ts`**

```ts
import { afterEach, describe, expect, it, vi } from "vitest";
import { renderHook } from "@testing-library/react";
import * as hooksModule from "../../api/hooks";
import { useHasPermission } from "../permissions";

vi.mock("../../api/hooks", () => ({
  useMe: vi.fn(),
}));

const mockUseMe = vi.mocked(hooksModule.useMe);

afterEach(() => {
  vi.clearAllMocks();
});

describe("useHasPermission", () => {
  it("returns true when permission is in the permissions array", () => {
    mockUseMe.mockReturnValue({
      data: {
        id: "user-1",
        sub: "alice-sub",
        email: "alice@example.com",
        name: "Alice",
        permissions: ["permission:manage", "query:execute"],
      },
    } as ReturnType<typeof hooksModule.useMe>);

    const { result } = renderHook(() => useHasPermission("permission:manage"));

    expect(result.current).toBe(true);
  });

  it("returns false when permission is not in the array", () => {
    mockUseMe.mockReturnValue({
      data: {
        id: "user-2",
        sub: "bob-sub",
        email: "bob@example.com",
        name: "Bob",
        permissions: ["query:execute"],
      },
    } as ReturnType<typeof hooksModule.useMe>);

    const { result } = renderHook(() => useHasPermission("permission:manage"));

    expect(result.current).toBe(false);
  });

  it("returns false while useMe has no data (loading)", () => {
    mockUseMe.mockReturnValue({
      data: undefined,
    } as ReturnType<typeof hooksModule.useMe>);

    const { result } = renderHook(() => useHasPermission("permission:manage"));

    expect(result.current).toBe(false);
  });
});
```

- [ ] **Step 2: Run tests — expect failure**

Run from `src/frontend/`:
```bash
npm run test
```

Expected: Error — `../permissions` module not found (or similar import error). This confirms TDD red state.

---

## Task 11: Implement useHasPermission (TDD — make tests green)

**Files:**
- Create: `src/frontend/src/auth/permissions.ts`

- [ ] **Step 1: Create `src/frontend/src/auth/permissions.ts`**

```ts
import { useMe } from "../api/hooks";

export function useHasPermission(permission: string): boolean {
  const me = useMe();
  return me.data?.permissions.includes(permission) ?? false;
}
```

- [ ] **Step 2: Run tests — expect pass**

Run from `src/frontend/`:
```bash
npm run test
```

Expected: All 3 `useHasPermission` tests pass. Existing tests in `api/__tests__/` also pass.

- [ ] **Step 3: Commit**

```bash
git add src/frontend/src/auth/
git commit -m "feat: add useHasPermission hook with Vitest tests"
```

---

## Task 12: Frontend — admin query hooks, @mantine/modals, navbar

**Files:**
- Modify: `src/frontend/package.json` (add @mantine/modals)
- Modify: `src/frontend/src/main.tsx` (add ModalsProvider)
- Modify: `src/frontend/src/api/hooks.ts` (add admin hooks and type interfaces)
- Modify: `src/frontend/src/routes/_authed.tsx` (add conditional Permission nav link)

- [ ] **Step 1: Install @mantine/modals**

Run from `src/frontend/`:
```bash
npm install @mantine/modals
```

- [ ] **Step 2: Update `src/frontend/src/main.tsx`**

Add the import and wrap `RouterProvider` with `ModalsProvider`. Replace entire file:

```tsx
import "@mantine/core/styles.css";
import "@mantine/notifications/styles.css";
import "@mantine/modals/styles.css";

import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { MantineProvider } from "@mantine/core";
import { ModalsProvider } from "@mantine/modals";
import { Notifications } from "@mantine/notifications";
import { QueryClientProvider } from "@tanstack/react-query";
import { ReactQueryDevtools } from "@tanstack/react-query-devtools";
import { RouterProvider, createRouter } from "@tanstack/react-router";
import { TanStackRouterDevtools } from "@tanstack/react-router-devtools";
import { createAppQueryClient } from "./api/hooks";
import { theme } from "./theme/theme";
// @ts-ignore — generated at build/dev time by @tanstack/router-plugin
import { routeTree } from "./routeTree.gen";

const queryClient = createAppQueryClient();

const router = createRouter({
  routeTree,
  context: { queryClient },
  defaultPreload: "intent",
});

declare module "@tanstack/react-router" {
  interface Register {
    router: typeof router;
  }
}

const rootElement = document.getElementById("root")!;
createRoot(rootElement).render(
  <StrictMode>
    <MantineProvider theme={theme} defaultColorScheme="auto">
      <ModalsProvider>
        <Notifications />
        <QueryClientProvider client={queryClient}>
          <RouterProvider router={router} />
          <ReactQueryDevtools initialIsOpen={false} />
          <TanStackRouterDevtools router={router} initialIsOpen={false} />
        </QueryClientProvider>
      </ModalsProvider>
    </MantineProvider>
  </StrictMode>,
);
```

- [ ] **Step 3: Add admin interfaces and hooks to `src/frontend/src/api/hooks.ts`**

Append to the end of the existing file (after `useAuthedHealth`):

```ts
export interface UserSummary {
  id: string;
  sub: string;
  email: string;
  name: string | null;
  lastLoginAt: string;
  permissions: Array<string>;
}

export interface ListUsersResponse {
  users: Array<UserSummary>;
}

export interface PermissionCatalogResponse {
  permissions: Array<string>;
}
```

Then add the following query/mutation hooks after the interfaces:

```ts
import {
  QueryCache,
  QueryClient,
  queryOptions,
  useMutation,
  useQuery,
  useQueryClient,
} from "@tanstack/react-query";
import { notifications } from "@mantine/notifications";
```

Wait — the imports at the top of the file need updating too. Replace the entire `hooks.ts` file with the full version below:

```ts
import {
  QueryCache,
  QueryClient,
  queryOptions,
  useMutation,
  useQuery,
  useQueryClient,
} from "@tanstack/react-query";
import { notifications } from "@mantine/notifications";
import { ApiError, apiRequest } from "@/api/client";

export interface MeResponse {
  id: string;
  sub: string | null;
  email: string | null;
  name: string | null;
  permissions: Array<string>;
}

export interface AuthedHealthResponse {
  status: string;
  user: string | null;
}

export interface UserSummary {
  id: string;
  sub: string;
  email: string;
  name: string | null;
  lastLoginAt: string;
  permissions: Array<string>;
}

export interface ListUsersResponse {
  users: Array<UserSummary>;
}

export interface PermissionCatalogResponse {
  permissions: Array<string>;
}

export function createAppQueryClient(): QueryClient {
  return new QueryClient({
    queryCache: new QueryCache({
      onError: (error) => {
        if (error instanceof ApiError && error.status === 401) {
          window.location.assign("/login");
        }
      },
    }),
    defaultOptions: {
      queries: {
        retry: (failureCount, error) => {
          if (error instanceof ApiError && error.status === 401) {
            return false;
          }
          return failureCount < 2;
        },
        staleTime: 30_000,
      },
    },
  });
}

export const meQueryOptions = queryOptions({
  queryKey: ["me"] as const,
  queryFn: () => apiRequest<MeResponse>("/api/me"),
});

export function useMe() {
  return useQuery(meQueryOptions);
}

export function useAuthedHealth() {
  return useQuery({
    queryKey: ["health-authed"] as const,
    queryFn: () => apiRequest<AuthedHealthResponse>("/api/health/authed"),
  });
}

export function useUsers() {
  return useQuery({
    queryKey: ["admin", "user"] as const,
    queryFn: () => apiRequest<ListUsersResponse>("/api/admin/user"),
  });
}

export function usePermissionCatalog() {
  return useQuery({
    queryKey: ["permission", "catalog"] as const,
    queryFn: () => apiRequest<PermissionCatalogResponse>("/api/permission/catalog"),
    staleTime: Infinity,
  });
}

function formatApiError(error: ApiError): string {
  const body = error.body as Record<string, unknown> | null;
  if (body && typeof body === "object" && "errors" in body) {
    const errors = body["errors"] as Record<string, string[]>;
    return Object.values(errors).flat().join(" ");
  }
  return error.message;
}

export function useGrantPermission() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({
      userId,
      permission,
    }: {
      userId: string;
      permission: string;
    }) =>
      apiRequest<void>(`/api/admin/user/${userId}/permission`, {
        method: "POST",
        body: { permission },
      }),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ["admin", "user"] });
      notifications.show({ title: "Permission granted", message: "", color: "teal" });
    },
    onError: (error) => {
      notifications.show({
        title: "Grant failed",
        message: error instanceof ApiError ? formatApiError(error) : error.message,
        color: "red",
      });
    },
  });
}

export function useRevokePermission() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({
      userId,
      permission,
    }: {
      userId: string;
      permission: string;
    }) =>
      apiRequest<void>(`/api/admin/user/${userId}/permission/${permission}`, {
        method: "DELETE",
      }),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ["admin", "user"] });
      notifications.show({ title: "Permission revoked", message: "", color: "teal" });
    },
    onError: (error) => {
      notifications.show({
        title: "Revoke failed",
        message: error instanceof ApiError ? formatApiError(error) : error.message,
        color: "red",
      });
    },
  });
}
```

- [ ] **Step 4: Add conditional Permission nav link to `src/frontend/src/routes/_authed.tsx`**

Replace the entire file:

```tsx
import {
  ActionIcon,
  AppShell,
  Avatar,
  Burger,
  Group,
  Menu,
  NavLink,
  Title,
  useMantineColorScheme,
} from "@mantine/core";
import { useDisclosure } from "@mantine/hooks";
import {
  IconHeartRateMonitor,
  IconHome,
  IconLogout,
  IconMoon,
  IconShieldLock,
  IconSun,
} from "@tabler/icons-react";
import { Link, Outlet, createFileRoute, useLocation } from "@tanstack/react-router";
import { useMe } from "@/api/hooks.ts";
import { AuthProvider } from "@/auth/AuthProvider.tsx";
import { useHasPermission } from "@/auth/permissions.ts";

export const Route = createFileRoute("/_authed")({
  component: AuthedLayout,
});

function AuthedLayout() {
  const me = useMe();
  const [opened, { toggle }] = useDisclosure();
  const { colorScheme, toggleColorScheme } = useMantineColorScheme();
  const location = useLocation();
  const isAdmin = useHasPermission("permission:manage");

  if (!me.data) {
    return null;
  }

  const displayName = me.data.name ?? me.data.email ?? "user";

  return (
    <AuthProvider user={me.data}>
      <AppShell
        header={{ height: 56 }}
        navbar={{
          width: 240,
          breakpoint: "sm",
          collapsed: { mobile: !opened },
        }}
        padding="md"
      >
        <AppShell.Header>
          <Group h="100%" px="md" justify="space-between">
            <Group gap="sm">
              <Burger opened={opened} onClick={toggle} hiddenFrom="sm" size="sm" />
              <Title order={3}>SluiceBase</Title>
            </Group>
            <Group gap="xs">
              <ActionIcon
                variant="subtle"
                onClick={() => toggleColorScheme()}
                aria-label="Toggle color scheme"
              >
                {colorScheme === "dark" ? <IconSun size={18} /> : <IconMoon size={18} />}
              </ActionIcon>
              <Menu position="bottom-end" withinPortal>
                <Menu.Target>
                  <Avatar
                    data-testid={"user-menu"}
                    name={displayName}
                    color={"initials"}
                    style={{ cursor: "pointer" }}
                  />
                </Menu.Target>
                <Menu.Dropdown>
                  <Menu.Item
                    component="a"
                    href="/logout"
                    leftSection={<IconLogout size={14} />}
                  >
                    Log out
                  </Menu.Item>
                </Menu.Dropdown>
              </Menu>
            </Group>
          </Group>
        </AppShell.Header>

        <AppShell.Navbar p="sm">
          <NavLink
            label="Home"
            leftSection={<IconHome size={16} />}
            component={Link}
            to="/"
            active={location.pathname === "/"}
          />
          <NavLink
            label="Health"
            leftSection={<IconHeartRateMonitor size={16} />}
            component={Link}
            to="/health"
            active={location.pathname === "/health"}
          />
          {isAdmin && (
            <NavLink
              label="Permission"
              leftSection={<IconShieldLock size={16} />}
              component={Link}
              to="/admin/permission"
              active={location.pathname === "/admin/permission"}
            />
          )}
        </AppShell.Navbar>

        <AppShell.Main>
          <Outlet />
        </AppShell.Main>
      </AppShell>
    </AuthProvider>
  );
}
```

> `useHasPermission` is used here with the `@/` alias. If TypeScript complains about the alias in vitest, note that this file is not tested by vitest — it's covered by E2E and integration tests.

- [ ] **Step 5: Build to verify**

Run from `src/frontend/`:
```bash
npm run build
```

Expected: TypeScript compiles clean, Vite build succeeds. `@mantine/notifications` import in hooks.ts may need the `notifications` import adjusted if Mantine v9 has a different export — check the build output.

- [ ] **Step 6: Also update `AuthProvider.tsx` since `MeResponse` type changed**

The `AuthProvider` accepts `user: MeResponse`. Since `MeResponse` now has `id` and `permissions` instead of `preferredUsername` and `roles`, verify `AuthProvider.tsx` still compiles (it just passes the type through, so no change needed).

Also verify `AuthContext.tsx` compiles (same — no change needed).

---

## Task 13: Admin permission page

**Files:**
- Create: `src/frontend/src/routes/_authed/admin/permission.tsx`

- [ ] **Step 1: Create `src/frontend/src/routes/_authed/admin/permission.tsx`**

```tsx
import { Badge, Card, Stack, Switch, Table, Text, TextInput, Title } from "@mantine/core";
import { modals } from "@mantine/modals";
import { createFileRoute, redirect } from "@tanstack/react-router";
import { useState } from "react";
import {
  useGrantPermission,
  useMe,
  usePermissionCatalog,
  useRevokePermission,
  useUsers,
  type UserSummary,
} from "@/api/hooks";
import { meQueryOptions } from "@/api/hooks";

const PERMISSION_LABELS: Record<string, { short: string; full: string }> = {
  "permission:manage": { short: "Permission", full: "Manage permissions" },
  "server:manage": { short: "Server", full: "Manage servers" },
  "query:execute": { short: "Query", full: "Run read queries" },
  "update:submit": { short: "Submit", full: "Submit update requests" },
  "update:approve": { short: "Approve", full: "Approve update requests" },
  "update:execute": { short: "Execute", full: "Execute approved updates" },
};

export const Route = createFileRoute("/_authed/admin/permission")({
  beforeLoad: ({ context }) => {
    const me = context.queryClient.getQueryData(meQueryOptions.queryKey);
    if (!me?.permissions.includes("permission:manage")) {
      throw redirect({ to: "/" });
    }
  },
  component: PermissionsAdminPage,
});

function PermissionsAdminPage() {
  const me = useMe();
  const users = useUsers();
  const catalog = usePermissionCatalog();
  const grant = useGrantPermission();
  const revoke = useRevokePermission();
  const [search, setSearch] = useState("");

  const permissions = catalog.data?.permissions ?? [];
  const allUsers = users.data?.users ?? [];
  const filtered = allUsers.filter(
    (u) =>
      u.email.toLowerCase().includes(search.toLowerCase()) ||
      (u.name ?? "").toLowerCase().includes(search.toLowerCase()),
  );

  const isMutating = (userId: string) =>
    (grant.isPending && grant.variables?.userId === userId) ||
    (revoke.isPending && revoke.variables?.userId === userId);

  function confirmSelfRevoke(): Promise<boolean> {
    return new Promise((resolve) => {
      modals.openConfirmModal({
        title: "Revoke your own admin permission?",
        children: (
          <Text size="sm">
            You will lose access to this page. The bootstrap config will re-grant
            permission:manage on your next login if your email is listed there.
          </Text>
        ),
        labels: { confirm: "Revoke", cancel: "Cancel" },
        confirmProps: { color: "red" },
        onConfirm: () => resolve(true),
        onCancel: () => resolve(false),
        onClose: () => resolve(false),
      });
    });
  }

  async function handleToggle(user: UserSummary, permission: string, nextValue: boolean) {
    if (
      !nextValue &&
      permission === "permission:manage" &&
      user.id === me.data?.id
    ) {
      const confirmed = await confirmSelfRevoke();
      if (!confirmed) return;
    }

    if (nextValue) {
      grant.mutate({ userId: user.id, permission });
    } else {
      revoke.mutate({ userId: user.id, permission });
    }
  }

  if (allUsers.length === 0 && !users.isLoading) {
    return (
      <Stack gap="md">
        <Title order={2}>Permission management</Title>
        <Card withBorder>
          <Text c="dimmed" size="sm">
            No users yet. Sign in as a bootstrap admin to populate the user table.
          </Text>
        </Card>
      </Stack>
    );
  }

  return (
    <Stack gap="md">
      <Title order={2}>Permission management</Title>
      <TextInput
        placeholder="Filter by email or name…"
        value={search}
        onChange={(e) => setSearch(e.currentTarget.value)}
        style={{ maxWidth: 320 }}
      />
      <Table.ScrollContainer minWidth={600}>
        <Table stickyHeader striped highlightOnHover>
          <Table.Thead>
            <Table.Tr>
              <Table.Th>User</Table.Th>
              <Table.Th>Last login</Table.Th>
              {permissions.map((p) => (
                <Table.Th key={p}>
                  {PERMISSION_LABELS[p]?.short ?? p}
                </Table.Th>
              ))}
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {filtered.map((user) => (
              <Table.Tr key={user.id}>
                <Table.Td>
                  <Stack gap={2}>
                    <Text size="sm" fw={500}>
                      {user.email}
                      {user.id === me.data?.id && (
                        <Badge ml={6} size="xs" variant="outline">
                          you
                        </Badge>
                      )}
                    </Text>
                    {user.name && (
                      <Text size="xs" c="dimmed">
                        {user.name}
                      </Text>
                    )}
                  </Stack>
                </Table.Td>
                <Table.Td>
                  <Text size="xs" c="dimmed">
                    {new Intl.DateTimeFormat("en", {
                      dateStyle: "medium",
                      timeStyle: "short",
                    }).format(new Date(user.lastLoginAt))}
                  </Text>
                </Table.Td>
                {permissions.map((permission) => (
                  <Table.Td key={permission}>
                    <Switch
                      checked={user.permissions.includes(permission)}
                      disabled={isMutating(user.id)}
                      aria-label={
                        PERMISSION_LABELS[permission]?.full ?? permission
                      }
                      onChange={(e) =>
                        void handleToggle(user, permission, e.currentTarget.checked)
                      }
                    />
                  </Table.Td>
                ))}
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      </Table.ScrollContainer>
    </Stack>
  );
}
```

- [ ] **Step 2: Build to verify**

Run from `src/frontend/`:
```bash
npm run build
```

Expected: TypeScript compiles clean. TanStack Router regenerates `routeTree.gen.ts` to include the new `/_authed/admin/permission` route.

- [ ] **Step 3: Commit**

```bash
git add src/frontend/
git commit -m "feat: add admin permission page with grant/revoke UI"
```

---

## Task 14: Backend integration test helper

**Files:**
- Create: `tests/IntegrationTests/Supports/KeycloakLoginHelper.cs`

`KeycloakLoginHelper` drives the OIDC authorization-code flow programmatically by scraping the Keycloak login form and submitting credentials. It returns an `AuthenticatedSession` that the tests use to make authenticated requests and fetch the antiforgery token for mutations.

- [ ] **Step 1: Create `tests/IntegrationTests/Supports/KeycloakLoginHelper.cs`**

```csharp
using System.Net;
using System.Text.RegularExpressions;
using System.Web;
using Aspire.Hosting.Testing;

namespace IntegrationTests.Supports;

public sealed class AuthenticatedSession(HttpClient client, CookieContainer cookies) : IDisposable
{
    public HttpClient Client => client;

    public async Task<string> FetchXsrfTokenAsync(CancellationToken ct = default)
    {
        using var response = await client.GetAsync("/api/antiforgery-token", ct);
        response.EnsureSuccessStatusCode();
        var cookie = cookies.GetCookies(client.BaseAddress!)["XSRF-TOKEN"];
        return Uri.UnescapeDataString(cookie?.Value ??
            throw new InvalidOperationException("XSRF-TOKEN cookie not set after /api/antiforgery-token"));
    }

    public void Dispose() => client.Dispose();
}

public sealed class KeycloakLoginHelper(DistributedApplication app)
{
    private static readonly Regex FormActionRegex = new(
        """<form[^>]+id="kc-form-login"[^>]+action="(?<action>[^"]+)""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public async Task<AuthenticatedSession> SignInAsync(
        string username,
        string password,
        CancellationToken ct = default)
    {
        var cookies = new CookieContainer();
        using var loginHandler = new HttpClientHandler
        {
            CookieContainer = cookies,
            AllowAutoRedirect = true,
            UseCookies = true,
        };
        using var loginClient = new HttpClient(loginHandler)
        {
            BaseAddress = app.GetEndpoint("api", "https"),
        };

        var loginPage = await loginClient.GetAsync("/login", ct);
        loginPage.EnsureSuccessStatusCode();

        var html = await loginPage.Content.ReadAsStringAsync(ct);
        var match = FormActionRegex.Match(html);
        if (!match.Success)
        {
            throw new InvalidOperationException(
                "Could not locate Keycloak login form (id=kc-form-login). " +
                "Realm theme or Keycloak version may have changed.");
        }
        var actionUrl = HttpUtility.HtmlDecode(match.Groups["action"].Value);

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = username,
            ["password"] = password,
        });
        var submitResponse = await loginClient.PostAsync(actionUrl, form, ct);
        submitResponse.EnsureSuccessStatusCode();

        var testHandler = new HttpClientHandler
        {
            CookieContainer = cookies,
            AllowAutoRedirect = false,
            UseCookies = true,
        };
        var testClient = new HttpClient(testHandler)
        {
            BaseAddress = app.GetEndpoint("api", "https"),
        };
        return new AuthenticatedSession(testClient, cookies);
    }
}
```

- [ ] **Step 2: Build the test project**

```bash
dotnet build tests/IntegrationTests/IntegrationTests.csproj
```

Expected: Build succeeded.

---

## Task 15: Me and catalog integration tests

**Files:**
- Create: `tests/IntegrationTests/MeEndpointTests.cs`
- Create: `tests/IntegrationTests/PermissionCatalogTests.cs`

- [ ] **Step 1: Create `tests/IntegrationTests/MeEndpointTests.cs`**

```csharp
using System.Net;
using System.Net.Http.Json;
using Aspire.Hosting.Testing;
using IntegrationTests.Supports;
using SluiceBase.Core.Permissions;

namespace IntegrationTests;

[Collection("Aspire")]
public class MeEndpointTests(SluiceBaseStackFactory factory)
{
    [Fact]
    public async Task Me_Anonymous_Returns401()
    {
        using var client = factory.InitialisedApp.CreateHttpClient("api", "https");

        var response = await client.GetAsync("/api/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_AliceBootstrapAdmin_ReturnsPermissionManage()
    {
        var helper = new KeycloakLoginHelper(factory.InitialisedApp);
        using var session = await helper.SignInAsync(
            "alice", "dev", TestContext.Current.CancellationToken);

        var response = await session.Client.GetAsync(
            "/api/me", TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<MeBody>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.False(string.IsNullOrEmpty(body.Sub));
        Assert.Equal("alice@example.com", body.Email);
        Assert.Contains(Permissions.PermissionManage, body.Permissions);
    }

    [Fact]
    public async Task Me_Bob_ReturnsEmptyPermissions()
    {
        var helper = new KeycloakLoginHelper(factory.InitialisedApp);
        using var session = await helper.SignInAsync(
            "bob", "dev", TestContext.Current.CancellationToken);

        var response = await session.Client.GetAsync(
            "/api/me", TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<MeBody>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.Equal("bob@example.com", body.Email);
        Assert.Empty(body.Permissions);
    }

    private sealed record MeBody(
        string Id, string Sub, string Email, string? Name, string[] Permissions);
}
```

> **Prerequisite:** `alice` and `bob` must exist in the Keycloak realm with password `dev`. Check the Keycloak realm seed file in `src/AppHost/` to confirm these users exist.

- [ ] **Step 2: Create `tests/IntegrationTests/PermissionCatalogTests.cs`**

```csharp
using System.Net;
using System.Net.Http.Json;
using IntegrationTests.Supports;
using SluiceBase.Core.Permissions;

namespace IntegrationTests;

[Collection("Aspire")]
public class PermissionCatalogTests(SluiceBaseStackFactory factory)
{
    [Fact]
    public async Task PermissionCatalog_Anonymous_Returns401()
    {
        using var client = factory.InitialisedApp.CreateHttpClient("api", "https");

        var response = await client.GetAsync(
            "/api/permission/catalog", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PermissionCatalog_Authenticated_ReturnsAllSixPermissions()
    {
        var helper = new KeycloakLoginHelper(factory.InitialisedApp);
        using var session = await helper.SignInAsync(
            "alice", "dev", TestContext.Current.CancellationToken);

        var response = await session.Client.GetAsync(
            "/api/permission/catalog", TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<CatalogBody>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.Equal(6, body.Permissions.Length);
        Assert.Contains(Permissions.PermissionManage, body.Permissions);
        Assert.Contains(Permissions.ServerManage, body.Permissions);
        Assert.Contains(Permissions.QueryExecute, body.Permissions);
        Assert.Contains(Permissions.UpdateSubmit, body.Permissions);
        Assert.Contains(Permissions.UpdateApprove, body.Permissions);
        Assert.Contains(Permissions.UpdateExecute, body.Permissions);
    }

    private sealed record CatalogBody(string[] Permissions);
}
```

- [ ] **Step 3: Check Keycloak realm seed for alice and bob**

Open the Keycloak realm seed file (look in `src/AppHost/` for a file named `realm-export.json` or similar) and confirm that users `alice` (email `alice@example.com`, password `dev`) and `bob` (email `bob@example.com`, password `dev`) exist. If not, add them to the seed file.

- [ ] **Step 4: Run Me and catalog tests**

```bash
dotnet test tests/IntegrationTests/IntegrationTests.csproj \
  --filter "FullyQualifiedName~MeEndpointTests|FullyQualifiedName~PermissionCatalogTests" \
  --logger "console;verbosity=normal"
```

Expected: All tests pass. If `alice` or `bob` credentials fail, check the Keycloak seed.

---

## Task 16: Admin grant/revoke integration tests

**Files:**
- Create: `tests/IntegrationTests/AdminPermissionsTests.cs`

These tests exercise the full admin CRUD surface: list users, grant, revoke, idempotency, validation, authorization, and bootstrap recovery.

- [ ] **Step 1: Create `tests/IntegrationTests/AdminPermissionsTests.cs`**

```csharp
using System.Net;
using System.Net.Http.Json;
using IntegrationTests.Supports;
using SluiceBase.Core.Permissions;

namespace IntegrationTests;

[Collection("Aspire")]
public class AdminPermissionsTests(SluiceBaseStackFactory factory)
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private KeycloakLoginHelper LoginHelper => new(factory.InitialisedApp);

    private static async Task<string> GetXsrfAsync(
        AuthenticatedSession session, CancellationToken ct) =>
        await session.FetchXsrfTokenAsync(ct);

    private static HttpRequestMessage MutationRequest(
        HttpMethod method, string url, string xsrf, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Add("X-XSRF-TOKEN", xsrf);
        if (body is not null)
            req.Content = JsonContent.Create(body);
        return req;
    }

    // ── anonymous / unauthorized ──────────────────────────────────────────────

    [Fact]
    public async Task ListUsers_Anonymous_Returns401()
    {
        using var client = factory.InitialisedApp.CreateHttpClient("api", "https");

        var response = await client.GetAsync(
            "/api/admin/user", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ListUsers_Bob_Returns403()
    {
        using var session = await LoginHelper.SignInAsync(
            "bob", "dev", TestContext.Current.CancellationToken);

        var response = await session.Client.GetAsync(
            "/api/admin/user", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── list users ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListUsers_Alice_ReturnsAtLeastAliceRow()
    {
        using var session = await LoginHelper.SignInAsync(
            "alice", "dev", TestContext.Current.CancellationToken);

        var response = await session.Client.GetAsync(
            "/api/admin/user", TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ListBody>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.Contains(body.Users, u => u.Email == "alice@example.com");
    }

    // ── grant permission ──────────────────────────────────────────────────────

    [Fact]
    public async Task GrantPermission_HappyPath_Returns201AndPersists()
    {
        var ct = TestContext.Current.CancellationToken;

        // Bob logs in to ensure a user row exists
        using var bobSession = await LoginHelper.SignInAsync("bob", "dev", ct);
        await bobSession.Client.GetAsync("/api/me", ct);

        // Alice grants bob query:execute
        using var aliceSession = await LoginHelper.SignInAsync("alice", "dev", ct);
        var xsrf = await GetXsrfAsync(aliceSession, ct);

        var users = await aliceSession.Client.GetFromJsonAsync<ListBody>(
            "/api/admin/user", ct);
        var bob = Assert.Single(users!.Users, u => u.Email == "bob@example.com");

        using var grantReq = MutationRequest(
            HttpMethod.Post,
            $"/api/admin/user/{bob.Id}/permission",
            xsrf,
            new { permission = Permissions.QueryExecute });
        var grantResp = await aliceSession.Client.SendAsync(grantReq, ct);

        Assert.Equal(HttpStatusCode.Created, grantResp.StatusCode);

        // Verify bob now holds the permission
        using var bobSession2 = await LoginHelper.SignInAsync("bob", "dev", ct);
        var meResp = await bobSession2.Client.GetFromJsonAsync<MeBody>(
            "/api/me", ct);
        Assert.Contains(Permissions.QueryExecute, meResp!.Permissions);
    }

    [Fact]
    public async Task GrantPermission_Idempotent_Returns200OnDuplicate()
    {
        var ct = TestContext.Current.CancellationToken;
        using var aliceSession = await LoginHelper.SignInAsync("alice", "dev", ct);
        var xsrf = await GetXsrfAsync(aliceSession, ct);

        var users = await aliceSession.Client.GetFromJsonAsync<ListBody>(
            "/api/admin/user", ct);
        var alice = Assert.Single(users!.Users, u => u.Email == "alice@example.com");

        // Grant alice server:manage twice
        using var req1 = MutationRequest(
            HttpMethod.Post,
            $"/api/admin/user/{alice.Id}/permission",
            xsrf,
            new { permission = Permissions.ServerManage });
        var resp1 = await aliceSession.Client.SendAsync(req1, ct);
        Assert.True(resp1.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK);

        using var req2 = MutationRequest(
            HttpMethod.Post,
            $"/api/admin/user/{alice.Id}/permission",
            xsrf,
            new { permission = Permissions.ServerManage });
        var resp2 = await aliceSession.Client.SendAsync(req2, ct);
        Assert.Equal(HttpStatusCode.OK, resp2.StatusCode);
    }

    [Fact]
    public async Task GrantPermission_UnknownPermission_Returns400()
    {
        var ct = TestContext.Current.CancellationToken;
        using var aliceSession = await LoginHelper.SignInAsync("alice", "dev", ct);
        var xsrf = await GetXsrfAsync(aliceSession, ct);

        var users = await aliceSession.Client.GetFromJsonAsync<ListBody>(
            "/api/admin/user", ct);
        var alice = Assert.Single(users!.Users, u => u.Email == "alice@example.com");

        using var req = MutationRequest(
            HttpMethod.Post,
            $"/api/admin/user/{alice.Id}/permission",
            xsrf,
            new { permission = "not:real" });
        var response = await aliceSession.Client.SendAsync(req, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── revoke permission ─────────────────────────────────────────────────────

    [Fact]
    public async Task RevokePermission_HappyPath_Returns204AndRemovesGrant()
    {
        var ct = TestContext.Current.CancellationToken;
        using var aliceSession = await LoginHelper.SignInAsync("alice", "dev", ct);
        var xsrf = await GetXsrfAsync(aliceSession, ct);

        var users = await aliceSession.Client.GetFromJsonAsync<ListBody>(
            "/api/admin/user", ct);
        var alice = Assert.Single(users!.Users, u => u.Email == "alice@example.com");

        // First grant update:submit
        using var grantReq = MutationRequest(
            HttpMethod.Post,
            $"/api/admin/user/{alice.Id}/permission",
            xsrf,
            new { permission = Permissions.UpdateSubmit });
        await aliceSession.Client.SendAsync(grantReq, ct);

        // Now revoke it
        using var revokeReq = MutationRequest(
            HttpMethod.Delete,
            $"/api/admin/user/{alice.Id}/permission/{Permissions.UpdateSubmit}",
            xsrf);
        var revokeResp = await aliceSession.Client.SendAsync(revokeReq, ct);
        Assert.Equal(HttpStatusCode.NoContent, revokeResp.StatusCode);

        // Verify removed
        using var aliceSession2 = await LoginHelper.SignInAsync("alice", "dev", ct);
        var me = await aliceSession2.Client.GetFromJsonAsync<MeBody>(
            "/api/me", ct);
        Assert.DoesNotContain(Permissions.UpdateSubmit, me!.Permissions);
    }

    [Fact]
    public async Task RevokePermission_Idempotent_Returns204WhenMissing()
    {
        var ct = TestContext.Current.CancellationToken;
        using var aliceSession = await LoginHelper.SignInAsync("alice", "dev", ct);
        var xsrf = await GetXsrfAsync(aliceSession, ct);

        var users = await aliceSession.Client.GetFromJsonAsync<ListBody>(
            "/api/admin/user", ct);
        var alice = Assert.Single(users!.Users, u => u.Email == "alice@example.com");

        // Revoke a permission alice never had
        using var req = MutationRequest(
            HttpMethod.Delete,
            $"/api/admin/user/{alice.Id}/permission/{Permissions.UpdateApprove}",
            xsrf);
        var response = await aliceSession.Client.SendAsync(req, ct);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // ── bootstrap recovery ────────────────────────────────────────────────────

    [Fact]
    public async Task SelfRevoke_AliceRevokesOwnPermissionManage_BootstrapRestoresOnNextLogin()
    {
        var ct = TestContext.Current.CancellationToken;
        using var aliceSession = await LoginHelper.SignInAsync("alice", "dev", ct);
        var xsrf = await GetXsrfAsync(aliceSession, ct);

        var users = await aliceSession.Client.GetFromJsonAsync<ListBody>(
            "/api/admin/user", ct);
        var alice = Assert.Single(users!.Users, u => u.Email == "alice@example.com");

        // Revoke alice's own permission:manage
        using var revokeReq = MutationRequest(
            HttpMethod.Delete,
            $"/api/admin/user/{alice.Id}/permission/{Permissions.PermissionManage}",
            xsrf);
        var revokeResp = await aliceSession.Client.SendAsync(revokeReq, ct);
        Assert.Equal(HttpStatusCode.NoContent, revokeResp.StatusCode);

        // Next request on the same session should see no permission:manage
        var meAfterRevoke = await aliceSession.Client.GetFromJsonAsync<MeBody>(
            "/api/me", ct);
        Assert.DoesNotContain(Permissions.PermissionManage, meAfterRevoke!.Permissions);

        // New login: bootstrap re-grants permission:manage
        using var aliceSession2 = await LoginHelper.SignInAsync("alice", "dev", ct);
        var meAfterLogin = await aliceSession2.Client.GetFromJsonAsync<MeBody>(
            "/api/me", ct);
        Assert.Contains(Permissions.PermissionManage, meAfterLogin!.Permissions);
    }

    // ── response record types ─────────────────────────────────────────────────

    private sealed record MeBody(
        string Id, string Sub, string Email, string? Name, string[] Permissions);
    private sealed record UserRow(string Id, string Email, string[] Permissions);
    private sealed record ListBody(UserRow[] Users);
}
```

- [ ] **Step 2: Run admin tests**

```bash
dotnet test tests/IntegrationTests/IntegrationTests.csproj \
  --filter "FullyQualifiedName~AdminPermissionsTests" \
  --logger "console;verbosity=normal"
```

Expected: All tests pass. If XSRF cookie is missing, verify `Program.cs` sets `o.Cookie.Name = "XSRF-TOKEN"` in `AddAntiforgery`.

- [ ] **Step 3: Run full dotnet test suite**

```bash
dotnet test SluiceBase.slnx
```

Expected: All tests pass (B3 tests + new permissions tests).

- [ ] **Step 4: Commit**

```bash
git add tests/
git commit -m "test: add integration tests for permissions — me, catalog, admin grant/revoke"
```

---

## Task 17: Playwright E2E — alice grants query:execute to bob

**Files:**
- Create: `src/frontend/e2e/admin-permission.spec.ts`

- [ ] **Step 1: Create `src/frontend/e2e/admin-permission.spec.ts`**

```ts
import { expect, test } from "@playwright/test";

test.describe("Permission admin", () => {
  test("alice grants query:execute to bob", async ({ page }) => {
    // 1. Bob logs in to ensure a user row is created
    await page.goto("/");
    await expect(page).toHaveURL(/login-actions\/authenticate/, { timeout: 15_000 });
    await page.getByLabel(/username/i).fill("bob");
    await page.locator('[id="password"]').fill("dev");
    await page.getByRole("button", { name: /sign in/i }).click();
    await expect(page).toHaveURL(/^http:\/\/localhost:5173\/?$/, { timeout: 15_000 });

    // Sign bob out
    await page.getByTestId("user-menu").click();
    await page.getByRole("menuitem", { name: /log out/i }).click();

    // 2. Alice logs in
    await page.goto("/");
    await expect(page).toHaveURL(/login-actions\/authenticate/, { timeout: 15_000 });
    await page.getByLabel(/username/i).fill("alice");
    await page.locator('[id="password"]').fill("dev");
    await page.getByRole("button", { name: /sign in/i }).click();
    await expect(page).toHaveURL(/^http:\/\/localhost:5173\/?$/, { timeout: 15_000 });

    // 3. Alice sees the Permission nav link
    await expect(page.getByRole("link", { name: "Permission" })).toBeVisible();

    // 4. Navigate to /admin/permission
    await page.getByRole("link", { name: "Permission" }).click();
    await expect(page).toHaveURL("/admin/permission");
    await expect(page.getByRole("heading", { level: 2 })).toContainText(
      "Permission management",
    );

    // 5. Find bob's row and toggle query:execute on
    const bobRow = page.getByRole("row").filter({ hasText: "bob@example.com" });
    await expect(bobRow).toBeVisible();

    // Find the query:execute switch (aria-label matches the full label from PERMISSION_LABELS)
    const querySwitch = bobRow.getByRole("switch", { name: /run read queries/i });
    await expect(querySwitch).not.toBeChecked();
    await querySwitch.click();

    // 6. Expect success toast and switch to be checked after refetch
    await expect(page.getByText("Permission granted")).toBeVisible({ timeout: 5_000 });
    await expect(querySwitch).toBeChecked({ timeout: 5_000 });

    // 7. Alice logs out
    await page.getByTestId("user-menu").click();
    await page.getByRole("menuitem", { name: /log out/i }).click();

    // 8. Bob logs back in
    await page.goto("/");
    await expect(page).toHaveURL(/login-actions\/authenticate/, { timeout: 15_000 });
    await page.getByLabel(/username/i).fill("bob");
    await page.locator('[id="password"]').fill("dev");
    await page.getByRole("button", { name: /sign in/i }).click();
    await expect(page).toHaveURL(/^http:\/\/localhost:5173\/?$/, { timeout: 15_000 });

    // 9. Bob's /api/me includes query:execute
    const [meResponse] = await Promise.all([
      page.waitForResponse((r) => r.url().includes("/api/me") && r.status() === 200),
    ]);
    const meBody = await meResponse.json() as { permissions: string[] };
    expect(meBody.permissions).toContain("query:execute");

    // 10. Bob does NOT see the Permission link
    await expect(page.getByRole("link", { name: "Permission" })).not.toBeVisible();

    // 11. Bob navigating directly to /admin/permission is redirected to /
    await page.goto("/admin/permission");
    await expect(page).toHaveURL(/^http:\/\/localhost:5173\/?$/, { timeout: 5_000 });
  });
});
```

- [ ] **Step 2: Run E2E test**

Run from `src/frontend/` with Aspire stack running:
```bash
npm run test:e2e -- admin-permission.spec.ts
```

Expected: The spec passes. If elements are not found by the locators (e.g., `getByRole("switch", { name: /run read queries/i })`), check that `aria-label` on the `<Switch>` in the page component matches and adjust the locator.

- [ ] **Step 3: Run full frontend test suite**

```bash
npm run test
npm run test:e2e
```

Expected: All tests pass.

- [ ] **Step 4: Commit**

```bash
git add src/frontend/e2e/
git commit -m "test(e2e): add admin-permission Playwright spec for grant flow"
```

---

## Self-review

### 1. Spec coverage check

| Spec requirement | Covered |
|---|---|
| User table populated from Keycloak claims at login | ✅ Task 5 (UserLoginRecorder, OnTokenValidated) |
| UserPermission table with one row per (user, permission) | ✅ Tasks 2–4 |
| Six fixed permission strings | ✅ Task 2 (Permissions.cs) |
| Bootstrap admin via config | ✅ Task 5 (BootstrapAdminOptions, UserLoginRecorder) |
| ASP.NET authorization handler (DB per request, request-scoped) | ✅ Task 6–7 |
| /api/me shape with permissions[] | ✅ Task 8 |
| /api/permission/catalog | ✅ Task 8 |
| /api/admin/user list, grant, revoke | ✅ Task 8 |
| Mantine admin page /admin/permission | ✅ Task 13 |
| useHasPermission + navbar gating + route guard | ✅ Tasks 11–13 |
| KeycloakLoginHelper for integration tests | ✅ Task 14 |
| Integration tests (me, catalog, admin CRUD) | ✅ Tasks 15–16 |
| Vitest unit tests for useHasPermission | ✅ Tasks 10–11 |
| Playwright E2E grant flow | ✅ Task 17 |
| Success criteria 1–7 from spec | ✅ All covered by tests |

### 2. Placeholder scan

None found. All code blocks are complete.

### 3. Type consistency check

- `Permissions.PermissionManage` — used in `UserLoginRecorder`, `AuthSetup`, `PermissionsEndpoints`, `AdminPermissionsTests`. Consistent.
- `UserId` — defined in Core, used in `User`, `UserPermission`, `UserConfiguration`, `PermissionsEndpoints`, `AuthEndpoints` (`MeResponse`). Consistent.
- `MeResponse.Permissions` (C#: `string[]`, TS: `Array<string>`) — consistent.
- `MeResponse.Id` (C#: `UserId`, TS: `string`) — `UserId` is Vogen-typed Guid, serializes as UUID string; TS `string` matches.
- `UserSummary.id` (TS) vs `UserSummaryResponse.Id` (C#) — JSON camel-case matches. Consistent.
- `useHasPermission` — defined in `permissions.ts`, imported in `_authed.tsx` and `permission.tsx`. Consistent.
- `AuthenticatedSession.FetchXsrfTokenAsync` — called in all admin integration tests. Consistent.

### Deviation from spec

**`PermissionAuthorizationHandler` is registered as `AddScoped`, not `AddSingleton`.**
The spec lists `AddSingleton`, but `PermissionAuthorizationHandler` takes `ICurrentUserAccessor` as a constructor dependency and `ICurrentUserAccessor` is scoped. Registering a singleton that depends on a scoped service throws at runtime in ASP.NET Core's scope validation. `AddScoped` is correct here — ASP.NET Core's authorization middleware resolves handlers from the request's `IServiceProvider` on every request.

---

**Plan complete and saved to `docs/superpowers/plans/2026-05-05-permissions.md`. Two execution options:**

**1. Subagent-Driven (recommended)** — Fresh subagent per task, review between tasks, fast iteration.

**2. Inline Execution** — Execute tasks in this session using executing-plans, batch execution with checkpoints.

**Which approach?**
