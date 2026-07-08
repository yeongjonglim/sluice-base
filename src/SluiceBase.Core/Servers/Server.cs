namespace SluiceBase.Core.Servers;

// A target database server. Modelled as a strict hierarchy: PostgresServer carries no
// engine-specific settings, MongoServer carries its connection options. Persistence is
// table-per-hierarchy (a single `server` table with a `kind` discriminator).
public abstract class Server
{
#pragma warning disable CS8618
    // Used by EF Core when materialising rows; properties are set from the row.
    protected Server() { }
#pragma warning restore CS8618

    // Shared construction of the properties common to every server kind. Subclass
    // constructors chain to this via `: base(...)` and then set their own fields.
    protected Server(string name, string host, int port, DateTimeOffset at)
    {
        Id = ServerId.FromNewVersion7Guid();
        Name = name;
        Host = host;
        Port = port;
        IsDisabled = false;
        CreatedAt = at;
        UpdatedAt = at;
    }

    public ServerId Id { get; private set; }
    public string Name { get; private set; }
    public string Host { get; private set; }
    public int Port { get; private set; }
    public bool IsDisabled { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IList<Credential> Credentials { get; private set; } = [];
    public IList<Database> Databases { get; private set; } = [];

    // The engine kind, fixed by the concrete server type; also the TPH discriminator value.
    public abstract string Kind { get; }

    // Updates the fields common to every server kind. Kind is immutable — a server cannot
    // change engine after creation.
    public void UpdateCore(string name, string host, int port, bool isDisabled, DateTimeOffset at)
    {
        Name = name;
        Host = host;
        Port = port;
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
