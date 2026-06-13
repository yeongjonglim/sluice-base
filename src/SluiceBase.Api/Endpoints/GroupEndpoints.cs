using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SluiceBase.Api.Auth;
using SluiceBase.Api.Data;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Users;

namespace SluiceBase.Api.Endpoints;

internal static class GroupEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin/group")
            .RequireAuthorization(Permissions.PermissionManage);

        admin.MapGet("/", ListGroups).WithName("ListGroups");
        admin.MapPost("/", CreateGroup).WithName("CreateGroup");
        admin.MapPut("/{groupId}", UpdateGroup).WithName("UpdateGroup");
        admin.MapDelete("/{groupId}", DeleteGroup).WithName("DeleteGroup");

        admin.MapGet("/{groupId}/member", ListMembers).WithName("ListGroupMembers");
        admin.MapPost("/{groupId}/member", AddMember).WithName("AddGroupMember");
        admin.MapDelete("/{groupId}/member/{userId}", RemoveMember).WithName("RemoveGroupMember");

        admin.MapGet("/{groupId}/permission", ListGroupPermissions).WithName("ListGroupPermissions");
        admin.MapPost("/{groupId}/permission", GrantGroupPermission).WithName("GrantGroupPermission");
        admin.MapDelete("/{groupId}/permission/{permission}", RevokeGroupPermission).WithName("RevokeGroupPermission");
    }

    // ── group CRUD ───────────────────────────────────────────────────────────

    private static async Task<Ok<GroupListResponse>> ListGroups(
        AppDbContext db, CancellationToken ct)
    {
        var groups = await db.Groups
            .AsNoTracking()
            .OrderBy(g => g.Name)
            .Select(g => new GroupItem(
                g.Id,
                g.Name,
                g.Description,
                g.CreatedAt,
                db.GroupMembers.Count(m => m.GroupId == g.Id)))
            .ToListAsync(ct);

        return TypedResults.Ok(new GroupListResponse(groups));
    }

    private static async Task<Results<ValidationProblem, Created<GroupItem>>> CreateGroup(
        CreateGroupRequest req,
        AppDbContext db,
        ICurrentUserAccessor currentUser,
        TimeProvider clock,
        CancellationToken ct)
    {
        var nameExists = await db.Groups.AnyAsync(g => g.Name == req.Name, ct);
        if (nameExists)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["name"] = [$"A group named '{req.Name}' already exists."]
            });
        }

        var actor = await currentUser.GetAsync(ct);
        var group = Group.Create(req.Name, req.Description, actor!.Id, clock.GetUtcNow());
        db.Groups.Add(group);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created(
            $"/api/admin/group/{group.Id}",
            new GroupItem(group.Id, group.Name, group.Description, group.CreatedAt, 0));
    }

    private static async Task<Results<ValidationProblem, NotFound, Ok<GroupItem>>> UpdateGroup(
        GroupId groupId,
        UpdateGroupRequest req,
        AppDbContext db,
        CancellationToken ct)
    {
        var group = await db.Groups.SingleOrDefaultAsync(g => g.Id == groupId, ct);
        if (group is null)
        {
            return TypedResults.NotFound();
        }

        var nameConflict = await db.Groups.AnyAsync(g => g.Name == req.Name && g.Id != groupId, ct);
        if (nameConflict)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["name"] = [$"A group named '{req.Name}' already exists."]
            });
        }

        group.Update(req.Name, req.Description);
        await db.SaveChangesAsync(ct);

        var memberCount = await db.GroupMembers.CountAsync(m => m.GroupId == groupId, ct);
        return TypedResults.Ok(new GroupItem(group.Id, group.Name, group.Description, group.CreatedAt, memberCount));
    }

    private static async Task<NoContent> DeleteGroup(
        GroupId groupId,
        AppDbContext db,
        CancellationToken ct)
    {
        var group = await db.Groups.SingleOrDefaultAsync(g => g.Id == groupId, ct);
        if (group is not null)
        {
            db.Groups.Remove(group);
            await db.SaveChangesAsync(ct);
        }

        return TypedResults.NoContent();
    }

    // ── membership ───────────────────────────────────────────────────────────

    private static async Task<Results<NotFound, Ok<GroupMemberListResponse>>> ListMembers(
        GroupId groupId, AppDbContext db, CancellationToken ct)
    {
        var groupExists = await db.Groups.AnyAsync(g => g.Id == groupId, ct);
        if (!groupExists)
        {
            return TypedResults.NotFound();
        }

        var members = await db.GroupMembers
            .AsNoTracking()
            .Where(m => m.GroupId == groupId)
            .Join(db.ExternalLogins,
                m => m.UserId,
                l => l.UserId,
                (m, l) => new GroupMemberItem(m.Id, m.UserId, l.Email, l.Name, m.AddedAt, m.AddedById))
            .ToListAsync(ct);

        return TypedResults.Ok(new GroupMemberListResponse(members));
    }

    private static async Task<Results<NotFound, Ok, Created>> AddMember(
        GroupId groupId,
        AddGroupMemberRequest req,
        AppDbContext db,
        ICurrentUserAccessor currentUser,
        TimeProvider clock,
        CancellationToken ct)
    {
        var groupExists = await db.Groups.AnyAsync(g => g.Id == groupId, ct);
        if (!groupExists)
        {
            return TypedResults.NotFound();
        }

        var userExists = await db.Users.AnyAsync(u => u.Id == req.UserId, ct);
        if (!userExists)
        {
            return TypedResults.NotFound();
        }

        var existing = await db.GroupMembers.AnyAsync(
            m => m.GroupId == groupId && m.UserId == req.UserId, ct);
        if (existing)
        {
            return TypedResults.Ok();
        }

        var actor = await currentUser.GetAsync(ct);
        db.GroupMembers.Add(GroupMember.Add(groupId, req.UserId, actor?.Id, clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/admin/group/{groupId}/member");
    }

    private static async Task<NoContent> RemoveMember(
        GroupId groupId,
        UserId userId,
        AppDbContext db,
        CancellationToken ct)
    {
        var member = await db.GroupMembers.SingleOrDefaultAsync(
            m => m.GroupId == groupId && m.UserId == userId, ct);
        if (member is not null)
        {
            db.GroupMembers.Remove(member);
            await db.SaveChangesAsync(ct);
        }

        return TypedResults.NoContent();
    }

    // ── group global permissions ─────────────────────────────────────────────

    private static async Task<Results<NotFound, Ok<GroupPermissionListResponse>>> ListGroupPermissions(
        GroupId groupId, AppDbContext db, CancellationToken ct)
    {
        var groupExists = await db.Groups.AnyAsync(g => g.Id == groupId, ct);
        if (!groupExists)
        {
            return TypedResults.NotFound();
        }

        var permissions = await db.GroupPermissions
            .AsNoTracking()
            .Where(p => p.GroupId == groupId)
            .Select(p => new GroupPermissionItem(p.Id, p.Permission, p.GrantedAt, p.GrantedById))
            .ToListAsync(ct);

        return TypedResults.Ok(new GroupPermissionListResponse(permissions));
    }

    private static async Task<Results<ValidationProblem, NotFound, Ok, Created>> GrantGroupPermission(
        GroupId groupId,
        GrantGroupPermissionRequest req,
        AppDbContext db,
        ICurrentUserAccessor currentUser,
        TimeProvider clock,
        CancellationToken ct)
    {
        if (!Permissions.Global.Contains(req.Permission))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["permission"] = [$"'{req.Permission}' is not a known global permission."]
            });
        }

        var groupExists = await db.Groups.AnyAsync(g => g.Id == groupId, ct);
        if (!groupExists)
        {
            return TypedResults.NotFound();
        }

        var existing = await db.GroupPermissions.AnyAsync(
            p => p.GroupId == groupId && p.Permission == req.Permission, ct);
        if (existing)
        {
            return TypedResults.Ok();
        }

        var actor = await currentUser.GetAsync(ct);
        db.GroupPermissions.Add(GroupPermissionMap.Grant(
            groupId, req.Permission, actor?.Id, clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/admin/group/{groupId}/permission");
    }

    private static async Task<NoContent> RevokeGroupPermission(
        GroupId groupId,
        string permission,
        AppDbContext db,
        CancellationToken ct)
    {
        var grant = await db.GroupPermissions.SingleOrDefaultAsync(
            p => p.GroupId == groupId && p.Permission == permission, ct);
        if (grant is not null)
        {
            db.GroupPermissions.Remove(grant);
            await db.SaveChangesAsync(ct);
        }

        return TypedResults.NoContent();
    }

    // ── request / response records ───────────────────────────────────────────

    public sealed record CreateGroupRequest(string Name, string? Description);
    public sealed record UpdateGroupRequest(string Name, string? Description);
    public sealed record AddGroupMemberRequest(UserId UserId);
    public sealed record GrantGroupPermissionRequest(string Permission);

    public sealed record GroupItem(
        GroupId Id,
        string Name,
        string? Description,
        DateTimeOffset CreatedAt,
        int MemberCount);

    public sealed record GroupListResponse(IReadOnlyList<GroupItem> Groups);

    public sealed record GroupMemberItem(
        GroupMemberId Id,
        UserId UserId,
        string? UserEmail,
        string? UserName,
        DateTimeOffset AddedAt,
        UserId? AddedById);

    public sealed record GroupMemberListResponse(IReadOnlyList<GroupMemberItem> Members);

    public sealed record GroupPermissionItem(
        GroupPermissionId Id,
        string Permission,
        DateTimeOffset GrantedAt,
        UserId? GrantedById);

    public sealed record GroupPermissionListResponse(IReadOnlyList<GroupPermissionItem> Permissions);
}
