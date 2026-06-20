using SluiceBase.Core.Servers;
using SluiceBase.Core.Users;

namespace SluiceBase.Core.Permissions;

public sealed class AccessGroupDatabaseRole
{
#pragma warning disable CS8618
    private AccessGroupDatabaseRole() { }
#pragma warning restore CS8618

    private AccessGroupDatabaseRole(
        AccessGroupDatabaseRoleId id, AccessGroupId groupId, string permission,
        DatabaseId databaseId, UserId? grantedById, DateTimeOffset at)
    {
        Id = id;
        GroupId = groupId;
        Permission = permission;
        DatabaseId = databaseId;
        GrantedById = grantedById;
        GrantedAt = at;
    }

    public AccessGroupDatabaseRoleId Id { get; private set; }
    public AccessGroupId GroupId { get; private set; }
    public string Permission { get; private set; }
    public DatabaseId DatabaseId { get; private set; }
    public DateTimeOffset GrantedAt { get; private set; }
    public UserId? GrantedById { get; private set; }

    public static AccessGroupDatabaseRole Grant(
        AccessGroupId groupId, string permission, DatabaseId databaseId, UserId? grantedById, DateTimeOffset at) =>
        new(AccessGroupDatabaseRoleId.FromNewVersion7Guid(), groupId, permission, databaseId, grantedById, at);
}
