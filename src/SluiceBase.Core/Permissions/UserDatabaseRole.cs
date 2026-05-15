using SluiceBase.Core.Servers;
using SluiceBase.Core.Users;

namespace SluiceBase.Core.Permissions;

public sealed class UserDatabaseRole
{
#pragma warning disable CS8618
    private UserDatabaseRole() { }
#pragma warning restore CS8618

    private UserDatabaseRole(
        UserDatabaseRoleId id, UserId userId, string permission,
        DatabaseId databaseId, UserId? grantedById, DateTimeOffset at)
    {
        Id = id;
        UserId = userId;
        Permission = permission;
        DatabaseId = databaseId;
        GrantedById = grantedById;
        GrantedAt = at;
    }

    public UserDatabaseRoleId Id { get; private set; }
    public UserId UserId { get; private set; }
    public string Permission { get; private set; }
    public DatabaseId DatabaseId { get; private set; }
    public DateTimeOffset GrantedAt { get; private set; }
    public UserId? GrantedById { get; private set; }

    public static UserDatabaseRole Grant(
        UserId userId, string permission, DatabaseId databaseId,
        UserId? grantedById, DateTimeOffset at) =>
        new(UserDatabaseRoleId.FromNewVersion7Guid(), userId, permission, databaseId, grantedById, at);
}
