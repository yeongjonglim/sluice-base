using Vogen;

namespace SluiceBase.Core.Permissions;

[ValueObject<Guid>(customizations: Customizations.AddFactoryMethodForGuids)]
public readonly partial struct GroupColumnBypassId;
