using SluiceBase.Core.Users;

namespace SluiceBase.Core.Permissions;

public sealed class Group
{
#pragma warning disable CS8618
    private Group() { }
#pragma warning restore CS8618

    private Group(
        GroupId id, string name, string? description,
        UserId createdById, DateTimeOffset at)
    {
        Id = id;
        Name = name;
        Description = description;
        CreatedById = createdById;
        CreatedAt = at;
    }

    public GroupId Id { get; private set; }
    public string Name { get; private set; }
    public string? Description { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public UserId CreatedById { get; private set; }

    public static Group Create(
        string name, string? description,
        UserId createdById, DateTimeOffset at) =>
        new(GroupId.FromNewVersion7Guid(), name, description, createdById, at);

    public void Update(string name, string? description)
    {
        Name = name;
        Description = description;
    }
}
