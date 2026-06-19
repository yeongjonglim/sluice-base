using Microsoft.EntityFrameworkCore;
using SluiceBase.Api.Data;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Servers;
using SluiceBase.Core.Users;

namespace SluiceBase.Api.Auth;

internal sealed class AccessResolver(AppDbContext db) : IAccessResolver
{
    // group ids the user belongs to
    private IQueryable<AccessGroupId> MemberGroupIds(UserId user) =>
        db.AccessGroupMembers.Where(m => m.UserId == user).Select(m => m.GroupId);

    public async Task<bool> HasGlobalPermissionAsync(UserId user, string permission, CancellationToken ct)
    {
        var direct = await db.UserPermissions
            .AnyAsync(p => p.UserId == user && p.Permission == permission, ct);
        if (direct)
        {
            return true;
        }
        return await db.AccessGroupPermissions
            .Where(p => p.Permission == permission)
            .Where(p => MemberGroupIds(user).Contains(p.GroupId))
            .AnyAsync(ct);
    }

    public async Task<bool> HasDatabasePermissionAsync(UserId user, DatabaseId dbId, string permission, CancellationToken ct)
    {
        var direct = await db.UserDatabaseRoles
            .AnyAsync(r => r.UserId == user && r.DatabaseId == dbId && r.Permission == permission, ct);
        if (direct)
        {
            return true;
        }
        return await db.AccessGroupDatabaseRoles
            .Where(r => r.DatabaseId == dbId && r.Permission == permission)
            .Where(r => MemberGroupIds(user).Contains(r.GroupId))
            .AnyAsync(ct);
    }

    public async Task<IReadOnlySet<DatabaseId>> DatabasesWithPermissionAsync(UserId user, string permission, CancellationToken ct)
    {
        var direct = db.UserDatabaseRoles
            .Where(r => r.UserId == user && r.Permission == permission)
            .Select(r => r.DatabaseId);
        var viaGroup = db.AccessGroupDatabaseRoles
            .Where(r => r.Permission == permission)
            .Where(r => MemberGroupIds(user).Contains(r.GroupId))
            .Select(r => r.DatabaseId);
        var ids = await direct.Union(viaGroup).ToListAsync(ct);
        return ids.ToHashSet();
    }

    public async Task<IReadOnlySet<DatabaseId>> DatabasesWithAnyScopeableAsync(UserId user, CancellationToken ct)
    {
        var direct = db.UserDatabaseRoles
            .Where(r => r.UserId == user)
            .Select(r => r.DatabaseId);
        var viaGroup = db.AccessGroupDatabaseRoles
            .Where(r => MemberGroupIds(user).Contains(r.GroupId))
            .Select(r => r.DatabaseId);
        var ids = await direct.Union(viaGroup).ToListAsync(ct);
        return ids.ToHashSet();
    }

    public async Task<IReadOnlySet<string>> EffectivePermissionsAsync(UserId user, CancellationToken ct)
    {
        var directGlobal = db.UserPermissions.Where(p => p.UserId == user).Select(p => p.Permission);
        var directDb = db.UserDatabaseRoles.Where(r => r.UserId == user).Select(r => r.Permission);
        var groupGlobal = db.AccessGroupPermissions
            .Where(p => MemberGroupIds(user).Contains(p.GroupId)).Select(p => p.Permission);
        var groupDb = db.AccessGroupDatabaseRoles
            .Where(r => MemberGroupIds(user).Contains(r.GroupId)).Select(r => r.Permission);

        var all = await directGlobal.Union(directDb).Union(groupGlobal).Union(groupDb).ToListAsync(ct);
        return all.ToHashSet();
    }
}
