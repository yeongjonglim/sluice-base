namespace SluiceBase.Core.Servers;

public sealed class MongoServer : Server
{
    private MongoServer() { }

    private MongoServer(
        string name,
        string host,
        int port,
        DateTimeOffset at,
        ConnectionMode connectionMode,
        string? authSource,
        string? replicaSet,
        bool useTls)
        : base(name, host, port, at)
    {
        ConnectionMode = connectionMode;
        AuthSource = authSource;
        ReplicaSet = replicaSet;
        UseTls = useTls;
    }

    public override string Kind => "mongodb";

    public ConnectionMode ConnectionMode { get; private set; }
    public string? AuthSource { get; private set; }
    public string? ReplicaSet { get; private set; }
    public bool UseTls { get; private set; }

    public static MongoServer Create(
        string name,
        string host,
        int port,
        DateTimeOffset at,
        ConnectionMode connectionMode,
        string? authSource,
        string? replicaSet,
        bool useTls) =>
        new(name, host, port, at, connectionMode, authSource, replicaSet, useTls);

    public void UpdateMongo(ConnectionMode connectionMode, string? authSource, string? replicaSet, bool useTls)
    {
        ConnectionMode = connectionMode;
        AuthSource = authSource;
        ReplicaSet = replicaSet;
        UseTls = useTls;
    }
}
