namespace SluiceBase.Api.Endpoints;

internal static class EndpointMapper
{
    public static IEndpointRouteBuilder MapAllEndpoints(this IEndpointRouteBuilder app)
    {
        AuthEndpoints.Map(app);
        HealthEndpoints.Map(app);
        return app;
    }
}