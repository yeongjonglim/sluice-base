using SluiceBase.Core.Servers;
using SluiceBase.Core.Updates;
using SluiceBase.Core.Users;
using Vogen;

namespace SluiceBase.Api.Extensions;

[OpenApiMarker<UserId>]
[OpenApiMarker<ServerId>]
[OpenApiMarker<UpdateRequestId>]
// Used as a marker to generate OpenApi schema
// ReSharper disable once UnusedType.Global
internal class OpenApiTransformers;