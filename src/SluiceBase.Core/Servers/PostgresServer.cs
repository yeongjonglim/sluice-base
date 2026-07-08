namespace SluiceBase.Core.Servers;

public sealed class PostgresServer : Server
{
    private PostgresServer() { }

    private PostgresServer(string name, string host, int port, DateTimeOffset at)
        : base(name, host, port, at)
    {
    }

    public override string Kind => "postgres";

    public static PostgresServer Create(string name, string host, int port, DateTimeOffset at) =>
        new(name, host, port, at);
}
