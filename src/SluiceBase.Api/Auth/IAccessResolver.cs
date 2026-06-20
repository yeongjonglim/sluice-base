using SluiceBase.Core.Servers;
using SluiceBase.Core.Users;

namespace SluiceBase.Api.Auth;

internal interface IAccessResolver
{
    Task<bool> HasGlobalPermissionAsync(UserId user, string permission, CancellationToken ct);
    Task<bool> HasDatabasePermissionAsync(UserId user, DatabaseId db, string permission, CancellationToken ct);
    Task<IReadOnlySet<DatabaseId>> DatabasesWithPermissionAsync(UserId user, string permission, CancellationToken ct);
    Task<IReadOnlySet<DatabaseId>> DatabasesWithAnyScopeableAsync(UserId user, CancellationToken ct);
    Task<IReadOnlySet<string>> EffectivePermissionsAsync(UserId user, CancellationToken ct);
}
