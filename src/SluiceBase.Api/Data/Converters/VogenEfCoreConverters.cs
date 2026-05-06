using SluiceBase.Core.Permissions;
using SluiceBase.Core.Servers;
using SluiceBase.Core.Users;
using Vogen;

namespace SluiceBase.Api.Data.Converters;

[EfCoreConverter<UserId>]
[EfCoreConverter<UserPermissionId>]
[EfCoreConverter<ServerId>]
internal partial class VogenEfCoreConverters;