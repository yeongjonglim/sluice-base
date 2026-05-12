using Vogen;

namespace SluiceBase.Core.Users;

[ValueObject<Guid>(conversions: Conversions.SystemTextJson, customizations: Customizations.AddFactoryMethodForGuids)]
public readonly partial struct UserId
{
    public static UserId Unknown => From(Guid.Empty);
};