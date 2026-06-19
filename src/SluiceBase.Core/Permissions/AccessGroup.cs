using SluiceBase.Core.Users;

namespace SluiceBase.Core.Permissions;

public sealed class AccessGroup
{
#pragma warning disable CS8618
    private AccessGroup() { }
#pragma warning restore CS8618

    private AccessGroup(
        AccessGroupId id, string name, string? description,
        UserId? createdById, DateTimeOffset createdAt)
    {
        Id = id;
        Name = name;
        Description = description;
        CreatedById = createdById;
        CreatedAt = createdAt;
    }

    public AccessGroupId Id { get; private set; }
    public string Name { get; private set; }
    public string? Description { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public UserId? CreatedById { get; private set; }

    public static AccessGroup Create(string name, string? description, UserId? createdById, DateTimeOffset at)
    {
        var trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0)
        {
            throw new ArgumentException("Group name is required.", nameof(name));
        }
        return new AccessGroup(AccessGroupId.FromNewVersion7Guid(), trimmed, Normalize(description), createdById, at);
    }

    public void Rename(string name)
    {
        var trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0)
        {
            throw new ArgumentException("Group name is required.", nameof(name));
        }
        Name = trimmed;
    }

    public void SetDescription(string? description) => Description = Normalize(description);

    private static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
