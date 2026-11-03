using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SluiceBase.Api.Data;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Users;

namespace SluiceBase.Api.Auth;

internal interface IUserLoginRecorder
{
    // verifiedEmail is the IdP-verified email used to match the bootstrap admin list; email is only for display.
    Task<User> RecordLoginAsync(string issuer, string sub, string? email, string? verifiedEmail, string? name, DateTimeOffset at, IEnumerable<ClaimRecord> claims, CancellationToken ct);
}

internal sealed class UserLoginRecorder(
    AppDbContext db,
    IOptions<BootstrapAdminOptions> bootstrap) : IUserLoginRecorder
{
    public async Task<User> RecordLoginAsync(string issuer, string sub, string? email, string? verifiedEmail, string? name, DateTimeOffset at, IEnumerable<ClaimRecord> claims, CancellationToken
            ct)
    {
        var externalLogin = await db.ExternalLogins
            .Include(x => x.User)
            .ThenInclude(u => u.Permissions)
            .SingleOrDefaultAsync(u => u.Issuer == issuer && u.Subject == sub, ct);

        if (externalLogin is null)
        {
            var user = User.Create(email, name, at);
            await db.Users.AddAsync(user, ct);
            externalLogin = ExternalLogin.Create(user.Id, issuer, sub, email, name, at, claims);
            await db.ExternalLogins.AddAsync(externalLogin, ct);
        }
        else
        {
            externalLogin.RecordLogin(at, email, name, claims);
        }

        var emailMatchesBootstrap = verifiedEmail is not null && bootstrap.Value.Admins.Any(b =>
            string.Equals(b, verifiedEmail, StringComparison.OrdinalIgnoreCase));

        if (emailMatchesBootstrap && !externalLogin.User.HasPermission(Permissions.PermissionManage))
        {
            await db.UserPermissions.AddAsync(UserPermissionMap.Grant(
                externalLogin.UserId,
                Permissions.PermissionManage,
                // I prefer to use UserId.System,
                // but it will cause foreign key violation since there is no user with that id exists
                grantedById: null,
                at), ct);
        }

        await db.SaveChangesAsync(ct);
        return externalLogin.User;
    }
}