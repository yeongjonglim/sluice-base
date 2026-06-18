using Vogen;

namespace SluiceBase.Core.Mcp;

[ValueObject<Guid>(conversions: Conversions.SystemTextJson, customizations: Customizations.AddFactoryMethodForGuids)]
public readonly partial struct McpOAuthClientId;
