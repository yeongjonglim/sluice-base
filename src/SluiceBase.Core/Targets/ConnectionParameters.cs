namespace SluiceBase.Core.Targets;

// Engine-neutral inputs for building a connection string. Mongo-specific options
// (connection mode, authSource, replica set, TLS) are added in a later phase.
public sealed record ConnectionParameters(
    string Host,
    int Port,
    string Database,
    string Username,
    string Password);
