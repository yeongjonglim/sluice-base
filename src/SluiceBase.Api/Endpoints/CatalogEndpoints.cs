using Microsoft.AspNetCore.Http.HttpResults;
using SluiceBase.Api.Auth;
using SluiceBase.Api.Services;
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
        ICatalogService catalog,
        ICurrentUserAccessor currentUser,
        CancellationToken ct)
    {
        var user = await currentUser.GetAsync(ct);
        return TypedResults.Ok(await catalog.ListAccessibleAsync(user!, ct));
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
