using Microsoft.EntityFrameworkCore;
using SluiceBase.Api.Auth;
using SluiceBase.Api.Data;
using SluiceBase.Api.Servers;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Schemas;
using SluiceBase.Core.Servers;
using SluiceBase.Core.Targets;
using SluiceBase.Core.Users;

namespace SluiceBase.Api.Services;

internal enum SchemaOutcome { Ok, NotFound, Forbidden, Error }

internal sealed record SchemaResult(SchemaOutcome Outcome, SchemaTree? Tree, string? Error);

internal interface ISchemaService
{
    Task<SchemaResult> GetAnnotatedSchemaAsync(User user, DatabaseId databaseId, CancellationToken ct);
}

internal sealed class SchemaService(
    AppDbContext db,
    IServerConnectionFactory connectionFactory,
    ITargetEngineRegistry engineRegistry,
    IAccessResolver resolver) : ISchemaService
{
    public async Task<SchemaResult> GetAnnotatedSchemaAsync(User user, DatabaseId databaseId, CancellationToken ct)
    {
        var database = await db.Databases.AsNoTracking()
            .Include(d => d.Server)
            .SingleOrDefaultAsync(d => d.Id == databaseId, ct);
        if (database is null)
        {
            return new SchemaResult(SchemaOutcome.NotFound, null, null);
        }

        var hasRole = await resolver.HasDatabasePermissionAsync(user.Id, databaseId, Permissions.QueryExecute, ct);
        if (!hasRole)
        {
            return new SchemaResult(SchemaOutcome.Forbidden, null, null);
        }

        try
        {
            var connectionString = await connectionFactory.GetConnectionStringAsync(databaseId, CredentialKind.Read, ct);
            var targetEngine = engineRegistry.Resolve(database.Server!.Kind);
            var tree = await targetEngine.GetSchemaAsync(connectionString, ct);

            var sensitiveColumns = await db.SensitiveColumns
                .AsNoTracking()
                .Where(c => c.DatabaseId == databaseId)
                .ToListAsync(ct);

            if (sensitiveColumns.Count == 0)
            {
                return new SchemaResult(SchemaOutcome.Ok, tree, null);
            }

            var sensitiveColumnIds = sensitiveColumns.Select(c => c.Id).ToList();
            var bypassedIds = await db.UserColumnBypasses
                .AsNoTracking()
                .Where(b => b.UserId == user.Id && sensitiveColumnIds.Contains(b.SensitiveColumnId))
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
                            }).ToList(),
                            t.PrimaryKey,
                            t.ForeignKeys,
                            t.Indexes
                        )).ToList(),
                    s.Views,
                    s.MaterializedViews,
                    s.Routines,
                    s.Sequences,
                    s.Types
                )).ToList();

            return new SchemaResult(SchemaOutcome.Ok, new SchemaTree(annotatedSchemas, tree.Extensions), null);
        }
        catch (InvalidOperationException ex)
        {
            return new SchemaResult(SchemaOutcome.Error, null, ex.Message);
        }
    }
}
