using SluiceBase.Core.Users;

namespace SluiceBase.Core.Permissions;

public sealed class UserPermissionMap
{
    private UserPermissionMap() { }

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
    public string Permission { get; private set; } = "";
    public DateTimeOffset GrantedAt { get; private set; }
    public UserId? GrantedById { get; private set; }

    public static UserPermissionMap Grant(
        UserId userId, string permission, UserId? grantedById, DateTimeOffset at) =>
        new(UserPermissionId.From(Guid.NewGuid()), userId, permission, grantedById, at);
}