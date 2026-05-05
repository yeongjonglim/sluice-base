using System.Security.Claims;
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

        var sub = http.HttpContext?.User.FindFirstValue(AppClaims.Sub);
        if (string.IsNullOrEmpty(sub))
        {
            return null;
        }

        _cached = await db.Users
            .Include(u => u.Permissions)
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.Sub == sub, ct);
        return _cached;
    }
}