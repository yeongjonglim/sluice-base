namespace SluiceBase.Api.Endpoints;

internal static class HealthEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }))
            .WithName("Health")
            .AllowAnonymous();

        app.MapGet("/api/health/authed", (HttpContext ctx) =>
                Results.Ok(new
                {
                    status = "ok",
                    user = ctx.User.Identity?.Name
                }))
            .WithName("HealthAuthed")
            .RequireAuthorization();
    }
}