using SluiceBase.Core.Users;

namespace SluiceBase.Core.Permissions;

public sealed class GroupColumnBypass
{
#pragma warning disable CS8618
    private GroupColumnBypass() { }
#pragma warning restore CS8618

    private GroupColumnBypass(
        GroupColumnBypassId id, GroupId groupId,
        SensitiveColumnId sensitiveColumnId,
        UserId? grantedById, DateTimeOffset at)
    {
        Id = id;
        GroupId = groupId;
        SensitiveColumnId = sensitiveColumnId;
        GrantedById = grantedById;
        GrantedAt = at;
    }

    public GroupColumnBypassId Id { get; private set; }
    public GroupId GroupId { get; private set; }
    public SensitiveColumnId SensitiveColumnId { get; private set; }
    public DateTimeOffset GrantedAt { get; private set; }
    public UserId? GrantedById { get; private set; }

    public static GroupColumnBypass Grant(
        GroupId groupId, SensitiveColumnId sensitiveColumnId,
        UserId? grantedById, DateTimeOffset at) =>
        new(GroupColumnBypassId.FromNewVersion7Guid(), groupId, sensitiveColumnId, grantedById, at);
}
