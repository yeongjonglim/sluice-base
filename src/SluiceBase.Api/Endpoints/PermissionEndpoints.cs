using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SluiceBase.Api.Auth;
using SluiceBase.Api.Data;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Servers;
using SluiceBase.Core.Users;

namespace SluiceBase.Api.Endpoints;

internal static class PermissionEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/permission/catalog",
                Ok<PermissionCatalogResponse> () => TypedResults.Ok(new PermissionCatalogResponse([.. Permissions.Global])))
            .WithName("PermissionCatalog")
            .RequireAuthorization();

        var admin = app.MapGroup("/api/admin")
            .RequireAuthorization(Permissions.PermissionManage);

        admin.MapGet("/user", ListUsers).WithName("ListUsers");
        admin.MapPost("/user/{userId}/permission", GrantPermission)
            .WithName("GrantPermission");
        admin.MapDelete("/user/{userId}/permission/{permission}", RevokePermission)
            .WithName("RevokePermission");
        admin.MapGet("/user/{userId}/effective", GetEffectiveUserPermissions)
            .WithName("GetEffectiveUserPermissions");
    }

    private static async Task<Ok<ListUsersResponse>> ListUsers(AppDbContext db, CancellationToken ct)
    {
        var users = await db.ExternalLogins
            .Include(u => u.User)
            .ThenInclude(u => u.Permissions)
            .AsNoTracking()
            .OrderBy(u => u.Email)
            .Select(u => new
            {
                u.UserId,
                u.Email,
                u.Name,
                u.LastLoginAt,
                Permissions = u.User.Permissions.Select(p => p.Permission).ToArray(),
            })
            .ToListAsync(ct);

        var allMemberships = await db.GroupMembers
            .AsNoTracking()
            .Join(db.Groups,
                m => m.GroupId,
                g => g.Id,
                (m, g) => new { m.UserId, g.Id, g.Name })
            .ToListAsync(ct);

        var membershipsByUser = allMemberships
            .GroupBy(m => m.UserId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<UserGroupMembership>)g
                    .Select(m => new UserGroupMembership(GroupId.From(m.Id.Value), m.Name))
                    .ToList());

        var response = users.Select(u => new UserSummaryResponse(
            u.UserId, u.Email, u.Name, u.LastLoginAt, u.Permissions,
            membershipsByUser.TryGetValue(u.UserId, out var groups) ? groups : []));

        return TypedResults.Ok(new ListUsersResponse([.. response]));
    }

    private static async Task<Results<ValidationProblem, NotFound, Ok, Created>> GrantPermission(
        UserId userId,
        GrantPermissionRequest req,
        AppDbContext db,
        ICurrentUserAccessor currentUser,
        TimeProvider clock,
        CancellationToken ct)
    {
        if (!Permissions.Global.Contains(req.Permission))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["permission"] = [$"'{req.Permission}' is not a known permission."]
            });
        }

        var user = await db.Users
            .Include(u => u.Permissions)
            .SingleOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
        {
            return TypedResults.NotFound();
        }

        if (user.HasPermission(req.Permission))
        {
            return TypedResults.Ok();
        }

        var actor = await currentUser.GetAsync(ct);
        db.UserPermissions.Add(UserPermissionMap.Grant(
            user.Id,
            req.Permission,
            actor?.Id,
            clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/admin/user/{userId}/permission/{req.Permission}");
    }

    private static async Task<NoContent> RevokePermission(
        UserId userId,
        string permission,
        AppDbContext db,
        CancellationToken ct)
    {
        var grant = await db.UserPermissions.SingleOrDefaultAsync(
            p => p.UserId == userId && p.Permission == permission,
            ct);
        if (grant is not null)
        {
            db.UserPermissions.Remove(grant);
            await db.SaveChangesAsync(ct);
        }

        return TypedResults.NoContent();
    }

    private static async Task<Results<NotFound, Ok<EffectiveUserPermissionsResponse>>> GetEffectiveUserPermissions(
        UserId userId,
        AppDbContext db,
        CancellationToken ct)
    {
        var userExists = await db.Users.AsNoTracking().AnyAsync(u => u.Id == userId, ct);
        if (!userExists)
        {
            return TypedResults.NotFound();
        }

        // Group memberships (materialized; reused as a local filter below).
        var groupMemberships = await db.GroupMembers
            .AsNoTracking()
            .Where(gm => gm.UserId == userId)
            .Join(db.Groups, gm => gm.GroupId, g => g.Id, (gm, g) => new { gm.GroupId, g.Name })
            .ToListAsync(ct);
        var groupIds = groupMemberships.Select(g => g.GroupId).ToList();

        // Global permissions: direct + group-inherited.
        var directGlobal = await db.UserPermissions
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => p.Permission)
            .ToListAsync(ct);

        var groupGlobal = await db.GroupPermissions
            .AsNoTracking()
            .Where(gp => groupIds.Contains(gp.GroupId))
            .Join(db.Groups, gp => gp.GroupId, g => g.Id, (gp, g) => new { gp.Permission, gp.GroupId, g.Name })
            .ToListAsync(ct);

        var globalPermissions = directGlobal
            .Concat(groupGlobal.Select(g => g.Permission))
            .Distinct()
            .Select(perm => new EffectivePermissionItem(perm, BuildSources(
                directGlobal.Contains(perm),
                groupGlobal.Where(g => g.Permission == perm).Select(g => new GroupInfo(g.GroupId, g.Name)))))
            .ToList();

        // Database-scoped roles: direct + group-inherited.
        var directDbRoles = await db.UserDatabaseRoles
            .AsNoTracking()
            .Where(r => r.UserId == userId)
            .Join(db.Databases, r => r.DatabaseId, d => d.Id, (r, d) => new { r.DatabaseId, r.Permission, d.DisplayName, d.ServerId })
            .Join(db.Servers, x => x.ServerId, s => s.Id, (x, s) => new { x.DatabaseId, x.Permission, x.DisplayName, ServerName = s.Name })
            .ToListAsync(ct);

        var groupDbRoles = await db.GroupDatabaseRoles
            .AsNoTracking()
            .Where(r => groupIds.Contains(r.GroupId))
            .Join(db.Databases, r => r.DatabaseId, d => d.Id, (r, d) => new { r.DatabaseId, r.Permission, r.GroupId, d.DisplayName, d.ServerId })
            .Join(db.Servers, x => x.ServerId, s => s.Id, (x, s) => new { x.DatabaseId, x.Permission, x.GroupId, x.DisplayName, ServerName = s.Name })
            .Join(db.Groups, x => x.GroupId, g => g.Id, (x, g) => new { x.DatabaseId, x.Permission, x.GroupId, x.DisplayName, x.ServerName, GroupName = g.Name })
            .ToListAsync(ct);

        var databaseRoles = directDbRoles.Select(r => (r.DatabaseId, r.Permission))
            .Concat(groupDbRoles.Select(r => (r.DatabaseId, r.Permission)))
            .Distinct()
            .Select(key =>
            {
                var info = directDbRoles.FirstOrDefault(r => r.DatabaseId == key.DatabaseId && r.Permission == key.Permission) is { } d
                    ? new { d.DatabaseId, d.DisplayName, d.ServerName }
                    : groupDbRoles.Where(r => r.DatabaseId == key.DatabaseId && r.Permission == key.Permission)
                        .Select(r => new { r.DatabaseId, r.DisplayName, r.ServerName }).First();
                return new EffectiveDbRoleItem(
                    info.DatabaseId,
                    info.DisplayName,
                    info.ServerName,
                    key.Permission,
                    BuildSources(
                        directDbRoles.Any(r => r.DatabaseId == key.DatabaseId && r.Permission == key.Permission),
                        groupDbRoles.Where(r => r.DatabaseId == key.DatabaseId && r.Permission == key.Permission)
                            .Select(r => new GroupInfo(r.GroupId, r.GroupName))));
            })
            .ToList();

        // Column bypasses: direct + group-inherited (carry SchemaName through every projection).
        var directBypasses = await db.UserColumnBypasses
            .AsNoTracking()
            .Where(b => b.UserId == userId)
            .Join(db.SensitiveColumns, b => b.SensitiveColumnId, sc => sc.Id,
                (b, sc) => new { b.SensitiveColumnId, sc.DatabaseId, sc.SchemaName, sc.TableName, sc.ColumnName })
            .Join(db.Databases, x => x.DatabaseId, d => d.Id,
                (x, d) => new { x.SensitiveColumnId, x.DatabaseId, x.SchemaName, x.TableName, x.ColumnName, d.DisplayName })
            .ToListAsync(ct);

        var groupBypasses = await db.GroupColumnBypasses
            .AsNoTracking()
            .Where(b => groupIds.Contains(b.GroupId))
            .Join(db.SensitiveColumns, b => b.SensitiveColumnId, sc => sc.Id,
                (b, sc) => new { b.SensitiveColumnId, b.GroupId, sc.DatabaseId, sc.SchemaName, sc.TableName, sc.ColumnName })
            .Join(db.Databases, x => x.DatabaseId, d => d.Id,
                (x, d) => new { x.SensitiveColumnId, x.GroupId, x.DatabaseId, x.SchemaName, x.TableName, x.ColumnName, d.DisplayName })
            .Join(db.Groups, x => x.GroupId, g => g.Id,
                (x, g) => new { x.SensitiveColumnId, x.DatabaseId, x.SchemaName, x.TableName, x.ColumnName, x.DisplayName, GroupName = g.Name, x.GroupId })
            .ToListAsync(ct);

        var columnBypasses = directBypasses.Select(b => b.SensitiveColumnId)
            .Concat(groupBypasses.Select(b => b.SensitiveColumnId))
            .Distinct()
            .Select(colId =>
            {
                var info = directBypasses.FirstOrDefault(b => b.SensitiveColumnId == colId) is { } d
                    ? new { d.DatabaseId, d.DisplayName, d.SchemaName, d.TableName, d.ColumnName }
                    : groupBypasses.Where(b => b.SensitiveColumnId == colId)
                        .Select(b => new { b.DatabaseId, b.DisplayName, b.SchemaName, b.TableName, b.ColumnName }).First();
                return new EffectiveColumnBypassItem(
                    info.DatabaseId,
                    info.DisplayName,
                    colId,
                    info.SchemaName,
                    info.TableName,
                    info.ColumnName,
                    BuildSources(
                        directBypasses.Any(b => b.SensitiveColumnId == colId),
                        groupBypasses.Where(b => b.SensitiveColumnId == colId)
                            .Select(b => new GroupInfo(b.GroupId, b.GroupName))));
            })
            .ToList();

        var memberships = groupMemberships
            .Select(g => new UserGroupMembership(g.GroupId, g.Name))
            .ToList();

        return TypedResults.Ok(new EffectiveUserPermissionsResponse(
            globalPermissions, databaseRoles, columnBypasses, memberships));
    }

    // Builds the unified source list: an optional direct source plus one per distinct group.
    private static List<EffectivePermissionSource> BuildSources(bool direct, IEnumerable<GroupInfo> groups)
    {
        var sources = new List<EffectivePermissionSource>();
        if (direct)
        {
            sources.Add(new EffectivePermissionSource(Direct: true));
        }
        foreach (var group in groups.DistinctBy(g => g.Id))
        {
            sources.Add(new EffectivePermissionSource(Group: group));
        }
        return sources;
    }
}

internal sealed record PermissionCatalogResponse(string[] Permissions);

internal sealed record GrantPermissionRequest(string Permission);

internal sealed record UserGroupMembership(GroupId GroupId, string GroupName);

internal sealed record UserSummaryResponse(
    UserId Id,
    string? Email,
    string? Name,
    DateTimeOffset? LastLoginAt,
    string[] Permissions,
    IReadOnlyList<UserGroupMembership> Groups);

internal sealed record ListUsersResponse(IReadOnlyList<UserSummaryResponse> Users);

// One consistent source shape: { "direct": bool, "group": GroupInfo | null }.
// Direct grant → { direct: true, group: null }; inherited → { direct: false, group: {...} }.
internal sealed record EffectivePermissionSource(bool Direct = false, GroupInfo? Group = null);

internal sealed record GroupInfo(GroupId Id, string Name);

internal sealed record EffectivePermissionItem(
    string Permission,
    IReadOnlyList<EffectivePermissionSource> Sources);

internal sealed record EffectiveDbRoleItem(
    DatabaseId DatabaseId,
    string DatabaseName,
    string ServerName,
    string Permission,
    IReadOnlyList<EffectivePermissionSource> Sources);

internal sealed record EffectiveColumnBypassItem(
    DatabaseId DatabaseId,
    string DatabaseName,
    SensitiveColumnId SensitiveColumnId,
    string Schema,
    string Table,
    string Column,
    IReadOnlyList<EffectivePermissionSource> Sources);

internal sealed record EffectiveUserPermissionsResponse(
    IReadOnlyList<EffectivePermissionItem> Global,
    IReadOnlyList<EffectiveDbRoleItem> DatabaseRoles,
    IReadOnlyList<EffectiveColumnBypassItem> ColumnBypasses,
    IReadOnlyList<UserGroupMembership> Memberships);