namespace SluiceBase.Core.Servers;

public sealed class Server
{
#pragma warning disable CS8618
    private Server() { }
#pragma warning restore CS8618

    public ServerId Id { get; private set; }
    public string Name { get; private set; }
    public string Kind { get; private set; }
    public string Host { get; private set; }
    public int Port { get; private set; }
    public bool IsDisabled { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IList<Credential> Credentials { get; private set; } = [];
    public IList<Database> Databases { get; private set; } = [];

    public static Server Create(string name, string kind, string host, int port, DateTimeOffset at) =>
        new()
        {
            Id = ServerId.FromNewVersion7Guid(),
            Name = name,
            Kind = kind,
            Host = host,
            Port = port,
            IsDisabled = false,
            CreatedAt = at,
            UpdatedAt = at,
        };

    public void Update(string name, string host, int port, string kind, bool isDisabled, DateTimeOffset at)
    {
        Name = name;
        Host = host;
        Port = port;
        Kind = kind;
        IsDisabled = isDisabled;
        UpdatedAt = at;
    }

    public void SoftDelete(DateTimeOffset at)
    {
        DeletedAt = at;
        UpdatedAt = at;
        foreach (var c in Credentials)
        {
            c.SoftDelete(at);
        }

        foreach (var d in Databases)
        {
            d.SoftDelete(at);
        }
    }
}