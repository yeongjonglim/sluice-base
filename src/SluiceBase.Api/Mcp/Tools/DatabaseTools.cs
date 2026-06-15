using System.ComponentModel;
using ModelContextProtocol.Server;
using SluiceBase.Api.Auth;
using SluiceBase.Api.Services;
using SluiceBase.Core.Queries;
using SluiceBase.Core.Servers;

namespace SluiceBase.Api.Mcp.Tools;

[McpServerToolType]
internal sealed class DatabaseTools
{
    [McpServerTool(Name = "list_databases")]
    [Description("List databases the authenticated user can query, grouped by server.")]
    public static async Task<object> ListDatabases(
        ICatalogService catalog, ICurrentUserAccessor currentUser, CancellationToken ct)
    {
        var user = await currentUser.GetAsync(ct)
            ?? throw new InvalidOperationException("No authenticated user.");
        var result = await catalog.ListAccessibleAsync(user, ct);
        return result;
    }

    [McpServerTool(Name = "get_schema")]
    [Description("Return the table/column schema for a database the user can query. Sensitive columns are flagged.")]
    public static async Task<object> GetSchema(
        [Description("The database id (GUID) from list_databases.")] string databaseId,
        ISchemaService schema, ICurrentUserAccessor currentUser, CancellationToken ct)
    {
        var user = await currentUser.GetAsync(ct) ?? throw new InvalidOperationException("No authenticated user.");
        if (!Guid.TryParse(databaseId, out var g)) { throw new ArgumentException("databaseId must be a GUID."); }
        var result = await schema.GetAnnotatedSchemaAsync(user, DatabaseId.From(g), ct);
        return result.Outcome switch
        {
            SchemaOutcome.Ok => result.Tree!,
            SchemaOutcome.NotFound => throw new InvalidOperationException("Database not found."),
            SchemaOutcome.Forbidden => throw new InvalidOperationException("You do not have query access to this database."),
            _ => throw new InvalidOperationException(result.Error ?? "Schema error."),
        };
    }

    [McpServerTool(Name = "run_query")]
    [Description("Execute a read-only SQL query against a database the user can query. Returns columns and rows.")]
    public static async Task<object> RunQuery(
        [Description("The database id (GUID) from list_databases.")] string databaseId,
        [Description("A read-only SQL statement.")] string sql,
        IQueryService queries, ICurrentUserAccessor currentUser, CancellationToken ct)
    {
        var user = await currentUser.GetAsync(ct) ?? throw new InvalidOperationException("No authenticated user.");
        if (!Guid.TryParse(databaseId, out var g)) { throw new ArgumentException("databaseId must be a GUID."); }
        var result = await queries.ExecuteAsync(user, DatabaseId.From(g), sql, QuerySource.Mcp, ct);
        return result.Outcome switch
        {
            QueryOutcome.Ok => result.Response!,
            QueryOutcome.NotFound => throw new InvalidOperationException("Database not found."),
            QueryOutcome.Forbidden => throw new InvalidOperationException("You do not have query access to this database."),
            QueryOutcome.Blocked => throw new InvalidOperationException(
                "Query touches sensitive columns: " + string.Join(", ",
                    result.BlockedColumns!.Select(c => $"{c.Schema}.{c.Table}.{c.Column}"))),
            _ => throw new InvalidOperationException(result.Error ?? "Query error."),
        };
    }
}
