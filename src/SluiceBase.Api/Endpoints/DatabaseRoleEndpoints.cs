using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SluiceBase.Api.Auth;
using SluiceBase.Api.Data;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Servers;
using SluiceBase.Core.Users;

namespace SluiceBase.Api.Endpoints;

internal static class DatabaseRoleEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin")
            .RequireAuthorization(Permissions.PermissionManage);

        admin.MapGet("/server", ListServers).WithName("AdminListServers");

        admin.MapGet("/database/{databaseId}/role", ListByDatabase)
            .WithName("ListDatabaseRoles");
        admin.MapPost("/database/{databaseId}/role", AssignByDatabase)
            .WithName("AssignDatabaseRole");
        admin.MapDelete("/database/{databaseId}/role/{userId}/{permission}", RemoveRole)
            .WithName("RemoveDatabaseRole");

        admin.MapGet("/user/{userId}/role", ListByUser)
            .WithName("ListUserRoles");
        admin.MapPost("/user/{userId}/role", AssignByUser)
            .WithName("AssignUserRole");
    }

    // ── admin server list ─────────────────────────────────────────────────────

    private static async Task<Ok<AdminServerListResponse>> ListServers(
        AppDbContext db, CancellationToken ct)
    {
        var servers = await db.Servers
            .AsNoTracking()
            .Where(s => s.DeletedAt == null)
            .Include(s => s.Databases.Where(d => d.DeletedAt == null))
            .OrderBy(s => s.Name)
            .ToListAsync(ct);

        return TypedResults.Ok(new AdminServerListResponse([
            .. servers.Select(s => new AdminServerItem(
                s.Id,
                s.Name,
                s.IsDisabled,
                [.. s.Databases
                    .Select(d => new AdminDatabaseItem(d.Id, d.DisplayName, d.IsDisabled))
                    .OrderBy(d => d.DisplayName)]))
        ]));
    }

    // ── list by database ──────────────────────────────────────────────────────

    private static async Task<Ok<DatabaseRoleListResponse>> ListByDatabase(
        DatabaseId databaseId, AppDbContext db, CancellationToken ct)
    {
        var direct = await db.UserDatabaseRoles
            .AsNoTracking()
            .Where(r => r.DatabaseId == databaseId)
            .Select(r => new { r.UserId, r.Permission })
            .ToListAsync(ct);

        var viaGroups = await db.AccessGroupDatabaseRoles
            .Where(r => r.DatabaseId == databaseId)
            .Join(db.AccessGroupMembers, r => r.GroupId, m => m.GroupId,
                (r, m) => new { m.UserId, r.Permission, r.GroupId })
            .Join(db.AccessGroups, x => x.GroupId, g => g.Id,
                (x, g) => new { x.UserId, x.Permission, Group = new GroupRef(g.Id, g.Name) })
            .ToListAsync(ct);

        var userIds = direct.Select(d => d.UserId)
            .Concat(viaGroups.Select(v => v.UserId)).Distinct().ToList();

        var logins = await db.ExternalLogins
            .Where(l => userIds.Contains(l.UserId))
            .Select(l => new { l.UserId, l.Email, l.Name })
            .ToListAsync(ct);

        var keys = direct.Select(d => (d.UserId, d.Permission))
            .Concat(viaGroups.Select(v => (v.UserId, v.Permission))).Distinct();

        var items = keys.Select(k =>
        {
            var login = logins.FirstOrDefault(l => l.UserId == k.UserId);
            return new EffectiveDatabaseRoleItem(
                k.UserId, login?.Email, login?.Name, k.Permission,
                direct.Any(d => d.UserId == k.UserId && d.Permission == k.Permission),
                viaGroups.Where(v => v.UserId == k.UserId && v.Permission == k.Permission)
                    .Select(v => v.Group).ToList());
        }).ToList();

        return TypedResults.Ok(new DatabaseRoleListResponse(items));
    }

    // ── assign by database ────────────────────────────────────────────────────

    private static async Task<Results<ValidationProblem, NotFound, Ok, Created>> AssignByDatabase(
        DatabaseId databaseId,
        AssignDatabaseRoleRequest req,
        AppDbContext db,
        ICurrentUserAccessor currentUser,
        TimeProvider clock,
        CancellationToken ct)
    {
        if (!Permissions.Scopeable.Contains(req.Permission))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["permission"] = [$"'{req.Permission}' is not a scopeable permission."]
            });
        }

        var userExists = await db.Users.AnyAsync(u => u.Id == req.UserId, ct);
        if (!userExists)
        {
            return TypedResults.NotFound();
        }

        var dbExists = await db.Databases.AnyAsync(d => d.Id == databaseId, ct);
        if (!dbExists)
        {
            return TypedResults.NotFound();
        }

        var existing = await db.UserDatabaseRoles.AnyAsync(
            r => r.UserId == req.UserId && r.Permission == req.Permission && r.DatabaseId == databaseId, ct);
        if (existing)
        {
            return TypedResults.Ok();
        }

        var actor = await currentUser.GetAsync(ct);
        db.UserDatabaseRoles.Add(UserDatabaseRole.Grant(
            req.UserId, req.Permission, databaseId, actor?.Id, clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/admin/database/{databaseId}/role");
    }

    // ── remove role ───────────────────────────────────────────────────────────

    private static async Task<NoContent> RemoveRole(
        DatabaseId databaseId,
        UserId userId,
        string permission,
        AppDbContext db,
        CancellationToken ct)
    {
        var role = await db.UserDatabaseRoles.SingleOrDefaultAsync(
            r => r.DatabaseId == databaseId && r.UserId == userId && r.Permission == permission, ct);
        if (role is not null)
        {
            db.UserDatabaseRoles.Remove(role);
            await db.SaveChangesAsync(ct);
        }

        return TypedResults.NoContent();
    }

    // ── list by user ──────────────────────────────────────────────────────────

    private static async Task<Ok<UserRoleListResponse>> ListByUser(
        UserId userId, AppDbContext db, CancellationToken ct)
    {
        var direct = await db.UserDatabaseRoles
            .Where(r => r.UserId == userId)
            .Select(r => new { r.DatabaseId, r.Permission })
            .ToListAsync(ct);

        var viaGroups = await db.AccessGroupMembers
            .Where(m => m.UserId == userId)
            .Join(db.AccessGroupDatabaseRoles, m => m.GroupId, r => r.GroupId,
                (m, r) => new { r.DatabaseId, r.Permission, r.GroupId })
            .Join(db.AccessGroups, x => x.GroupId, g => g.Id,
                (x, g) => new { x.DatabaseId, x.Permission, Group = new GroupRef(g.Id, g.Name) })
            .ToListAsync(ct);

        var dbNames = await db.Databases
            .Join(db.Servers, d => d.ServerId, s => s.Id, (d, s) => new { d.Id, d.DisplayName, ServerName = s.Name })
            .ToListAsync(ct);

        var keys = direct.Select(d => (d.DatabaseId, d.Permission))
            .Concat(viaGroups.Select(v => (v.DatabaseId, v.Permission))).Distinct();

        var items = keys.Select(k =>
        {
            var name = dbNames.FirstOrDefault(n => n.Id == k.DatabaseId);
            return new EffectiveUserRoleItem(
                k.DatabaseId, name?.DisplayName ?? "", name?.ServerName ?? "", k.Permission,
                direct.Any(d => d.DatabaseId == k.DatabaseId && d.Permission == k.Permission),
                viaGroups.Where(v => v.DatabaseId == k.DatabaseId && v.Permission == k.Permission)
                    .Select(v => v.Group).ToList());
        }).ToList();

        return TypedResults.Ok(new UserRoleListResponse(items));
    }

    // ── assign by user ────────────────────────────────────────────────────────

    private static async Task<Results<ValidationProblem, NotFound, Ok, Created>> AssignByUser(
        UserId userId,
        AssignUserRoleRequest req,
        AppDbContext db,
        ICurrentUserAccessor currentUser,
        TimeProvider clock,
        CancellationToken ct)
    {
        if (!Permissions.Scopeable.Contains(req.Permission))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["permission"] = [$"'{req.Permission}' is not a scopeable permission."]
            });
        }

        var userExists = await db.Users.AnyAsync(u => u.Id == userId, ct);
        if (!userExists)
        {
            return TypedResults.NotFound();
        }

        var dbExists = await db.Databases.AnyAsync(d => d.Id == req.DatabaseId, ct);
        if (!dbExists)
        {
            return TypedResults.NotFound();
        }

        var existing = await db.UserDatabaseRoles.AnyAsync(
            r => r.UserId == userId && r.Permission == req.Permission && r.DatabaseId == req.DatabaseId, ct);
        if (existing)
        {
            return TypedResults.Ok();
        }

        var actor = await currentUser.GetAsync(ct);
        db.UserDatabaseRoles.Add(UserDatabaseRole.Grant(
            userId, req.Permission, req.DatabaseId, actor?.Id, clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/admin/user/{userId}/role");
    }

    // ── request / response records ────────────────────────────────────────────

    public sealed record AssignDatabaseRoleRequest(UserId UserId, string Permission);
    public sealed record AssignUserRoleRequest(DatabaseId DatabaseId, string Permission);

    public sealed record EffectiveDatabaseRoleItem(
        UserId UserId,
        string? UserEmail,
        string? UserName,
        string Permission,
        bool FromDirect,
        IReadOnlyList<GroupRef> FromGroups);

    public sealed record DatabaseRoleListResponse(IReadOnlyList<EffectiveDatabaseRoleItem> Roles);

    public sealed record EffectiveUserRoleItem(
        DatabaseId DatabaseId,
        string DatabaseDisplayName,
        string ServerName,
        string Permission,
        bool FromDirect,
        IReadOnlyList<GroupRef> FromGroups);

    public sealed record UserRoleListResponse(IReadOnlyList<EffectiveUserRoleItem> Roles);

    public sealed record AdminDatabaseItem(DatabaseId Id, string DisplayName, bool IsDisabled);

    public sealed record AdminServerItem(
        ServerId Id,
        string Name,
        bool IsDisabled,
        IReadOnlyList<AdminDatabaseItem> Databases);

    public sealed record AdminServerListResponse(IReadOnlyList<AdminServerItem> Servers);
}
