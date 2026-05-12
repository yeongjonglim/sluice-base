using Microsoft.EntityFrameworkCore;
using SluiceBase.Api.Data;
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

        var userId = http.HttpContext.User.GetInternalUserId();

        _cached = await db.Users
            .Include(u => u.Permissions)
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == userId, ct);
        return _cached;
    }
}