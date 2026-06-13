using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SluiceBase.Api.Auth;
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
            .RequireAuthorization()
            .WithName("GetSchema");
    }

    private static async Task<Results<Ok<SchemaTree>, NotFound, BadRequest<string>, ForbidHttpResult>> GetSchema(
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
            var tree = await targetEngine.GetSchemaAsync(connectionString, ct);

            var sensitiveColumns = await db.SensitiveColumns
                .AsNoTracking()
                .Where(c => c.DatabaseId == databaseId)
                .ToListAsync(ct);

            if (sensitiveColumns.Count == 0)
            {
                return TypedResults.Ok(tree);
            }

            var sensitiveColumnIds = sensitiveColumns.Select(c => c.Id).ToList();
            var bypassedIds = await db.UserColumnBypasses
                .AsNoTracking()
                .Where(b => b.UserId == user!.Id && sensitiveColumnIds.Contains(b.SensitiveColumnId))
                .Select(b => b.SensitiveColumnId)
                .ToListAsync(ct);

            var sensitiveKeys = sensitiveColumns
                .Select(c => (c.SchemaName.ToLowerInvariant(), c.TableName.ToLowerInvariant(), c.ColumnName.ToLowerInvariant()))
                .ToHashSet();

            var restrictedKeys = sensitiveColumns
                .Where(c => !bypassedIds.Contains(c.Id))
                .Select(c => (c.SchemaName.ToLowerInvariant(), c.TableName.ToLowerInvariant(), c.ColumnName.ToLowerInvariant()))
                .ToHashSet();

            var annotatedSchemas = tree.Schemas.Select(s =>
                new SchemaInfo(s.Name,
                    s.Tables.Select(t =>
                        new TableInfo(t.Name,
                            t.Columns.Select(c =>
                            {
                                var key = (s.Name.ToLowerInvariant(), t.Name.ToLowerInvariant(), c.Name.ToLowerInvariant());
                                return new ColumnInfo(
                                    c.Name, c.DataType, c.IsNullable,
                                    sensitiveKeys.Contains(key),
                                    restrictedKeys.Contains(key));
                            }).ToList()
                        )).ToList()
                )).ToList();

            return TypedResults.Ok(new SchemaTree(annotatedSchemas, tree.PrimaryKeys, tree.ForeignKeys));
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.BadRequest(ex.Message);
        }
    }
}