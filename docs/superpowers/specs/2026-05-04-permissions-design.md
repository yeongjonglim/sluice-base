# SluiceBase Permissions — Design

**Date:** 2026-05-04
**Status:** Proposed
**Sub-project:** 2 of 6 (Permissions)
**Predecessor:** 1. Foundations (`2026-05-03-foundations-design.md`)
**Successor sub-projects:** 3. Server registry, 4. Schema browser, 5. Query workspace, 6. Approval workflow.

## 1. Purpose & scope

Foundations established BFF auth (anyone with a Keycloak account is "logged in") but no notion of *what they can do*. Permissions introduces the v1 authorization model: per-user grants stored in SluiceBase's metadata DB, enforced by ASP.NET policies, and managed via an in-app admin UI. Every later sub-project (server registry, query workspace, approval workflow) builds on this — `.RequireAuthorization(Permissions.ServersManage)` etc.

### In scope

- A cached `User` table populated from Keycloak claims at login.
- A `UserPermission` table holding the user's grants (one row per (user, permission) pair).
- A fixed catalog of six permission strings.
- Bootstrap admin via config — `Permissions:Bootstrap:Admins` email list grants `permission:manage` on login.
- ASP.NET authorization handler that checks permissions live against the DB on each authorization decision.
- `/api/me` shape extended with `permissions: string[]`.
- `/api/permission/catalog` endpoint returning the catalog.
- `/api/admin/user` listing and grant/revoke endpoints, gated by `permission:manage`.
- Mantine admin page at `/admin/permission`.
- Frontend permission helpers (`useHasPermission`, navbar link gating, route guard).
- xUnit integration tests via the existing `SluiceBaseStackFactory`, authenticating through the real Keycloak code-flow via a `KeycloakLoginHelper` test-supports class.
- One Playwright E2E covering the grant flow end-to-end.

### Out of scope (deferred)

- Per-resource (per-server) scoping of permissions — added in sub-project 3 when the server registry exists.
- Audit log of permission changes (the `granted_at` / `granted_by_id` columns on `user_permissions` provide a basic audit trail; a separate audit table is later work).
- Permission delegation / time-bounded grants.
- Group-based permissions (Keycloak groups, etc.) — single-user grants only for v1.
- A CLI tool to grant permissions (config-based bootstrap covers the only non-UI need).
- Test-only auth surface in the api (auth in tests goes through real Keycloak).

### Success criteria

After implementation, with Aspire running:

1. Signing in as `alice@example.com` (configured as a bootstrap admin in `appsettings.Development.json`) results in `/api/me` returning `permissions: ["permission:manage"]`.
2. The Mantine app shell shows a `Permissions` link in the navbar for alice but not for bob.
3. Alice can navigate to `/admin/permission`, see a table with at least her own row, and toggle permissions on bob's row (assuming bob has logged in at least once).
4. Toggling `query:execute` for bob's row persists the grant in `metadata-pg` and `/api/me` for bob (next session) reflects the new permission.
5. Bob navigating directly to `/admin/permission` is redirected to `/` (UI guard) and any direct API call to `/api/admin/user` returns 403 (server guard).
6. Anonymous calls to `/api/admin/user` return 401.
7. Revoking alice's own `permission:manage` succeeds, but on her next login the bootstrap re-grants it.

## 2. Architectural decisions (locked-in via brainstorm)

| # | Decision | Rationale |
|---|----------|-----------|
| 1 | Source of truth for permissions: SluiceBase's metadata DB, not Keycloak realm roles | Keeps SluiceBase deployable against any OIDC IdP without depending on the IdP's authorization model; admin UI lives in-app; permissions are part of the product domain. |
| 2 | Permission shape: fine-grained strings (no roles concept) | Maps directly to ASP.NET policies; idiomatic; per-resource scoping is a future extension without a model rewrite. |
| 3 | User identity: cached `User` table keyed by `Sub` (Keycloak `sub` claim, stored as `string` for IdP portability) | Stable PK independent of mutable email; admin UI works without Keycloak admin API. |
| 4 | User table fields: `sub` (string, unique index), `email`, `name`, `last_login_at` | No `preferred_username` (cut per user preference); rest is "what we need to render the admin UI." |
| 5 | Permission catalog: 6 fixed strings | Matches the v1 product surface: `permission:manage`, `server:manage`, `query:execute`, `update:submit`, `update:approve`, `update:execute`. |
| 6 | Approval/execution split: `update:approve` vs `update:execute` are separate | Approving a mutation and running it are independent capabilities (one human approves, another executes — possibly via system action). |
| 7 | Admin model: single super-admin (`permission:manage`), no granular per-area sub-admins | YAGNI for v1; sub-admin model can layer on later if real demand emerges. |
| 8 | Resource scoping: global only for v1 | Server registry doesn't exist yet (sub-project 3); per-resource scoping is a small migration when needed. |
| 9 | Bootstrap mechanism: config-driven email list, sync-on-every-login | Idempotent recovery; same config drives dev and prod; first action is to exercise the grant UI rather than skip it. |
| 10 | Permission lookup: DB query per request (cached scoped to the request), no claim-caching in the cookie | Immediate consistency on grant/revoke; one indexed query per request is negligible at our scale. |
| 11 | Vogen-typed value object IDs (`UserId`, `UserPermissionId`) | Compile-time guard against mixing identifier types; integrates cleanly with EF and System.Text.Json via Vogen. |
| 12 | `DateTimeOffset` for all timestamps | Timezone-safe; idiomatic .NET. |
| 13 | Rich domain models in `Core` (private setters, factory methods, behavior methods); EF `IEntityTypeConfiguration<T>` in `Api` | Domain models stay infra-free; persistence concerns externalized; no DTO/entity mapper layer (single class, dual purpose). |
| 14 | Default test auth: real Keycloak code-flow via a `KeycloakLoginHelper` that drives the OIDC chain programmatically | No test-only surface in the api; tests exercise the same authentication path as real users. |
| 15 | Sequencing: single PR | Scope is smaller than any Foundations slice; coherent feature is best reviewed as one. |

## 3. Schema & migration

### 3.1 Vogen-typed IDs (in `Core`)

```csharp
// SluiceBase.Core/Users/UserId.cs
[ValueObject<Guid>]
public readonly partial struct UserId;

// SluiceBase.Core/Permissions/UserPermissionId.cs
[ValueObject<Guid>]
public readonly partial struct UserPermissionId;
```

Project-level Vogen defaults (in `SluiceBase.Core/AssemblyAttributes.cs` and `SluiceBase.Api/AssemblyAttributes.cs`):

```csharp
[assembly: VogenDefaults(
    conversions: Conversions.SystemTextJson | Conversions.TypeConverter)]
```

`SystemTextJson` is needed for request/response bodies. `TypeConverter` is needed for ASP.NET route binding (`{userId}` URL segments parse into `UserId`).

### 3.2 Domain models (rich, in `Core`)

```csharp
// SluiceBase.Core/Users/User.cs
public sealed class User
{
    private readonly List<UserPermission> _permissions = [];

    private User() { }                                          // EF hydration

    private User(UserId id, string sub, string email, string? name, DateTimeOffset at)
    {
        Id = id;
        Sub = sub;
        Email = email;
        Name = name;
        LastLoginAt = at;
    }

    public UserId Id { get; private set; }
    public string Sub { get; private set; } = "";              // Keycloak `sub`; opaque IdP-stable string
    public string Email { get; private set; } = "";            // last-known from claims
    public string? Name { get; private set; }                  // last-known display name
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

```csharp
// SluiceBase.Core/Permissions/UserPermission.cs
public sealed class UserPermission
{
    private UserPermission() { }                                // EF hydration

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
    public UserId? GrantedById { get; private set; }            // NULL for bootstrap-issued grants

    public static UserPermission Grant(
        UserId userId, string permission, UserId? grantedById, DateTimeOffset at) =>
        new(UserPermissionId.From(Guid.NewGuid()), userId, permission, grantedById, at);
}
```

### 3.3 Permission catalog (in `Core`)

```csharp
// SluiceBase.Core/Permissions/Permissions.cs
public static class Permissions
{
    public const string PermissionManage = "permission:manage";
    public const string ServerManage = "server:manage";
    public const string QueryExecute = "query:execute";
    public const string UpdateSubmit = "update:submit";
    public const string UpdateApprove = "update:approve";
    public const string UpdateExecute = "update:execute";

    public static readonly IReadOnlySet<string> All =
    [
        PermissionsManage, ServersManage, QueryExecute,
        UpdateSubmit, UpdateApprove, UpdateExecute,
    ];
}
```

### 3.4 EF entity configurations (in `Api`)

```csharp
// SluiceBase.Api/Data/Configurations/UserConfiguration.cs
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

```csharp
// SluiceBase.Api/Data/Configurations/UserPermissionConfiguration.cs
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

### 3.5 `AppDbContext` additions

```csharp
public DbSet<User> Users => Set<User>();
public DbSet<UserPermission> UserPermissions => Set<UserPermission>();

protected override void OnModelCreating(ModelBuilder b)
{
    b.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    base.OnModelCreating(b);
}
```

### 3.6 Migration

One new migration, `AddUserAndPermission`. Creates `user` and `user_permission` tables with:
- Snake-case names from existing `EFCore.NamingConventions`.
- `user.sub` unique index.
- `user_permission(user_id, permission)` unique index.
- FKs on `user_id` (CASCADE delete) and `granted_by_id` (SET NULL).

Existing `data_protection_key` table from B1 is not touched.

## 4. Login hook, bootstrap, authorization plumbing

### 4.1 Bootstrap configuration

```csharp
// SluiceBase.Api/Auth/BootstrapAdminOptions.cs
public sealed class BootstrapAdminOptions
{
    public const string SectionName = "Permissions:Bootstrap";
    public IList<string> Admins { get; set; } = [];
}
```

Bound via `builder.Services.Configure<BootstrapAdminOptions>(builder.Configuration.GetSection(BootstrapAdminOptions.SectionName))`.

`appsettings.json` (prod default): empty array.
`appsettings.Development.json`: `["alice@example.com"]`.
Production deployers populate via env: `Permissions__Bootstrap__Admins__0=ops@yourorg.com`.

### 4.2 Login recorder service

```csharp
// SluiceBase.Api/Auth/UserLoginRecorder.cs
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

        var emailMatches = bootstrap.Value.Admins.Any(b =>
            string.Equals(b, email, StringComparison.OrdinalIgnoreCase));

        if (emailMatches && !user.HasPermission(Permissions.PermissionsManage))
        {
            db.UserPermissions.Add(UserPermission.Grant(
                user.Id, Permissions.PermissionsManage, grantedById: null, at));
        }

        await db.SaveChangesAsync(ct);
        return user;
    }
}
```

Registered scoped: `services.AddScoped<IUserLoginRecorder, UserLoginRecorder>()`.

### 4.3 OIDC `OnTokenValidated` wiring

In `AuthSetup.cs`'s `.AddOpenIdConnect(options => { … })`:

```csharp
options.Events.OnTokenValidated = async ctx =>
{
    var services = ctx.HttpContext.RequestServices;
    var recorder = services.GetRequiredService<IUserLoginRecorder>();
    var clock = services.GetRequiredService<TimeProvider>();

    var sub = ctx.Principal!.FindFirstValue("sub")!;
    var email = ctx.Principal.FindFirstValue("email") ?? "";
    var name = ctx.Principal.FindFirstValue("name");

    await recorder.RecordLoginAsync(
        sub, email, name, clock.GetUtcNow(),
        ctx.HttpContext.RequestAborted);
};
```

Runs once per OIDC code-flow login. The only place that writes to `user` and the only place that issues bootstrap grants.

### 4.4 Authorization handler & current-user accessor

```csharp
// SluiceBase.Api/Auth/PermissionRequirement.cs
internal sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}
```

```csharp
// SluiceBase.Api/Auth/CurrentUserAccessor.cs
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

```csharp
// SluiceBase.Api/Auth/PermissionAuthorizationHandler.cs
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

`ICurrentUserAccessor` is scoped → one instance per request → at most one DB query per request even if six authorization checks run.

### 4.5 Policy registration

In `AuthSetup.cs`:

```csharp
services.AddAuthorization(options =>
{
    foreach (var permission in Permissions.All)
    {
        options.AddPolicy(permission,
            policy => policy.Requirements.Add(new PermissionRequirement(permission)));
    }
});

services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
services.AddScoped<ICurrentUserAccessor, CurrentUserAccessor>();
services.AddHttpContextAccessor();
```

Endpoints gate themselves with the catalog constants: `.RequireAuthorization(Permissions.ServersManage)`.

### 4.6 Why DB-per-request, not claim caching

- Immediate consistency. Grant/revoke takes effect on the user's *very next request*. No cookie re-issue, no TTL window.
- Simpler. Cookie size is unaffected; no `OnValidatePrincipal` revalidation logic.
- Cheap. ~1ms indexed query per authorized request, deduplicated to one DB hit per request via the scoped cache.

If perf becomes an issue at significant scale, swapping `CurrentUserAccessor` to a short-TTL `IMemoryCache` is a one-class change.

## 5. `/api/me`, permission catalog, gating

### 5.1 `/api/me` shape change

In `AuthEndpoints.cs`:

```csharp
app.MapGet("/api/me", async (ICurrentUserAccessor currentUser, CancellationToken ct) =>
{
    var user = await currentUser.GetAsync(ct);
    if (user is null) return Results.Unauthorized();

    return Results.Ok(new MeResponse(
        Sub: user.Sub,
        Email: user.Email,
        Name: user.Name,
        Permissions: user.Permissions.Select(p => p.Permission).ToArray()));
})
.WithName("Me")
.RequireAuthorization();

internal sealed record MeResponse(
    string Sub,
    string Email,
    string? Name,
    string[] Permissions);
```

Drops `roles` (always-empty since the realm seeds none) and `preferredUsername` (chained to the User-table-no-preferred-username decision). Adds `permissions: string[]` from the DB.

A real `record` rather than the prior anonymous object so OpenAPI codegen emits a single canonical `MeResponse` type for the frontend.

### 5.2 Permission catalog endpoint

New file `Endpoints/PermissionsEndpoints.cs`:

```csharp
internal static class PermissionsEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/permission/catalog", () =>
            Results.Ok(new PermissionCatalogResponse(Permissions.All.ToArray())))
            .WithName("PermissionCatalog")
            .RequireAuthorization();

        var admin = app.MapGroup("/api/admin")
            .RequireAuthorization(Permissions.PermissionsManage);

        admin.MapGet("/user", ListUsers).WithName("ListUsers");
        admin.MapPost("/user/{userId}/permission", GrantPermission)
            .WithName("GrantPermission");
        admin.MapDelete("/user/{userId}/permission/{permission}", RevokePermission)
            .WithName("RevokePermission");
    }

    // Handler implementations: see §6.
}

internal sealed record PermissionCatalogResponse(string[] Permissions);
```

`EndpointMapper.cs` extended:

```csharp
public static IEndpointRouteBuilder MapAllEndpoints(this IEndpointRouteBuilder app)
{
    AuthEndpoints.Map(app);
    HealthEndpoints.Map(app);
    PermissionsEndpoints.Map(app);
    return app;
}
```

### 5.3 Endpoint gating summary

| Endpoint | Auth | Notes |
|---|---|---|
| `/api/health` | Anon | unchanged |
| `/api/health/authed` | Authenticated | unchanged |
| `/api/me` | Authenticated | shape changed |
| `/api/antiforgery-token` | Authenticated | unchanged |
| `/login`, `/logout` | Anon | unchanged |
| `/api/permission/catalog` | Authenticated | new |
| `/api/admin/user` | `permission:manage` | new |
| `/api/admin/user/{userId}/permission` (POST) | `permission:manage` | new |
| `/api/admin/user/{userId}/permission/{permission}` (DELETE) | `permission:manage` | new |

## 6. Grant/revoke API handlers

```csharp
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

    if (user.HasPermission(req.Permission)) return Results.Ok();   // idempotent

    var actor = await currentUser.GetAsync(ct);
    db.UserPermissions.Add(UserPermission.Grant(
        user.Id, req.Permission, actor?.Id, clock.GetUtcNow()));
    await db.SaveChangesAsync(ct);

    return Results.Created(
        $"/api/admin/users/{userId}/permissions/{req.Permission}", null);
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
    return Results.NoContent();   // idempotent
}

private sealed record GrantPermissionRequest(string Permission);

private sealed record UserSummaryResponse(
    UserId Id, string Sub, string Email, string? Name,
    DateTimeOffset LastLoginAt, string[] Permissions);

private sealed record ListUsersResponse(IReadOnlyList<UserSummaryResponse> Users);
```

### 6.1 Behaviour decisions

- **Idempotent grant** — granting an already-held permission returns 200 (no-op). Avoids 409 retry loops on the client.
- **Idempotent revoke** — revoking a missing permission returns 204.
- **No self-revoke guard.** The API doesn't reject self-revocation of `permission:manage`. Bootstrap config restores the grant on next login. The UI shows a confirmation modal as the safety net (§7).
- **No transitive grants.** `permission:manage` doesn't transitively imply other permissions; even the super admin must explicitly hold (or grant themselves) `server:manage` etc. Authorization handler stays a one-row lookup.
- **`GrantedById`** is sourced from `ICurrentUserAccessor`, never the request body or route. NULL is reserved for bootstrap.
- **Unknown permission** returns RFC 7807 `ValidationProblemDetails` (better OpenAPI codegen than `BadRequest(string)`).
- **Anti-forgery** — POST/DELETE inherit B1's `app.UseAntiforgery()`. Frontend's `apiRequest` helper from B2 already sends `X-XSRF-TOKEN` on mutations.

### 6.2 `UserId` route binding

Vogen's project-wide `Conversions.TypeConverter` (set in `AssemblyAttributes.cs`, §3.1) lets ASP.NET parse `{userId}` URL segments into `UserId` directly. JSON conversion is via `Conversions.SystemTextJson` for request/response bodies.

## 7. Admin UI

### 7.1 Route & permission gating

New route at `src/frontend/src/routes/_authed/admin/permission.tsx` → URL `/admin/permission`. Uses TanStack Router file-based routing under the existing `_authed` Mantine AppShell layout.

```tsx
export const Route = createFileRoute("/_authed/admin/permission")({
  beforeLoad: ({ context }) => {
    const me = context.queryClient.getQueryData(meQueryOptions.queryKey);
    if (!me?.permissions.includes("permission:manage")) {
      throw redirect({ to: "/" });
    }
  },
  component: PermissionsAdminPage,
});
```

The check is mirrored server-side (each admin endpoint requires `permission:manage`); the client-side check is UX, not security.

### 7.2 Frontend permission helpers

```ts
// src/frontend/src/auth/permissions.ts
import { useMe } from "../api/hooks";

export function useHasPermission(permission: string): boolean {
  const me = useMe();
  return me.data?.permissions.includes(permission) ?? false;
}
```

Used by the route guard above and by `_authed.tsx` to conditionally render the navbar `Permission` link:

```tsx
const isAdmin = useHasPermission("permission:manage");
{isAdmin && (
  <NavLink
    label="Permission"
    leftSection={<IconUsersGroup size={16} />}
    component={Link}
    to="/admin/permission"
    active={location.pathname === "/admin/permission"}
  />
)}
```

### 7.3 Page layout

- `Title order={2}` "Permission management"
- `TextInput` search filter (client-side, matches against email + name)
- Mantine `Table` with sticky header. Columns:
  1. **User** — email + name (subtitle); `(you)` chip if the row's `id` matches `me.data.id`.
  2. **Last login** — relative time (`Intl.RelativeTimeFormat`).
  3. One column per permission in the catalog, each with a `Switch`. Permission strings rendered via a small label dictionary; unknown strings render as the raw permission string.

```ts
const PERMISSION_LABELS: Record<string, { short: string; full: string }> = {
  "permission:manage":  { short: "Permission", full: "Manage permissions" },
  "server:manage":      { short: "Server",     full: "Manage servers" },
  "query:execute":      { short: "Query",      full: "Run read queries" },
  "update:submit":      { short: "Submit",     full: "Submit update requests" },
  "update:approve":     { short: "Approve",    full: "Approve update requests" },
  "update:execute":     { short: "Execute",    full: "Execute approved updates" },
};
```

### 7.4 Toggle behaviour

```tsx
const handleToggle = async (
  user: UserSummary,
  permission: string,
  nextValue: boolean,
) => {
  if (
    !nextValue &&
    permission === "permission:manage" &&
    user.id === me.data!.id
  ) {
    const confirmed = await openConfirmModal({
      title: "Revoke your own admin permission?",
      children: (
        <Text size="sm">
          You will lose access to this page. The bootstrap config will re-grant
          permissions:manage on your next login if your email is listed there.
        </Text>
      ),
      labels: { confirm: "Revoke", cancel: "Cancel" },
      confirmProps: { color: "red" },
    });
    if (!confirmed) return;
  }

  if (nextValue) {
    await grant.mutateAsync({ userId: user.id, permission });
  } else {
    await revoke.mutateAsync({ userId: user.id, permission });
  }
};
```

The switch is `disabled` while the row's mutation is in-flight. After success, the `["admin", "user"]` query invalidates and the table re-fetches.

### 7.5 TanStack Query hooks (in `api/hooks.ts`)

```ts
export function useUsers() {
  return useQuery({
    queryKey: ["admin", "user"] as const,
    queryFn: () => apiRequest<ListUsersResponse>("/api/admin/user"),
  });
}

export function usePermissionCatalog() {
  return useQuery({
    queryKey: ["permission", "catalog"] as const,
    queryFn: () =>
      apiRequest<PermissionCatalogResponse>("/api/permission/catalog"),
    staleTime: Infinity,
  });
}

export function useGrantPermission() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ userId, permission }: { userId: string; permission: string }) =>
      apiRequest<void>(`/api/admin/user/${userId}/permission`, {
        method: "POST",
        body: { permission },
      }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["admin", "user"] });
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
    mutationFn: ({ userId, permission }: { userId: string; permission: string }) =>
      apiRequest<void>(
        `/api/admin/user/${userId}/permission/${permission}`,
        { method: "DELETE" },
      ),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["admin", "user"] });
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

A small `formatApiError(err)` helper extracts validation-problem messages from `err.body` for friendly toast text.

### 7.6 `/api/me` ripple — frontend updates outside the new page

Two consequential edits in B2 code:

1. `src/frontend/src/api/hooks.ts` — `MeResponse` regenerated by `npm run gen:api`; hand-typed `MeResponse` interface drops `roles` and `preferredUsername`, adds `permissions`.
2. `src/frontend/src/routes/_authed.tsx` and `_authed/index.tsx` — display-name fallback compresses from `name ?? preferredUsername ?? email` to `name ?? email`.

### 7.7 Empty state

If `useUsers()` returns 0 users (only happens before anyone has logged in), the page renders a small Mantine `Card`: "No users yet. Sign in as a bootstrap admin to populate the user table." On typical OSS deployments where the operator deploys and immediately signs in, this state lasts one second.

## 8. Tests

### 8.1 Auth helper for tests

Tests authenticate via the real Keycloak code-flow — no test-only auth surface in the api. The helper drives the OIDC chain programmatically.

```csharp
// tests/IntegrationTests/Supports/KeycloakLoginHelper.cs
using System.Net;
using System.Text.RegularExpressions;
using System.Web;
using Aspire.Hosting;
using Aspire.Hosting.Testing;

namespace IntegrationTests.Supports;

public sealed class KeycloakLoginHelper(DistributedApplication app)
{
    private static readonly Regex FormActionRegex = new(
        """<form[^>]+id="kc-form-login"[^>]+action="(?<action>[^"]+)"""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public async Task<HttpClient> SignInAsync(
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
        return new HttpClient(testHandler)
        {
            BaseAddress = app.GetEndpoint("api", "https"),
        };
    }
}
```

Trade-off: the regex parses Keycloak's `kc-form-login` HTML form. `id="kc-form-login"` has been stable across Keycloak 18+ but a custom realm theme could break it. The exception message above flags this clearly.

No realm.json change needed — we use the standard authorization-code flow.

### 8.2 Backend integration test inventory

All in `tests/IntegrationTests/`, all using the existing `[Collection("Aspire")]` fixture:

| File | What it asserts |
|---|---|
| `MeEndpointTests.cs` | New shape (`sub`, `email`, `name`, `permissions[]`); alice (bootstrap) sees `permission:manage`; bob sees `[]`. |
| `PermissionCatalogTests.cs` | `/api/permission/catalog` returns the 6 catalog strings; 401 anonymous. |
| `AdminPermissionsTests.cs` | List, grant happy-path, revoke happy-path, idempotent grant (200 on duplicate), idempotent revoke (204 on missing), unknown permission → 400 validation problem, bob (no `permission:manage`) → 403, anonymous → 401, self-revoke succeeds and re-login restores `permission:manage` via bootstrap. |

`UserLoginRecorder` is exercised transitively through these tests (every `KeycloakLoginHelper.SignInAsync` triggers it).

### 8.3 Frontend Vitest additions

`src/frontend/src/auth/__tests__/permission.test.ts`:

| Test | Asserts |
|---|---|
| `useHasPermission_returnsTrueForGrantedPermission` | Hook returns `true` when permission is in the array. |
| `useHasPermission_returnsFalseForMissing` | `false` when missing. |
| `useHasPermission_returnsFalseWhenMeIsLoading` | While `useMe()` is pending, `false`. |

Tests use `@testing-library/react`'s `renderHook` with a stub `QueryClient` seeded with the relevant me data shape.

### 8.4 Playwright E2E

Extend `e2e/` with `admin-permission.spec.ts`:

One spec — "alice grants `query:execute` to bob":

1. Sign in as bob (populates a User row); sign out.
2. Sign in as alice. Expect `Permission` link in the navbar.
3. Click `Permission`. Expect the user table.
4. Find bob's row. Toggle `query:execute` on. Wait for success toast. Expect switch is checked after re-fetch.
5. Sign out. Sign in as bob.
6. Confirm via `page.waitForResponse('/api/me')` that response includes `query:execute` in `permission`.
7. Confirm the navbar does NOT show `Permission` (bob lacks `permission:manage`).

### 8.5 Out of test scope

- Direct testing of the OIDC `OnTokenValidated` callback wiring — covered transitively by login-flow integration tests.
- Stress / concurrency on grant/revoke (single-instance v1).
- Performance assertions on `CurrentUserAccessor` query.

## 9. Packages, configuration, risks, acceptance

### 9.1 New NuGet packages

On `SluiceBase.Api`:
- `Vogen` — source generator; emits the value-object struct bodies.
- `Vogen.EntityFrameworkCore` — provides `HasVogenConversion()` for value converters.

No new packages on `SluiceBase.Core` (Vogen's source generator runs in the consuming project).

No new frontend packages (Mantine + TanStack already in B2).

### 9.2 DI registration changes

```csharp
// In AuthSetup.cs (or Program.cs composition root):
services.AddScoped<IUserLoginRecorder, UserLoginRecorder>();
services.AddScoped<ICurrentUserAccessor, CurrentUserAccessor>();
services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
services.AddHttpContextAccessor();
services.Configure<BootstrapAdminOptions>(
    config.GetSection(BootstrapAdminOptions.SectionName));

services.AddAuthorization(options =>
{
    foreach (var permission in Permissions.All)
    {
        options.AddPolicy(permission,
            policy => policy.Requirements.Add(new PermissionRequirement(permission)));
    }
});
```

### 9.3 Risks & open questions

- **Bootstrap recovery** — if no bootstrap email matches any logged-in user, no one can grant permissions. Recovery: edit appsettings/env, restart, sign in. Documented in `docs/TESTING.md` and a future ops doc.
- **DataProtection key rotation** — `IDataProtectionKeyContext` already implemented in B1. New `DbSet`s don't disturb the existing `data_protection_keys` table; the migration only `CREATE TABLE`s the two new tables.
- **`CurrentUserAccessor` query cost** — one indexed query per authorized request, deduplicated to one DB hit per request via the scoped cache. Negligible at SluiceBase scale.
- **`X-XSRF-TOKEN` in tests** — integration tests covering POST/DELETE need to fetch the antiforgery token first (`GET /api/antiforgery-token`), set the cookie, and send the header. Same path the SPA uses; not test-only.
- **Vogen TypeConverter for route binding** — verified by the first integration test that POSTs to `/api/admin/user/{userId}/permission`. If `{userId}` doesn't bind, the request returns 400 — surfaces immediately.
- **`(you)` chip rendering** — relies on `useMe().data.id` matching the user-summary `id`. Both deserialize from the same JSON `UserId` string; direct `===` works.
- **Self-revoke + active session** — after revoking own `permission:manage`, the *next* request fails authorization (since `CurrentUserAccessor` re-reads from DB). The admin page may show a stale state until refresh. The confirmation modal (§7.4) is sufficient mitigation for v1; a follow-up could navigate the SPA on success.
- **Keycloak login form regex** — depends on the `kc-form-login` form structure. Stable across recent Keycloak versions; if a custom realm theme overrides the login template, the regex breaks and tests fail with a clear error pointing at the realm theme.

### 9.4 Acceptance criteria

- `dotnet build SluiceBase.slnx` clean (warnings-as-errors).
- `dotnet test SluiceBase.slnx` passes (B1 + B3 + new permissions tests).
- `npm run build` clean (TS strict + ESLint).
- `npm run test` passes (B2 + new `useHasPermission` tests).
- `aspire run` then sign in as `alice@example.com`:
  - `/api/me` returns `permissions: ["permission:manage"]`.
  - Mantine app shell shows a `Permission` link in the navbar.
  - Admin page renders alice's row with `permission:manage` switch on, all other switches off.
  - Toggling `query:execute` for bob's row (after bob has logged in once) persists and shows a success toast.
- Sign in as bob: `/api/me` returns `permissions: ["query:execute"]`. No `Permission` link. Direct nav to `/admin/permission` redirects to `/`.
- `npm run test:e2e` passes the new `admin-permission.spec.ts`.

### 9.5 References

- Foundations design: `docs/superpowers/specs/2026-05-03-foundations-design.md`.
- B3 plan: `docs/superpowers/plans/2026-05-03-foundations-b3-tests.md` (which the integration test patterns build on).
- Test fixture: `tests/IntegrationTests/Supports/SluiceBaseStackFactory.cs`.
- Vogen documentation: <https://github.com/SteveDunn/Vogen>.
