using Vogen;

namespace SluiceBase.Core.Mcp;

[ValueObject<string>]
[Instance("AuthorizationCode", "authorization_code")]
[Instance("RefreshToken", "refresh_token")]
public readonly partial struct GrantType
{
    private static Validation Validate(string input) =>
        input is "authorization_code" or "refresh_token"
            ? Validation.Ok
            : Validation.Invalid($"Unsupported grant type '{input}'.");
}
