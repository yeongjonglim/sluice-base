using SluiceBase.Core.Users;

namespace SluiceBase.Core.Permissions;

public sealed class UserPermissionMap
{
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
    private UserPermissionMap() { }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

    private UserPermissionMap(
        UserPermissionId id, UserId userId, string permission,
        UserId? grantedById, DateTimeOffset at)
    {
        Id = id;
        UserId = userId;
        Permission = permission;
        GrantedById = grantedById;
        GrantedAt = at;
    }

    public UserPermissionId Id { get; private set; }
    public UserId UserId { get; private set; }
    public string Permission { get; private set; }
    public DateTimeOffset GrantedAt { get; private set; }
    public UserId? GrantedById { get; private set; }

    public static UserPermissionMap Grant(
        UserId userId, string permission, UserId? grantedById, DateTimeOffset at) =>
        new(UserPermissionId.FromNewVersion7Guid(), userId, permission, grantedById, at);
}