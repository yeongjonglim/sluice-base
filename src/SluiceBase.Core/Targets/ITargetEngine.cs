using SluiceBase.Core.Queries;
using SluiceBase.Core.Schemas;

namespace SluiceBase.Core.Targets;

public interface ITargetEngine
{
    string Kind { get; }

    string BuildConnectionString(ConnectionParameters parameters);

    Task<ConnectivityResult> TestConnectionAsync(
        string connectionString,
        CancellationToken ct);

    Task<SchemaTree> GetSchemaAsync(
        string connectionString,
        CancellationToken ct);

    Task<string> ExportSchemaDdlAsync(
        string connectionString,
        CancellationToken ct);

    Task<QueryData> ExecuteQueryAsync(
        string connectionString,
        string sql,
        CancellationToken ct);

    Task<QueryPlan> ExplainAsync(
        string connectionString,
        string sql,
        bool analyze,
        CancellationToken ct);

    Task<UpdateExecutionResult> ExecuteUpdateAsync(
        string connectionString,
        string sql,
        bool commit,
        CancellationToken ct);
}

public sealed record ConnectivityResult(bool Ok, string? Error);

public sealed record UpdateExecutionResult(
    IReadOnlyList<QueryData> ResultSets,
    int AffectedRows);

public sealed record ColumnRef(string Schema, string Table, string Column);