using SluiceBase.Core.Servers;

namespace SluiceBase.Core.Targets;

// Engine-neutral inputs for building a connection string. The first five fields are
// common to all engines; the trailing options are Mongo-specific and default so that
// PostgreSQL callers are unaffected.
public sealed record ConnectionParameters(
    string Host,
    int Port,
    string Database,
    string Username,
    string Password,
    ConnectionMode Mode = ConnectionMode.Standard,
    string? AuthSource = null,
    string? ReplicaSet = null,
    bool UseTls = false);
