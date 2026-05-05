using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SluiceBase.Api.Auth;
using SluiceBase.Api.Data;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Users;

namespace SluiceBase.Api.Endpoints;

internal static class PermissionEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/permission/catalog",
                () => Results.Ok(new PermissionCatalogResponse([.. Permissions.All])))
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

    private static async Task<Ok<ListUsersResponse>> ListUsers(AppDbContext db, CancellationToken ct)
    {
        var users = await db.Users
            .AsNoTracking()
            .OrderBy(u => u.Email)
            .Select(u => new UserSummaryResponse(
                u.Id,
                u.Sub,
                u.Email,
                u.Name,
                u.LastLoginAt,
                u.Permissions.Select(p => p.Permission).ToArray()))
            .ToListAsync(ct);
        return TypedResults.Ok(new ListUsersResponse(users));
    }

    private static async Task<Results<ValidationProblem, NotFound, Ok, Created>> GrantPermission(
        UserId userId,
        GrantPermissionRequest req,
        AppDbContext db,
        ICurrentUserAccessor currentUser,
        TimeProvider clock,
        CancellationToken ct)
    {
        if (!Permissions.All.Contains(req.Permission))
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
}

internal sealed record PermissionCatalogResponse(string[] Permissions);

internal sealed record GrantPermissionRequest(string Permission);

internal sealed record UserSummaryResponse(
    UserId Id,
    string Sub,
    string Email,
    string? Name,
    DateTimeOffset? LastLoginAt,
    string[] Permissions);

internal sealed record ListUsersResponse(IReadOnlyList<UserSummaryResponse> Users);