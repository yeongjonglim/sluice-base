using SluiceBase.Core.Users;

namespace SluiceBase.Core.Permissions;

public sealed class GroupMember
{
#pragma warning disable CS8618
    private GroupMember() { }
#pragma warning restore CS8618

    private GroupMember(
        GroupMemberId id, GroupId groupId, UserId userId,
        UserId? addedById, DateTimeOffset at)
    {
        Id = id;
        GroupId = groupId;
        UserId = userId;
        AddedById = addedById;
        AddedAt = at;
    }

    public GroupMemberId Id { get; private set; }
    public GroupId GroupId { get; private set; }
    public UserId UserId { get; private set; }
    public DateTimeOffset AddedAt { get; private set; }
    public UserId? AddedById { get; private set; }

    public static GroupMember Add(
        GroupId groupId, UserId userId,
        UserId? addedById, DateTimeOffset at) =>
        new(GroupMemberId.FromNewVersion7Guid(), groupId, userId, addedById, at);
}
