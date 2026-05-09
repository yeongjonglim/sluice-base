using SluiceBase.Core.Permissions;
using SluiceBase.Core.Queries;
using SluiceBase.Core.Servers;
using SluiceBase.Core.Users;
using Vogen;

namespace SluiceBase.Api.Data.Converters;

[EfCoreConverter<UserId>]
[EfCoreConverter<UserPermissionId>]
[EfCoreConverter<ServerId>]
[EfCoreConverter<QueryLogId>]
internal partial class VogenEfCoreConverters;