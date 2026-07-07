namespace SluiceBase.Core.Servers;

public sealed class MongoServer : Server
{
    private MongoServer() { }

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
        new()
        {
            Id = ServerId.FromNewVersion7Guid(),
            Name = name,
            Host = host,
            Port = port,
            IsDisabled = false,
            CreatedAt = at,
            UpdatedAt = at,
            ConnectionMode = connectionMode,
            AuthSource = authSource,
            ReplicaSet = replicaSet,
            UseTls = useTls,
        };

    public void UpdateMongo(ConnectionMode connectionMode, string? authSource, string? replicaSet, bool useTls)
    {
        ConnectionMode = connectionMode;
        AuthSource = authSource;
        ReplicaSet = replicaSet;
        UseTls = useTls;
    }
}
