using Microsoft.EntityFrameworkCore;
using SluiceBase.Api.Auth;
using SluiceBase.Api.Data;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Servers;
using SluiceBase.Core.Users;
using static SluiceBase.Api.Endpoints.CatalogEndpoints;

namespace SluiceBase.Api.Services;

internal interface ICatalogService
{
    Task<CatalogServersResponse> ListAccessibleAsync(User user, CancellationToken ct);
}

internal sealed class CatalogService(AppDbContext db, IAccessResolver resolver) : ICatalogService
{
    public async Task<CatalogServersResponse> ListAccessibleAsync(User user, CancellationToken ct)
    {
        var isServerAdmin = await resolver.HasGlobalPermissionAsync(user.Id, Permissions.ServerManage, ct);
        var baseQuery = db.Databases.AsNoTracking().Where(d => d.DeletedAt == null && !d.IsDisabled);

        List<Database> databases;
        if (isServerAdmin)
        {
            databases = await baseQuery.Include(d => d.Server).ToListAsync(ct);
        }
        else
        {
            var allowedIds = await resolver.DatabasesWithPermissionAsync(user.Id, Permissions.QueryExecute, ct);
            databases = await baseQuery.Where(d => allowedIds.Contains(d.Id)).Include(d => d.Server).ToListAsync(ct);
        }

        var servers = databases
            .Where(d => d.Server != null && d.Server.DeletedAt == null && !d.Server.IsDisabled)
            .GroupBy(d => d.Server!)
            .OrderBy(g => g.Key.Name)
            .Select(g => new CatalogServerItem(g.Key.Id, g.Key.Name,
                [.. g.Select(d => new CatalogDatabaseItem(d.Id, d.DisplayName, d.CanWrite)).OrderBy(d => d.DisplayName)]))
            .ToList();

        return new CatalogServersResponse(servers);
    }
}
