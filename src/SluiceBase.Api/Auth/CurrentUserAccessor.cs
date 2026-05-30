using Microsoft.EntityFrameworkCore;
using SluiceBase.Api.Data;
using SluiceBase.Core.Permissions;
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
        if (_loaded)
        {
            return _cached;
        }

        _loaded = true;

        if (http.HttpContext?.User is null)
        {
            return null;
        }

        if (!http.HttpContext.User.TryGetInternalUserId(out var userId))
        {
            return null;
        }

        var user = await db.Users
            .Include(u => u.Permissions)
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == userId, ct);

        if (user is not null)
        {
            var groupPermissions = await db.GroupPermissions
                .AsNoTracking()
                .Where(gp => db.GroupMembers.Any(
                    gm => gm.GroupId == gp.GroupId && gm.UserId == userId))
                .ToListAsync(ct);

            foreach (var gp in groupPermissions)
            {
                if (!user.HasPermission(gp.Permission))
                {
                    user.Permissions.Add(UserPermissionMap.Grant(
                        userId, gp.Permission, gp.GrantedById, gp.GrantedAt));
                }
            }
        }

        _cached = user;
        return _cached;
    }
}