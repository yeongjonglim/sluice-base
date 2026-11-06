using SluiceBase.Core.Mcp;
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
[OpenApiMarker<McpGrantId>]
// Used as a marker to generate OpenApi schema
// ReSharper disable once UnusedType.Global
internal class OpenApiTransformers;