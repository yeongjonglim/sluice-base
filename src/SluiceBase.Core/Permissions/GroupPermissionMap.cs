using SluiceBase.Core.Users;

namespace SluiceBase.Core.Permissions;

public sealed class GroupPermissionMap
{
#pragma warning disable CS8618
    private GroupPermissionMap() { }
#pragma warning restore CS8618

    private GroupPermissionMap(
        GroupPermissionId id, GroupId groupId, string permission,
        UserId? grantedById, DateTimeOffset at)
    {
        Id = id;
        GroupId = groupId;
        Permission = permission;
        GrantedById = grantedById;
        GrantedAt = at;
    }

    public GroupPermissionId Id { get; private set; }
    public GroupId GroupId { get; private set; }
    public string Permission { get; private set; }
    public DateTimeOffset GrantedAt { get; private set; }
    public UserId? GrantedById { get; private set; }

    public static GroupPermissionMap Grant(
        GroupId groupId, string permission,
        UserId? grantedById, DateTimeOffset at) =>
        new(GroupPermissionId.FromNewVersion7Guid(), groupId, permission, grantedById, at);
}
