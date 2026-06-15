using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SluiceBase.Api.Auth;
using SluiceBase.Api.Data;
using SluiceBase.Api.Servers;
using SluiceBase.Api.Services;
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
            .RequireAuthorization()
            .WithName("GetSchema");

        app.MapGet("/api/schema/{databaseId}/ddl", ExportSchemaDdl)
            .RequireAuthorization()
            .WithName("ExportSchemaDdl");
    }

    private static async Task<Results<Ok<SchemaTree>, NotFound, BadRequest<string>, ForbidHttpResult>> GetSchema(
        DatabaseId databaseId,
        ISchemaService schema,
        ICurrentUserAccessor currentUser,
        CancellationToken ct)
    {
        var user = await currentUser.GetAsync(ct);
        var result = await schema.GetAnnotatedSchemaAsync(user!, databaseId, ct);
        return result.Outcome switch
        {
            SchemaOutcome.Ok => TypedResults.Ok(result.Tree!),
            SchemaOutcome.NotFound => TypedResults.NotFound(),
            SchemaOutcome.Forbidden => TypedResults.Forbid(),
            _ => TypedResults.BadRequest(result.Error!),
        };
    }

    private static async Task<Results<FileContentHttpResult, NotFound, BadRequest<string>, ForbidHttpResult>> ExportSchemaDdl(
        DatabaseId databaseId,
        AppDbContext db,
        ICurrentUserAccessor currentUser,
        IServerConnectionFactory connectionFactory,
        ITargetEngine targetEngine,
        CancellationToken ct)
    {
        var user = await currentUser.GetAsync(ct);

        var database = await db.Databases.AsNoTracking()
            .SingleOrDefaultAsync(d => d.Id == databaseId, ct);
        if (database is null)
        {
            return TypedResults.NotFound();
        }

        var hasRole = await db.UserDatabaseRoles.AnyAsync(
            r => r.UserId == user!.Id && r.Permission == Permissions.QueryExecute && r.DatabaseId == databaseId, ct);
        if (!hasRole)
        {
            return TypedResults.Forbid();
        }

        try
        {
            var connectionString = await connectionFactory.GetConnectionStringAsync(databaseId, CredentialKind.Read, ct);
            var ddl = await targetEngine.ExportSchemaDdlAsync(connectionString, ct);

            var bytes = Encoding.UTF8.GetBytes(ddl);
            var fileName = $"{SanitizeFileName(database.DisplayName)}-schema-{DateTime.UtcNow:yyyyMMdd-HHmmss}.sql";
            return TypedResults.File(bytes, "application/sql", fileName);
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.BadRequest(ex.Message);
        }
    }

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "schema";
        }

        var invalid = Path.GetInvalidFileNameChars();
        return new string(name
            .Select(c => invalid.Contains(c) || char.IsWhiteSpace(c) ? '-' : c)
            .ToArray());
    }
}