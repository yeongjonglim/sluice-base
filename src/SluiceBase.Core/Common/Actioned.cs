using SluiceBase.Core.Users;

namespace SluiceBase.Core.Common;

public record Actioned(UserId UserId, DateTimeOffset At);