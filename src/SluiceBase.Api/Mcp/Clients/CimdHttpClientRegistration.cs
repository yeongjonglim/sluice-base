namespace SluiceBase.Api.Mcp.Clients;

internal static class CimdHttpClientRegistration
{
    public static IHttpClientBuilder AddCimdHttpClient(this IServiceCollection services, bool allowLoopback)
    {
        // The service defaults add resilience (retries would stretch the 5 s budget, multiplying
        // outbound fetches an anonymous caller can trigger) and service discovery (which would
        // re-resolve the host outside the connect-time address guard). Neither belongs here.
        return services.AddHttpClient(CimdFetcher.HttpClientName, c => c.Timeout = CimdFetcher.FetchTimeout)
            .ConfigurePrimaryHttpMessageHandler(() => CimdFetcher.CreateHandler(allowLoopback))
            .ConfigureAdditionalHttpMessageHandlers((handlers, _) => handlers.Clear());
    }
}
