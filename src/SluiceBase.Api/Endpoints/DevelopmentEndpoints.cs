using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.HttpResults;

namespace SluiceBase.Api.Endpoints;

internal static class DevelopmentEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/internal/dev/encrypt",
                Ok<EncryptResponse> (EncryptRequest req, IDataProtectionProvider dataProtection) =>
                {
                    var protector = dataProtection.CreateProtector("SluiceBase.ServerPassword");
                    return TypedResults.Ok(new EncryptResponse(protector.Protect(req.Plaintext)));
                })
            .RequireHost("localhost")
            .ExcludeFromDescription();
    }

    public sealed record EncryptRequest(string Plaintext);

    public sealed record EncryptResponse(string Ciphertext);
}