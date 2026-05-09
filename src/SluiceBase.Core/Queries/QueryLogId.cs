using Vogen;

namespace SluiceBase.Core.Queries;

[ValueObject<Guid>(conversions: Conversions.SystemTextJson, customizations: Customizations.AddFactoryMethodForGuids)]
public readonly partial struct QueryLogId;