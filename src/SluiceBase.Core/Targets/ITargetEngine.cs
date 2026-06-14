using SluiceBase.Core.Queries;
using SluiceBase.Core.Schemas;

namespace SluiceBase.Core.Targets;

public interface ITargetEngine
{
    string Kind { get; }

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

    Task<int> ExecuteUpdateAsync(
        string connectionString,
        string sql,
        CancellationToken ct);
}

public sealed record ConnectivityResult(bool Ok, string? Error);