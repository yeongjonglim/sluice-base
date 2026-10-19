using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SluiceBase.Api.Mcp.Clients;

namespace SluiceBase.Api.Tests;

public class CimdHttpClientRegistrationTests
{
    // Framework logging handlers only log; anything else (resilience, service discovery) could
    // retry, re-resolve or re-route the fetch around the connect-time guard.
    private static readonly HashSet<string> PassiveHandlers =
    [
        "LifetimeTrackingHttpMessageHandler",
        "LoggingHttpMessageHandler",
        "LoggingScopeHttpMessageHandler",
    ];

    [Fact]
    public void CimdClient_WithServiceDefaults_HasNoAdditionalHandlersAndGuardedPrimary()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddServiceDefaults();
        builder.Services.AddCimdHttpClient(allowLoopback: false);
        using var host = builder.Build();

        var handler = host.Services.GetRequiredService<IHttpMessageHandlerFactory>()
            .CreateHandler(CimdFetcher.HttpClientName);

        var delegating = new List<string>();
        while (handler is DelegatingHandler d)
        {
            delegating.Add(d.GetType().Name);
            handler = d.InnerHandler!;
        }

        Assert.All(delegating, name => Assert.Contains(name, PassiveHandlers));
        var primary = Assert.IsType<SocketsHttpHandler>(handler);
        Assert.False(primary.AllowAutoRedirect);
        Assert.False(primary.UseProxy);
        Assert.NotNull(primary.ConnectCallback);
    }
}
