using MongoDB.Bson;
using MongoDB.Driver;
using SluiceBase.Core.Queries;
using SluiceBase.Core.Schemas;
using SluiceBase.Core.Servers;
using SluiceBase.Core.Targets;

namespace SluiceBase.Api.Targets;

internal sealed class MongoTargetEngine : ITargetEngine
{
    public string Kind => "mongodb";

    public string BuildConnectionString(ConnectionParameters p)
    {
        var scheme = p.Mode == ConnectionMode.Srv ? "mongodb+srv" : "mongodb";
        var user = Uri.EscapeDataString(p.Username);
        var pass = Uri.EscapeDataString(p.Password);
        // SRV derives the host list (and ports) from DNS, so the port is not emitted.
        var hostPart = p.Mode == ConnectionMode.Srv ? p.Host : $"{p.Host}:{p.Port}";
        var db = Uri.EscapeDataString(p.Database);

        var options = new List<string>();
        if (!string.IsNullOrWhiteSpace(p.AuthSource))
        {
            options.Add($"authSource={Uri.EscapeDataString(p.AuthSource)}");
        }

        if (!string.IsNullOrWhiteSpace(p.ReplicaSet))
        {
            options.Add($"replicaSet={Uri.EscapeDataString(p.ReplicaSet)}");
        }

        if (p.UseTls)
        {
            options.Add("tls=true");
        }

        var query = options.Count > 0 ? "?" + string.Join("&", options) : string.Empty;
        return $"{scheme}://{user}:{pass}@{hostPart}/{db}{query}";
    }

    public async Task<ConnectivityResult> TestConnectionAsync(string connectionString, CancellationToken ct)
    {
        try
        {
            var settings = MongoClientSettings.FromConnectionString(connectionString);
            settings.ServerSelectionTimeout = TimeSpan.FromSeconds(5);
            var client = new MongoClient(settings);
            await client.GetDatabase("admin")
                .RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1), cancellationToken: ct)
                .ConfigureAwait(false);
            return new ConnectivityResult(true, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ConnectivityResult(false, ex.Message);
        }
    }

    public Task<SchemaTree> GetSchemaAsync(string connectionString, CancellationToken ct) =>
        throw new NotSupportedException("Schema introspection is not yet supported for MongoDB.");

    public Task<string> ExportSchemaDdlAsync(string connectionString, CancellationToken ct) =>
        throw new NotSupportedException("DDL export is not supported for MongoDB.");

    public Task<QueryData> ExecuteQueryAsync(string connectionString, string sql, CancellationToken ct) =>
        throw new NotSupportedException("Query execution is not yet supported for MongoDB.");

    public Task<int> ExecuteUpdateAsync(string connectionString, string sql, CancellationToken ct) =>
        throw new NotSupportedException("Writes are not supported for MongoDB.");
}
