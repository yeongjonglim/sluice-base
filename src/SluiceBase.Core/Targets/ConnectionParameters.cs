using SluiceBase.Core.Servers;

namespace SluiceBase.Core.Targets;

// Engine-neutral inputs for building a connection string. The core fields are common to
// every engine; engine-specific settings ride along as a typed extension (Options) that
// each engine owns and pattern-matches. This keeps the core clean and lets a new engine
// add its own options type without touching this record.
public sealed record ConnectionParameters(
    string Host,
    int Port,
    string Database,
    string Username,
    string Password,
    IConnectionOptions? Options = null);

// Marker for an engine-specific connection options extension.
public interface IConnectionOptions;

// MongoDB-specific connection options (see MongoTargetEngine.BuildConnectionString).
public sealed record MongoConnectionOptions(
    ConnectionMode Mode,
    string? AuthSource,
    string? ReplicaSet,
    bool UseTls) : IConnectionOptions;
