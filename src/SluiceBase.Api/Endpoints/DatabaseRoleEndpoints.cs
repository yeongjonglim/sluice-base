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
            .RequireAuthorization();

        admin.MapGet("/server", ListServers)
            .WithName("AdminListServers");

        admin.MapGet("/database/{databaseId}/role", ListByDatabase)
            .WithName("ListDatabaseRoles");
        admin.MapPost("/database/{databaseId}/role", AssignByDatabase)
            .WithName("AssignDatabaseRole");
        admin.MapDelete("/database/{databaseId}/role/user/{userId}/{permission}", RemoveUserRole)
            .WithName("RemoveUserDatabaseRole");
        admin.MapDelete("/database/{databaseId}/role/group/{groupId}/{permission}", RemoveGroupRole)
            .WithName("RemoveGroupDatabaseRole");

        admin.MapGet("/user/{userId}/role", ListByUser)
            .WithName("ListUserRoles");
        admin.MapPost("/user/{userId}/role", AssignByUser)
            .WithName("AssignUserRole");
    }

    // ── admin server list ─────────────────────────────────────────────────────

    private static async Task<Results<ForbidHttpResult, Ok<AdminServerListResponse>>> ListServers(
        AppDbContext db,
        ICurrentUserAccessor currentUser,
        CancellationToken ct)
    {
        var user = await currentUser.GetAsync(ct);
        if (user is null || (!user.HasPermission(Permissions.PermissionManage) && !user.HasPermission(Permissions.GroupManage)))
        {
            return TypedResults.Forbid();
        }

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

    private static async Task<Results<ForbidHttpResult, Ok<DatabaseRoleListResponse>>> ListByDatabase(
        DatabaseId databaseId,
        AppDbContext db,
        ICurrentUserAccessor currentUser,
        CancellationToken ct)
    {
        var user = await currentUser.GetAsync(ct);
        if (user is null || (!user.HasPermission(Permissions.PermissionManage) && !user.HasPermission(Permissions.GroupManage)))
        {
            return TypedResults.Forbid();
        }

        var userRoles = await db.UserDatabaseRoles
            .AsNoTracking()
            .Where(r => r.DatabaseId == databaseId)
            .Join(db.ExternalLogins,
                r => r.UserId,
                l => l.UserId,
                (r, l) => new DatabaseRoleItem("user", r.UserId, null, l.Email, l.Name, r.Permission, r.GrantedAt, r.GrantedById))
            .ToListAsync(ct);

        var groupRoles = await db.GroupDatabaseRoles
            .AsNoTracking()
            .Where(r => r.DatabaseId == databaseId)
            .Join(db.Groups,
                r => r.GroupId,
                g => g.Id,
                (r, g) => new DatabaseRoleItem("group", null, r.GroupId, g.Name, null, r.Permission, r.GrantedAt, r.GrantedById))
            .ToListAsync(ct);

        return TypedResults.Ok(new DatabaseRoleListResponse([.. userRoles, .. groupRoles]));
    }

    // ── assign by database ────────────────────────────────────────────────────

    private static async Task<Results<ValidationProblem, NotFound, Ok, Created, ForbidHttpResult>> AssignByDatabase(
        DatabaseId databaseId,
        AssignDatabaseRoleRequest req,
        AppDbContext db,
        ICurrentUserAccessor currentUser,
        TimeProvider clock,
        CancellationToken ct)
    {
        if ((req.UserId is null && req.GroupId is null) || (req.UserId is not null && req.GroupId is not null))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [""] = ["Exactly one of 'userId' or 'groupId' must be provided."]
            });
        }

        if (!Permissions.Scopeable.Contains(req.Permission))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["permission"] = [$"'{req.Permission}' is not a scopeable permission."]
            });
        }

        var dbExists = await db.Databases.AnyAsync(d => d.Id == databaseId, ct);
        if (!dbExists)
        {
            return TypedResults.NotFound();
        }

        var actor = await currentUser.GetAsync(ct);
        if (actor is null || !actor.HasPermission(req.UserId is not null ? Permissions.PermissionManage : Permissions.GroupManage))
        {
            return TypedResults.Forbid();
        }

        if (req.UserId is { } userId)
        {
            var userExists = await db.Users.AnyAsync(u => u.Id == userId, ct);
            if (!userExists)
            {
                return TypedResults.NotFound();
            }

            var existing = await db.UserDatabaseRoles.AnyAsync(
                r => r.UserId == userId && r.Permission == req.Permission && r.DatabaseId == databaseId, ct);
            if (existing)
            {
                return TypedResults.Ok();
            }

            db.UserDatabaseRoles.Add(UserDatabaseRole.Grant(
                userId, req.Permission, databaseId, actor.Id, clock.GetUtcNow()));
        }
        else
        {
            var groupId = req.GroupId!.Value;
            var groupExists = await db.Groups.AnyAsync(g => g.Id == groupId, ct);
            if (!groupExists)
            {
                return TypedResults.NotFound();
            }

            var existing = await db.GroupDatabaseRoles.AnyAsync(
                r => r.GroupId == groupId && r.Permission == req.Permission && r.DatabaseId == databaseId, ct);
            if (existing)
            {
                return TypedResults.Ok();
            }

            db.GroupDatabaseRoles.Add(GroupDatabaseRole.Grant(
                groupId, req.Permission, databaseId, actor.Id, clock.GetUtcNow()));
        }

        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/admin/database/{databaseId}/role");
    }

    // ── remove role ───────────────────────────────────────────────────────────

    private static async Task<NoContent> RemoveUserRole(
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

    private static async Task<NoContent> RemoveGroupRole(
        DatabaseId databaseId,
        GroupId groupId,
        string permission,
        AppDbContext db,
        CancellationToken ct)
    {
        var role = await db.GroupDatabaseRoles.SingleOrDefaultAsync(
            r => r.DatabaseId == databaseId && r.GroupId == groupId && r.Permission == permission, ct);
        if (role is not null)
        {
            db.GroupDatabaseRoles.Remove(role);
            await db.SaveChangesAsync(ct);
        }

        return TypedResults.NoContent();
    }

    // ── list by user ──────────────────────────────────────────────────────────

    private static async Task<Ok<UserRoleListResponse>> ListByUser(
        UserId userId, AppDbContext db, CancellationToken ct)
    {
        var directRoles = await db.UserDatabaseRoles
            .AsNoTracking()
            .Where(r => r.UserId == userId)
            .Select(r => new UserRoleItem(
                r.DatabaseId,
                db.Databases.Where(d => d.Id == r.DatabaseId).Select(d => d.DisplayName).FirstOrDefault() ?? "",
                db.Databases.Where(d => d.Id == r.DatabaseId)
                    .Select(d => db.Servers.Where(s => s.Id == d.ServerId).Select(s => s.Name).FirstOrDefault())
                    .FirstOrDefault() ?? "",
                r.Permission,
                r.GrantedAt,
                "direct",
                null))
            .ToListAsync(ct);

        var userGroupIds = db.GroupMembers
            .Where(gm => gm.UserId == userId)
            .Select(gm => gm.GroupId);

        var groupRoles = await db.GroupDatabaseRoles
            .AsNoTracking()
            .Where(r => userGroupIds.Contains(r.GroupId))
            .Join(db.Groups, r => r.GroupId, g => g.Id, (r, g) => new { r, g.Name })
            .Select(x => new UserRoleItem(
                x.r.DatabaseId,
                db.Databases.Where(d => d.Id == x.r.DatabaseId).Select(d => d.DisplayName).FirstOrDefault() ?? "",
                db.Databases.Where(d => d.Id == x.r.DatabaseId)
                    .Select(d => db.Servers.Where(s => s.Id == d.ServerId).Select(s => s.Name).FirstOrDefault())
                    .FirstOrDefault() ?? "",
                x.r.Permission,
                x.r.GrantedAt,
                "group",
                x.Name))
            .ToListAsync(ct);

        return TypedResults.Ok(new UserRoleListResponse([.. directRoles, .. groupRoles]));
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

    public sealed record AssignDatabaseRoleRequest(UserId? UserId, GroupId? GroupId, string Permission);
    public sealed record AssignUserRoleRequest(DatabaseId DatabaseId, string Permission);

    public sealed record DatabaseRoleItem(
        string Type,
        UserId? UserId,
        GroupId? GroupId,
        string? DisplayName,
        string? SecondaryName,
        string Permission,
        DateTimeOffset GrantedAt,
        UserId? GrantedById);

    public sealed record DatabaseRoleListResponse(IReadOnlyList<DatabaseRoleItem> Roles);

    public sealed record UserRoleItem(
        DatabaseId DatabaseId,
        string DatabaseDisplayName,
        string ServerName,
        string Permission,
        DateTimeOffset GrantedAt,
        string Source,
        string? GroupName);

    public sealed record UserRoleListResponse(IReadOnlyList<UserRoleItem> Roles);

    public sealed record AdminDatabaseItem(DatabaseId Id, string DisplayName, bool IsDisabled);

    public sealed record AdminServerItem(
        ServerId Id,
        string Name,
        bool IsDisabled,
        IReadOnlyList<AdminDatabaseItem> Databases);

    public sealed record AdminServerListResponse(IReadOnlyList<AdminServerItem> Servers);
}
