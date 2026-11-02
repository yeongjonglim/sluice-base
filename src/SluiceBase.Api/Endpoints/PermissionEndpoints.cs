using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SluiceBase.Api.Auth;
using SluiceBase.Api.Data;
using SluiceBase.Api.Mcp;
using SluiceBase.Core.Permissions;
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
        admin.MapPost("/user/{userId}/disable", DisableUser).WithName("DisableUser");
        admin.MapPost("/user/{userId}/enable", EnableUser).WithName("EnableUser");
        admin.MapDelete("/user/{userId}/mcp-grant", RevokeUserMcpGrants).WithName("RevokeUserMcpGrants");
    }

    private static async Task<Ok<ListUsersResponse>> ListUsers(AppDbContext db, CancellationToken ct)
    {
        var users = await db.ExternalLogins
            .Include(u => u.User).ThenInclude(u => u.Permissions)
            .AsNoTracking()
            .OrderBy(u => u.Email)
            .Select(u => new { u.UserId, u.Email, u.Name, u.LastLoginAt, u.User.DisabledAt,
                Direct = u.User.Permissions.Select(p => p.Permission).ToList() })
            .ToListAsync(ct);

        // all group-global grants joined to membership + group name
        var groupGrants = await db.AccessGroupMembers
            .Join(db.AccessGroupPermissions, m => m.GroupId, p => p.GroupId,
                (m, p) => new { m.UserId, p.Permission, p.GroupId })
            .Join(db.AccessGroups, x => x.GroupId, g => g.Id,
                (x, g) => new { x.UserId, x.Permission, Group = new GroupRef(g.Id, g.Name) })
            .ToListAsync(ct);

        var result = users.Select(u =>
        {
            var groupsForUser = groupGrants.Where(g => g.UserId == u.UserId).ToList();
            var perms = u.Direct.Concat(groupsForUser.Select(g => g.Permission)).Distinct()
                .Select(perm => new EffectivePermission(
                    perm, u.Direct.Contains(perm),
                    groupsForUser.Where(g => g.Permission == perm).Select(g => g.Group).ToList()))
                .ToList();
            return new UserSummaryResponse(u.UserId, u.Email, u.Name, u.LastLoginAt, u.DisabledAt, perms);
        }).ToList();

        return TypedResults.Ok(new ListUsersResponse(result));
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

    // Disabling ends the user's browser sessions on their next request and revokes their MCP tokens now.
    private static async Task<Results<NoContent, NotFound, ValidationProblem>> DisableUser(
        UserId userId,
        AppDbContext db,
        ICurrentUserAccessor currentUser,
        IMcpTokenService tokens,
        TimeProvider clock,
        CancellationToken ct)
    {
        var actor = await currentUser.GetAsync(ct);
        if (actor?.Id == userId)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["userId"] = ["You cannot disable your own account."],
            });
        }

        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
        {
            return TypedResults.NotFound();
        }

        user.Disable(clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        await tokens.RevokeAllAsync(userId, ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, NotFound>> EnableUser(
        UserId userId, AppDbContext db, CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
        {
            return TypedResults.NotFound();
        }

        user.Enable();
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, NotFound>> RevokeUserMcpGrants(
        UserId userId, AppDbContext db, IMcpTokenService tokens, CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(u => u.Id == userId, ct))
        {
            return TypedResults.NotFound();
        }

        await tokens.RevokeAllAsync(userId, ct);
        return TypedResults.NoContent();
    }
}

internal sealed record PermissionCatalogResponse(string[] Permissions);

internal sealed record GrantPermissionRequest(string Permission);

internal sealed record UserSummaryResponse(
    UserId Id,
    string? Email,
    string? Name,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset? DisabledAt,
    IReadOnlyList<EffectivePermission> Permissions);

internal sealed record ListUsersResponse(IReadOnlyList<UserSummaryResponse> Users);