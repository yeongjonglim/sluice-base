using SluiceBase.Api.Mcp;
using SluiceBase.Api.Mcp.Oauth;

namespace SluiceBase.Api.Endpoints;

internal static class EndpointMapper
{
    public static IEndpointRouteBuilder MapAllEndpoints(this WebApplication app)
    {
        AuthEndpoints.Map(app);
        HealthEndpoints.Map(app);
        PermissionEndpoints.Map(app);
        DatabaseRoleEndpoints.Map(app);
        SensitiveColumnEndpoints.Map(app);
        CatalogEndpoints.Map(app);
        ServerEndpoints.Map(app);
        CredentialEndpoints.Map(app);
        DatabaseEndpoints.Map(app);
        SchemaEndpoints.Map(app);
        QueryEndpoints.Map(app);
        UpdateEndpoints.Map(app);

        if (app.Configuration.GetValue($"{McpOptions.SectionName}:Enabled", true))
        {
            OAuthMetadataEndpoints.Map(app);
            OAuthRegistrationEndpoints.Map(app);
            OAuthAuthorizeEndpoints.Map(app);
            OAuthTokenEndpoints.Map(app);
        }

        if (app.Environment.IsDevelopment())
        {
            DevelopmentEndpoints.Map(app);
        }

        return app;
    }
}