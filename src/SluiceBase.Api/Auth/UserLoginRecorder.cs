using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SluiceBase.Api.Data;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Users;

namespace SluiceBase.Api.Auth;

internal interface IUserLoginRecorder
{
    Task<User> RecordLoginAsync(string sub, string email, string? name, DateTimeOffset at, CancellationToken ct);
}

internal sealed class UserLoginRecorder(
    AppDbContext db,
    IOptions<BootstrapAdminOptions> bootstrap) : IUserLoginRecorder
{
    public async Task<User> RecordLoginAsync(string sub, string email, string? name, DateTimeOffset at, CancellationToken ct)
    {
        var user = await db.Users
            .Include(u => u.Permissions)
            .SingleOrDefaultAsync(u => u.Sub == sub, ct);

        if (user is null)
        {
            user = User.Create(sub, email, name, at);
            await db.Users.AddAsync(user, ct);
        }
        else
        {
            user.RecordLogin(email, name, at);
        }

        var emailMatchesBootstrap = bootstrap.Value.Admins.Any(b =>
            string.Equals(b, email, StringComparison.OrdinalIgnoreCase));

        if (emailMatchesBootstrap && !user.HasPermission(Permissions.PermissionManage))
        {
            await db.UserPermissions.AddAsync(UserPermissionMap.Grant(
                user.Id,
                Permissions.PermissionManage,
                // I prefer to use UserId.System,
                // but it will cause foreign key violation since there is no user with that id exists
                grantedById: null,
                at), ct);
        }

        await db.SaveChangesAsync(ct);
        return user;
    }
}