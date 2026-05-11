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
        app.MapGet("/api/schema/{databaseId}", GetSchema)
            .RequireAuthorization(Permissions.QueryExecute)
            .WithName("GetSchema");
    }

    private static async Task<Results<Ok<SchemaTree>, NotFound, BadRequest<string>>> GetSchema(
        DatabaseId databaseId,
        AppDbContext db,
        IServerConnectionFactory connectionFactory,
        ITargetEngine targetEngine,
        CancellationToken ct)
    {
        var database = await db.Databases.AsNoTracking()
            .SingleOrDefaultAsync(d => d.Id == databaseId, ct);
        if (database is null)
        {
            return TypedResults.NotFound();
        }

        try
        {
            var connectionString = await connectionFactory.GetConnectionStringAsync(databaseId, CredentialKind.Read, ct);
            var tree = await targetEngine.GetSchemaAsync(connectionString, ct);
            return TypedResults.Ok(tree);
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.BadRequest(ex.Message);
        }
    }
}