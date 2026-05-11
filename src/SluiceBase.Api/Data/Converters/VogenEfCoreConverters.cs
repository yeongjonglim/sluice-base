using SluiceBase.Core.Permissions;
using SluiceBase.Core.Queries;
using SluiceBase.Core.Servers;
using SluiceBase.Core.Updates;
using SluiceBase.Core.Users;
using Vogen;

namespace SluiceBase.Api.Data.Converters;

[EfCoreConverter<UserId>]
[EfCoreConverter<UserPermissionId>]
[EfCoreConverter<ServerId>]
[EfCoreConverter<DatabaseId>]
[EfCoreConverter<CredentialId>]
[EfCoreConverter<QueryLogId>]
[EfCoreConverter<UpdateRequestId>]
internal partial class VogenEfCoreConverters;