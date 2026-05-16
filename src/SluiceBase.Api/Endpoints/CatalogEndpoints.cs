using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SluiceBase.Api.Auth;
using SluiceBase.Api.Data;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Servers;

namespace SluiceBase.Api.Endpoints;

internal static class CatalogEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/catalog/server", ListServers)
            .RequireAuthorization()
            .WithName("CatalogListServers");
    }

    private static async Task<Ok<CatalogServersResponse>> ListServers(
        AppDbContext db,
        ICurrentUserAccessor currentUser,
        CancellationToken ct)
    {
        var user = await currentUser.GetAsync(ct);
        var isServerAdmin = user?.HasPermission(Permissions.ServerManage) ?? false;

        var baseQuery = db.Databases
            .AsNoTracking()
            .Where(d => d.DeletedAt == null && !d.IsDisabled);

        List<Database> databases;
        if (isServerAdmin)
        {
            databases = await baseQuery
                .Include(d => d.Server)
                .ToListAsync(ct);
        }
        else
        {
            var allowedIds = await db.UserDatabaseRoles
                .Where(r => r.UserId == user!.Id)
                .Select(r => r.DatabaseId)
                .ToListAsync(ct);

            databases = await baseQuery
                .Where(d => allowedIds.Contains(d.Id))
                .Include(d => d.Server)
                .ToListAsync(ct);
        }

        var servers = databases
            .Where(d => d.Server != null && d.Server.DeletedAt == null && !d.Server.IsDisabled)
            .GroupBy(d => d.Server!)
            .OrderBy(g => g.Key.Name)
            .Select(g => new CatalogServerItem(
                g.Key.Id,
                g.Key.Name,
                [.. g.Select(d => new CatalogDatabaseItem(d.Id, d.DisplayName, d.CanWrite))
                     .OrderBy(d => d.DisplayName)])
            )
            .ToList();

        return TypedResults.Ok(new CatalogServersResponse(servers));
    }

    public sealed record CatalogServersResponse(IReadOnlyList<CatalogServerItem> Servers);

    public sealed record CatalogServerItem(
        ServerId Id,
        string Name,
        IReadOnlyList<CatalogDatabaseItem> Databases);

    public sealed record CatalogDatabaseItem(
        DatabaseId Id,
        string DisplayName,
        bool CanWrite);
}
