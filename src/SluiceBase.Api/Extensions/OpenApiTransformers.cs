using SluiceBase.Core.Permissions;
using SluiceBase.Core.Servers;
using SluiceBase.Core.Updates;
using SluiceBase.Core.Users;
using Vogen;

namespace SluiceBase.Api.Extensions;

[OpenApiMarker<UserId>]
[OpenApiMarker<ServerId>]
[OpenApiMarker<DatabaseId>]
[OpenApiMarker<CredentialId>] // TODO: Think about this again
[OpenApiMarker<UpdateRequestId>]
[OpenApiMarker<UserDatabaseRoleId>]
[OpenApiMarker<GroupId>]
[OpenApiMarker<GroupMemberId>]
[OpenApiMarker<GroupPermissionId>]
[OpenApiMarker<GroupDatabaseRoleId>]
[OpenApiMarker<GroupColumnBypassId>]
// Used as a marker to generate OpenApi schema
// ReSharper disable once UnusedType.Global
internal class OpenApiTransformers;