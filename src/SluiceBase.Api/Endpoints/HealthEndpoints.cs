using Microsoft.AspNetCore.Http.HttpResults;

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
                    TypedResults.Ok(new HealthResponse("ok", ctx.User.Identity?.Name)))
            .WithName("HealthAuthed")
            .RequireAuthorization();
    }
}

internal sealed record HealthResponse(string Status, string? User);