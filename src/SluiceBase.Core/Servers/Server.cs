// src/SluiceBase.Core/Servers/Server.cs

namespace SluiceBase.Core.Servers;

public sealed class Server
{
#pragma warning disable CS8618
    private Server()
    {
    }
#pragma warning restore CS8618

    private Server(ServerId id,
        string name,
        string kind,
        string host,
        int port,
        string database,
        string readUsername,
        string encryptedReadPassword,
        string? writeUsername,
        string? encryptedWritePassword,
        DateTimeOffset at)
    {
        Id = id;
        Name = name;
        Kind = kind;
        Host = host;
        Port = port;
        Database = database;
        ReadUsername = readUsername;
        EncryptedReadPassword = encryptedReadPassword;
        WriteUsername = writeUsername;
        EncryptedWritePassword = encryptedWritePassword;
        IsEnabled = true;
        CreatedAt = at;
        UpdatedAt = at;
    }

    public ServerId Id { get; private set; }
    public string Name { get; private set; }
    public string Kind { get; private set; }
    public string Host { get; private set; }
    public int Port { get; private set; }
    public string Database { get; private set; }
    public string ReadUsername { get; private set; }
    public string EncryptedReadPassword { get; private set; }
    public string? WriteUsername { get; private set; }
    public string? EncryptedWritePassword { get; private set; }
    public bool IsEnabled { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public bool HasWriteCredential => !string.IsNullOrEmpty(WriteUsername) && !string.IsNullOrEmpty(EncryptedWritePassword);

    public static Server Create(string name,
        string kind,
        string host,
        int port,
        string database,
        string readUsername,
        string encryptedReadPassword,
        string? writeUsername,
        string? encryptedWritePassword,
        DateTimeOffset at) =>
        new(ServerId.FromNewVersion7Guid(),
            name,
            kind,
            host,
            port,
            database,
            readUsername,
            encryptedReadPassword,
            writeUsername,
            encryptedWritePassword,
            at);

    public void Update(string name,
        string host,
        int port,
        string database,
        string readUsername,
        DateTimeOffset at)
    {
        Name = name;
        Host = host;
        Port = port;
        Database = database;
        ReadUsername = readUsername;
        UpdatedAt = at;
    }

    public void ReplaceReadPassword(string encryptedPassword, DateTimeOffset at)
    {
        EncryptedReadPassword = encryptedPassword;
        UpdatedAt = at;
    }

    public void SetWriteCredential(string username, string encryptedPassword, DateTimeOffset at)
    {
        WriteUsername = username;
        EncryptedWritePassword = encryptedPassword;
        UpdatedAt = at;
    }

    public void ClearWriteCredential(DateTimeOffset at)
    {
        WriteUsername = null;
        EncryptedWritePassword = null;
        UpdatedAt = at;
    }
}