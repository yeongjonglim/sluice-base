using SluiceBase.Core.Permissions;
using SluiceBase.Core.Users;
using Vogen;

namespace SluiceBase.Api.Data.Converters;

[EfCoreConverter<UserId>]
[EfCoreConverter<UserPermissionId>]
internal partial class VogenEfCoreConverters;