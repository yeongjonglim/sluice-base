using SluiceBase.Core.Mcp;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Queries;
using SluiceBase.Core.Servers;
using SluiceBase.Core.Updates;
using SluiceBase.Core.Users;
using Vogen;

namespace SluiceBase.Api.Data.Converters;

[EfCoreConverter<UserId>]
[EfCoreConverter<ExternalLoginId>]
[EfCoreConverter<UserPermissionId>]
[EfCoreConverter<UserDatabaseRoleId>]
[EfCoreConverter<AccessGroupId>]
[EfCoreConverter<AccessGroupMemberId>]
[EfCoreConverter<AccessGroupPermissionId>]
[EfCoreConverter<AccessGroupDatabaseRoleId>]
[EfCoreConverter<SensitiveColumnId>]
[EfCoreConverter<UserColumnBypassId>]
[EfCoreConverter<ServerId>]
[EfCoreConverter<DatabaseId>]
[EfCoreConverter<CredentialId>]
[EfCoreConverter<QueryLogId>]
[EfCoreConverter<UpdateRequestId>]
[EfCoreConverter<McpOAuthClientId>]
[EfCoreConverter<McpAuthCodeId>]
[EfCoreConverter<McpTokenId>]
internal partial class VogenEfCoreConverters;