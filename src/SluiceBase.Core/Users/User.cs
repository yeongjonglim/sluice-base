using SluiceBase.Core.Permissions;

namespace SluiceBase.Core.Users;

public sealed class User
{
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
    private User() { }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

    private User(UserId id, string sub, string email, string? name, DateTimeOffset lastLoginAt)
    {
        Id = id;
        Sub = sub;
        Email = email;
        Name = name;
        LastLoginAt = lastLoginAt;
    }

    public UserId Id { get; private set; }
    public string Sub { get; private set; }
    public string Email { get; private set; }
    public string? Name { get; private set; }
    public DateTimeOffset? LastLoginAt { get; private set; }
    public IReadOnlyList<UserPermissionMap> Permissions { get; init; } = [];

    public static User Create(string sub, string email, string? name, DateTimeOffset at) =>
        new(UserId.FromNewVersion7Guid(), sub, email, name, at);

    public void RecordLogin(string email, string? name, DateTimeOffset at)
    {
        Email = email;
        Name = name;
        LastLoginAt = at;
    }

    public bool HasPermission(string permission) =>
        Permissions.Any(p => p.Permission == permission);
}