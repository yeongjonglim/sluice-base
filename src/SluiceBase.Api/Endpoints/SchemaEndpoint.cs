using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SluiceBase.Api.Data;
using SluiceBase.Api.Servers;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Schemas;
using SluiceBase.Core.Servers;
using SluiceBase.Core.Targets;

namespace SluiceBase.Api.Endpoints;

internal static class SchemaEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/schema/{serverId}", GetSchema)
            .RequireAuthorization(Permissions.QueryExecute)
            .WithName("GetSchema");
    }

    private static async Task<Results<Ok<SchemaTree>, NotFound>> GetSchema(
        ServerId serverId,
        AppDbContext db,
        IServerConnectionFactory connectionFactory,
        ITargetEngine targetEngine,
        CancellationToken ct)
    {
        var server = await db.Servers.AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == serverId, ct);
        if (server is null)
        {
            return TypedResults.NotFound();
        }

        var connectionString = await connectionFactory
            .GetConnectionStringAsync(serverId, CredentialKind.Read, ct);

        var tree = await targetEngine.GetSchemaAsync(connectionString, ct);
        return TypedResults.Ok(tree);
    }
}