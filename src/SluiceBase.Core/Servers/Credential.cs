namespace SluiceBase.Core.Servers;

public sealed class Credential
{
#pragma warning disable CS8618
    private Credential()
    {
    }
#pragma warning restore CS8618

    public CredentialId Id { get; private set; }
    public ServerId ServerId { get; private set; }
    public string Label { get; private set; }
    public string Username { get; private set; }
    public string EncryptedPassword { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static Credential Create(
        ServerId serverId,
        string label,
        string username,
        string encryptedPassword,
        DateTimeOffset at) =>
        new()
        {
            Id = CredentialId.FromNewVersion7Guid(),
            ServerId = serverId,
            Label = label,
            Username = username,
            EncryptedPassword = encryptedPassword,
            CreatedAt = at,
            UpdatedAt = at,
        };

    public void Update(string label, string username, string? encryptedPassword, DateTimeOffset at)
    {
        Label = label;
        Username = username;
        if (encryptedPassword is not null)
        {
            EncryptedPassword = encryptedPassword;
        }

        UpdatedAt = at;
    }

    public void SoftDelete(DateTimeOffset at)
    {
        DeletedAt = at;
        UpdatedAt = at;
    }
}