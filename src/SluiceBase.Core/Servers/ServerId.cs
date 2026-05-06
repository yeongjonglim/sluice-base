using Vogen;

namespace SluiceBase.Core.Servers;

[ValueObject<Guid>(conversions: Conversions.SystemTextJson, customizations: Customizations.AddFactoryMethodForGuids)]
public readonly partial struct ServerId;