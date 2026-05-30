using SluiceBase.Core.Servers;
using SluiceBase.Core.Users;

namespace SluiceBase.Core.Permissions;

public sealed class GroupDatabaseRole
{
#pragma warning disable CS8618
    private GroupDatabaseRole() { }
#pragma warning restore CS8618

    private GroupDatabaseRole(
        GroupDatabaseRoleId id, GroupId groupId, string permission,
        DatabaseId databaseId, UserId? grantedById, DateTimeOffset at)
    {
        Id = id;
        GroupId = groupId;
        Permission = permission;
        DatabaseId = databaseId;
        GrantedById = grantedById;
        GrantedAt = at;
    }

    public GroupDatabaseRoleId Id { get; private set; }
    public GroupId GroupId { get; private set; }
    public string Permission { get; private set; }
    public DatabaseId DatabaseId { get; private set; }
    public DateTimeOffset GrantedAt { get; private set; }
    public UserId? GrantedById { get; private set; }

    public static GroupDatabaseRole Grant(
        GroupId groupId, string permission, DatabaseId databaseId,
        UserId? grantedById, DateTimeOffset at) =>
        new(GroupDatabaseRoleId.FromNewVersion7Guid(), groupId, permission, databaseId, grantedById, at);
}
