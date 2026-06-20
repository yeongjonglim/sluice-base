using SluiceBase.Core.Permissions;

namespace SluiceBase.Api.Auth;

public sealed record GroupRef(AccessGroupId GroupId, string Name);

#pragma warning disable CA1711
public sealed record EffectivePermission(string Permission, bool FromDirect, IReadOnlyList<GroupRef> FromGroups);
#pragma warning restore CA1711
