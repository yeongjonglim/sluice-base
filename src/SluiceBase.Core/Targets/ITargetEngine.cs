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
}

public sealed record ConnectivityResult(bool Ok, string? Error);