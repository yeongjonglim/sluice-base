using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SluiceBase.Api.Data;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Servers;

namespace SluiceBase.Api.Endpoints;

internal static class CatalogEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var catalog = app.MapGroup("/api/catalog")
            .RequireAuthorization(Permissions.CatalogRead);

        catalog.MapGet("/server", ListServers).WithName("CatalogListServers");
    }

    private static async Task<Ok<CatalogServersResponse>> ListServers(
        AppDbContext db, CancellationToken ct)
    {
        var servers = await db.Servers
            .AsNoTracking()
            .Where(s => s.DeletedAt == null && !s.IsDisabled)
            .Include(s => s.Databases.Where(d => d.DeletedAt == null && !d.IsDisabled))
            .OrderBy(s => s.Name)
            .ToListAsync(ct);

        return TypedResults.Ok(new CatalogServersResponse(
        [
            .. servers.Select(s => new CatalogServerItem(
                    s.Id,
                    s.Name,
                    [
                        .. s.Databases
                            .Select(d => new CatalogDatabaseItem(d.Id, d.DisplayName, d.CanWrite))
                            .OrderBy(d => d.DisplayName)
                    ]
                )
            )
        ]));
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