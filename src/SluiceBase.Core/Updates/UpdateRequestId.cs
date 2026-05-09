using Vogen;

namespace SluiceBase.Core.Updates;

[ValueObject<Guid>(conversions: Conversions.SystemTextJson, customizations: Customizations.AddFactoryMethodForGuids)]
public readonly partial struct UpdateRequestId;