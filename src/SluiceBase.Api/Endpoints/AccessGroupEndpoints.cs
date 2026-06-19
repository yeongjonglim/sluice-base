using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SluiceBase.Api.Auth;
using SluiceBase.Api.Data;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Servers;
using SluiceBase.Core.Users;

namespace SluiceBase.Api.Endpoints;

internal static class AccessGroupEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin").RequireAuthorization(Permissions.PermissionManage);

        admin.MapGet("/group", ListGroups).WithName("ListAccessGroups");
        admin.MapPost("/group", CreateGroup).WithName("CreateAccessGroup");
        admin.MapGet("/group/{groupId}", GetGroup).WithName("GetAccessGroup");
        admin.MapPatch("/group/{groupId}", UpdateGroup).WithName("UpdateAccessGroup");
        admin.MapDelete("/group/{groupId}", DeleteGroup).WithName("DeleteAccessGroup");

        admin.MapPost("/group/{groupId}/member/{userId}", AddMember).WithName("AddAccessGroupMember");
        admin.MapDelete("/group/{groupId}/member/{userId}", RemoveMember).WithName("RemoveAccessGroupMember");

        admin.MapPost("/group/{groupId}/permission/{permission}", GrantGlobal).WithName("GrantAccessGroupPermission");
        admin.MapDelete("/group/{groupId}/permission/{permission}", RevokeGlobal).WithName("RevokeAccessGroupPermission");

        admin.MapPost("/group/{groupId}/database/{databaseId}/role/{permission}", GrantDbRole).WithName("GrantAccessGroupDatabaseRole");
        admin.MapDelete("/group/{groupId}/database/{databaseId}/role/{permission}", RevokeDbRole).WithName("RevokeAccessGroupDatabaseRole");
    }

    private static async Task<Ok<GroupListResponse>> ListGroups(AppDbContext db, CancellationToken ct)
    {
        var groups = await db.AccessGroups
            .AsNoTracking()
            .OrderBy(g => g.Name)
            .Select(g => new GroupSummary(
                g.Id, g.Name, g.Description,
                db.AccessGroupMembers.Count(m => m.GroupId == g.Id),
                db.AccessGroupPermissions.Count(p => p.GroupId == g.Id),
                db.AccessGroupDatabaseRoles.Count(r => r.GroupId == g.Id)))
            .ToListAsync(ct);
        return TypedResults.Ok(new GroupListResponse(groups));
    }

    private static async Task<Created> CreateGroup(
        CreateGroupRequest req, AppDbContext db, ICurrentUserAccessor currentUser, TimeProvider clock, CancellationToken ct)
    {
        var actor = await currentUser.GetAsync(ct);
        var group = AccessGroup.Create(req.Name, req.Description, actor?.Id, clock.GetUtcNow());
        db.AccessGroups.Add(group);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/admin/group/{group.Id}");
    }

    private static async Task<Results<Ok<GroupDetailResponse>, NotFound>> GetGroup(
        AccessGroupId groupId, AppDbContext db, CancellationToken ct)
    {
        var group = await db.AccessGroups.AsNoTracking().SingleOrDefaultAsync(g => g.Id == groupId, ct);
        if (group is null)
        {
            return TypedResults.NotFound();
        }

        var members = await db.AccessGroupMembers
            .Where(m => m.GroupId == groupId)
            .Join(db.ExternalLogins, m => m.UserId, l => l.UserId,
                (m, l) => new GroupMemberItem(m.UserId, l.Email, l.Name))
            .ToListAsync(ct);
        var global = await db.AccessGroupPermissions
            .Where(p => p.GroupId == groupId).Select(p => p.Permission).ToListAsync(ct);
        var roles = await db.AccessGroupDatabaseRoles
            .Where(r => r.GroupId == groupId)
            .Select(r => new GroupDatabaseRoleItem(r.DatabaseId, r.Permission)).ToListAsync(ct);

        return TypedResults.Ok(new GroupDetailResponse(group.Id, group.Name, group.Description, members, global, roles));
    }

    private static async Task<Results<NoContent, NotFound>> UpdateGroup(
        AccessGroupId groupId, UpdateGroupRequest req, AppDbContext db, CancellationToken ct)
    {
        var group = await db.AccessGroups.SingleOrDefaultAsync(g => g.Id == groupId, ct);
        if (group is null)
        {
            return TypedResults.NotFound();
        }

        group.Rename(req.Name);
        group.SetDescription(req.Description);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<NoContent> DeleteGroup(AccessGroupId groupId, AppDbContext db, CancellationToken ct)
    {
        var group = await db.AccessGroups.SingleOrDefaultAsync(g => g.Id == groupId, ct);
        if (group is not null)
        {
            db.AccessGroups.Remove(group); // cascades members + grant rows
            await db.SaveChangesAsync(ct);
        }

        return TypedResults.NoContent();
    }

    private static async Task<Results<Created, NotFound, Ok>> AddMember(
        AccessGroupId groupId, UserId userId, AppDbContext db, ICurrentUserAccessor currentUser, TimeProvider clock, CancellationToken ct)
    {
        if (!await db.AccessGroups.AnyAsync(g => g.Id == groupId, ct))
        {
            return TypedResults.NotFound();
        }

        if (!await db.Users.AnyAsync(u => u.Id == userId, ct))
        {
            return TypedResults.NotFound();
        }

        if (await db.AccessGroupMembers.AnyAsync(m => m.GroupId == groupId && m.UserId == userId, ct))
        {
            return TypedResults.Ok();
        }

        var actor = await currentUser.GetAsync(ct);
        db.AccessGroupMembers.Add(AccessGroupMember.Add(groupId, userId, actor?.Id, clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/admin/group/{groupId}/member/{userId}");
    }

    private static async Task<NoContent> RemoveMember(
        AccessGroupId groupId, UserId userId, AppDbContext db, CancellationToken ct)
    {
        var member = await db.AccessGroupMembers.SingleOrDefaultAsync(m => m.GroupId == groupId && m.UserId == userId, ct);
        if (member is not null)
        {
            db.AccessGroupMembers.Remove(member);
            await db.SaveChangesAsync(ct);
        }

        return TypedResults.NoContent();
    }

    private static async Task<Results<Created, NotFound, Ok, ValidationProblem>> GrantGlobal(
        AccessGroupId groupId, string permission, AppDbContext db, ICurrentUserAccessor currentUser, TimeProvider clock, CancellationToken ct)
    {
        if (!Permissions.Global.Contains(permission))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["permission"] = [$"'{permission}' is not a global permission."]
            });
        }

        if (!await db.AccessGroups.AnyAsync(g => g.Id == groupId, ct))
        {
            return TypedResults.NotFound();
        }

        if (await db.AccessGroupPermissions.AnyAsync(p => p.GroupId == groupId && p.Permission == permission, ct))
        {
            return TypedResults.Ok();
        }

        var actor = await currentUser.GetAsync(ct);
        db.AccessGroupPermissions.Add(AccessGroupPermission.Grant(groupId, permission, actor?.Id, clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/admin/group/{groupId}/permission/{permission}");
    }

    private static async Task<NoContent> RevokeGlobal(
        AccessGroupId groupId, string permission, AppDbContext db, CancellationToken ct)
    {
        var row = await db.AccessGroupPermissions.SingleOrDefaultAsync(p => p.GroupId == groupId && p.Permission == permission, ct);
        if (row is not null)
        {
            db.AccessGroupPermissions.Remove(row);
            await db.SaveChangesAsync(ct);
        }

        return TypedResults.NoContent();
    }

    private static async Task<Results<Created, NotFound, Ok, ValidationProblem>> GrantDbRole(
        AccessGroupId groupId, DatabaseId databaseId, string permission, AppDbContext db, ICurrentUserAccessor currentUser, TimeProvider clock, CancellationToken ct)
    {
        if (!Permissions.Scopeable.Contains(permission))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["permission"] = [$"'{permission}' is not a scopeable permission."]
            });
        }

        if (!await db.AccessGroups.AnyAsync(g => g.Id == groupId, ct))
        {
            return TypedResults.NotFound();
        }

        if (!await db.Databases.AnyAsync(d => d.Id == databaseId, ct))
        {
            return TypedResults.NotFound();
        }

        if (await db.AccessGroupDatabaseRoles.AnyAsync(r => r.GroupId == groupId && r.Permission == permission && r.DatabaseId == databaseId, ct))
        {
            return TypedResults.Ok();
        }

        var actor = await currentUser.GetAsync(ct);
        db.AccessGroupDatabaseRoles.Add(AccessGroupDatabaseRole.Grant(groupId, permission, databaseId, actor?.Id, clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/admin/group/{groupId}/database/{databaseId}/role/{permission}");
    }

    private static async Task<NoContent> RevokeDbRole(
        AccessGroupId groupId, DatabaseId databaseId, string permission, AppDbContext db, CancellationToken ct)
    {
        var row = await db.AccessGroupDatabaseRoles.SingleOrDefaultAsync(
            r => r.GroupId == groupId && r.DatabaseId == databaseId && r.Permission == permission, ct);
        if (row is not null)
        {
            db.AccessGroupDatabaseRoles.Remove(row);
            await db.SaveChangesAsync(ct);
        }

        return TypedResults.NoContent();
    }

    // ── request / response records ────────────────────────────────────────────

    public sealed record CreateGroupRequest(string Name, string? Description);
    public sealed record UpdateGroupRequest(string Name, string? Description);
    public sealed record GroupSummary(
        AccessGroupId Id, string Name, string? Description, int MemberCount, int GlobalPermissionCount, int DatabaseRoleCount);
    public sealed record GroupListResponse(IReadOnlyList<GroupSummary> Groups);
    public sealed record GroupMemberItem(UserId UserId, string? Email, string? Name);
    public sealed record GroupDatabaseRoleItem(DatabaseId DatabaseId, string Permission);
    public sealed record GroupDetailResponse(
        AccessGroupId Id, string Name, string? Description,
        IReadOnlyList<GroupMemberItem> Members, IReadOnlyList<string> GlobalPermissions,
        IReadOnlyList<GroupDatabaseRoleItem> DatabaseRoles);
}
