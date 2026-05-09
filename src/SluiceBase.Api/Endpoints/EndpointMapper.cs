namespace SluiceBase.Api.Endpoints;

internal static class EndpointMapper
{
    public static IEndpointRouteBuilder MapAllEndpoints(this WebApplication app)
    {
        AuthEndpoints.Map(app);
        HealthEndpoints.Map(app);
        PermissionEndpoints.Map(app);
        ServerEndpoints.Map(app);
        SchemaEndpoints.Map(app);
        QueryEndpoints.Map(app);

        if (app.Environment.IsDevelopment())
        {
            DevelopmentEndpoints.Map(app);
        }

        return app;
    }
}