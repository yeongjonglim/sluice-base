using SluiceBase.Core.Users;

namespace SluiceBase.Core.Permissions;

public sealed class UserColumnBypass
{
#pragma warning disable CS8618
    private UserColumnBypass() { }
#pragma warning restore CS8618

    private UserColumnBypass(
        UserColumnBypassId id, UserId userId,
        SensitiveColumnId sensitiveColumnId,
        UserId? grantedById, DateTimeOffset at)
    {
        Id = id;
        UserId = userId;
        SensitiveColumnId = sensitiveColumnId;
        GrantedById = grantedById;
        GrantedAt = at;
    }

    public UserColumnBypassId Id { get; private set; }
    public UserId UserId { get; private set; }
    public SensitiveColumnId SensitiveColumnId { get; private set; }
    public DateTimeOffset GrantedAt { get; private set; }
    public UserId? GrantedById { get; private set; }

    public static UserColumnBypass Grant(
        UserId userId, SensitiveColumnId sensitiveColumnId,
        UserId? grantedById, DateTimeOffset at) =>
        new(UserColumnBypassId.FromNewVersion7Guid(), userId, sensitiveColumnId, grantedById, at);
}
