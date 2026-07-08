using SluiceBase.Core.Servers;

namespace SluiceBase.Core.Targets;

// Engine-neutral inputs for building a connection string: only the pieces that are truly
// universal — the target database and credentials. Everything endpoint- or engine-specific
// (host(s), port, connection mode, TLS) lives in the engine's own IConnectionOptions, so the
// core makes no assumptions that don't hold for every engine (e.g. a port, which MongoDB SRV
// does not use, or a single host, which a clustered engine would not have).
public sealed record ConnectionParameters(
    string Database,
    string Username,
    string Password,
    IConnectionOptions Options);

// Marker for an engine-specific set of connection settings (endpoint + engine options).
public interface IConnectionOptions;

public sealed record PostgresConnectionOptions(string Host, int Port) : IConnectionOptions;

// MongoDB connection settings (see MongoTargetEngine.BuildConnectionString). Port is null in
// SRV mode — the driver derives the hosts and ports from DNS.
public sealed record MongoConnectionOptions(
    string Host,
    ConnectionMode Mode,
    int? Port,
    string? AuthSource,
    string? ReplicaSet,
    bool UseTls) : IConnectionOptions;
