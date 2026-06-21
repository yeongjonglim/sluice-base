using SluiceBase.Core.Users;

namespace SluiceBase.Core.Permissions;

#pragma warning disable CA1711
public sealed class AccessGroupPermission
#pragma warning restore CA1711
{
#pragma warning disable CS8618
    private AccessGroupPermission() { }
#pragma warning restore CS8618

    private AccessGroupPermission(
        AccessGroupPermissionId id, AccessGroupId groupId, string permission,
        UserId? grantedById, DateTimeOffset at)
    {
        Id = id;
        GroupId = groupId;
        Permission = permission;
        GrantedById = grantedById;
        GrantedAt = at;
    }

    public AccessGroupPermissionId Id { get; private set; }
    public AccessGroupId GroupId { get; private set; }
    public string Permission { get; private set; }
    public DateTimeOffset GrantedAt { get; private set; }
    public UserId? GrantedById { get; private set; }

    public static AccessGroupPermission Grant(AccessGroupId groupId, string permission, UserId? grantedById, DateTimeOffset at)
    {
        if (string.IsNullOrWhiteSpace(permission))
        {
            throw new ArgumentException("Permission is required.", nameof(permission));
        }
        return new(AccessGroupPermissionId.FromNewVersion7Guid(), groupId, permission, grantedById, at);
    }
}
