using Microsoft.AspNetCore.Http.HttpResults;
using SluiceBase.Api.Auth;

namespace SluiceBase.Api.Endpoints;

internal static class HealthEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }))
            .WithName("Health")
            .AllowAnonymous();

        app.MapGet("/api/health/authed",
                Ok<HealthResponse> (HttpContext ctx) =>
                    TypedResults.Ok(new HealthResponse("ok", ctx.User.FindFirst(AppClaims.Name)?.Value)))
            .WithName("HealthAuthed")
            .RequireAuthorization();
    }
}

internal sealed record HealthResponse(string Status, string? User);