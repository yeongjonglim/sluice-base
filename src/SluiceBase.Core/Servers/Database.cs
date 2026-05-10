namespace SluiceBase.Core.Servers;

public sealed class Database
{
#pragma warning disable CS8618
    private Database()
    {
    }
#pragma warning restore CS8618

    public DatabaseId Id { get; private set; }
    public ServerId ServerId { get; private set; }
    public string DisplayName { get; private set; }
    public string DatabaseName { get; private set; }
    public CredentialId ReadCredentialId { get; private set; }
    public CredentialId? WriteCredentialId { get; private set; }
    public bool IsDisabled { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public bool CanWrite => WriteCredentialId.HasValue;

    // Loaded by EF for the connection factory
    public Server? Server { get; private set; }

    public static Database Create(
        ServerId serverId,
        string displayName,
        string databaseName,
        CredentialId readCredentialId,
        CredentialId? writeCredentialId,
        DateTimeOffset at) =>
        new()
        {
            Id = DatabaseId.FromNewVersion7Guid(),
            ServerId = serverId,
            DisplayName = displayName,
            DatabaseName = databaseName,
            ReadCredentialId = readCredentialId,
            WriteCredentialId = writeCredentialId,
            IsDisabled = false,
            CreatedAt = at,
            UpdatedAt = at,
        };

    public void Update(
        string displayName,
        string databaseName,
        CredentialId readCredentialId,
        CredentialId? writeCredentialId,
        bool isDisabled,
        DateTimeOffset at)
    {
        DisplayName = displayName;
        DatabaseName = databaseName;
        ReadCredentialId = readCredentialId;
        WriteCredentialId = writeCredentialId;
        IsDisabled = isDisabled;
        UpdatedAt = at;
    }

    public void SoftDelete(DateTimeOffset at)
    {
        DeletedAt = at;
        UpdatedAt = at;
    }
}