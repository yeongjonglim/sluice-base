using SluiceBase.Core.Users;

namespace SluiceBase.Core.Permissions;

public sealed class AccessGroupMember
{
#pragma warning disable CS8618
    private AccessGroupMember() { }
#pragma warning restore CS8618

    private AccessGroupMember(
        AccessGroupMemberId id, AccessGroupId groupId, UserId userId,
        UserId? addedById, DateTimeOffset at)
    {
        Id = id;
        GroupId = groupId;
        UserId = userId;
        AddedById = addedById;
        AddedAt = at;
    }

    public AccessGroupMemberId Id { get; private set; }
    public AccessGroupId GroupId { get; private set; }
    public UserId UserId { get; private set; }
    public DateTimeOffset AddedAt { get; private set; }
    public UserId? AddedById { get; private set; }

    public static AccessGroupMember Add(AccessGroupId groupId, UserId userId, UserId? addedById, DateTimeOffset at) =>
        new(AccessGroupMemberId.FromNewVersion7Guid(), groupId, userId, addedById, at);
}
