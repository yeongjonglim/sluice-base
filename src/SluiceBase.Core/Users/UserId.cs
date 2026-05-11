using Vogen;

namespace SluiceBase.Core.Users;

[ValueObject<Guid>(conversions: Conversions.SystemTextJson, customizations: Customizations.AddFactoryMethodForGuids)]
public readonly partial struct UserId
{
    public static UserId System => From(Guid.Parse("11111111-1111-1111-1111-111111111111"));
}