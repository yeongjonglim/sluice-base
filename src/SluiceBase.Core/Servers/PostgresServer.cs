namespace SluiceBase.Core.Servers;

public sealed class PostgresServer : Server
{
    private PostgresServer() { }

    public override string Kind => "postgres";

    public static PostgresServer Create(string name, string host, int port, DateTimeOffset at) =>
        new()
        {
            Id = ServerId.FromNewVersion7Guid(),
            Name = name,
            Host = host,
            Port = port,
            IsDisabled = false,
            CreatedAt = at,
            UpdatedAt = at,
        };
}
