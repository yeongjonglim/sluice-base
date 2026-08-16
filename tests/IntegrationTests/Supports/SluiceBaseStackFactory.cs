using Aspire.Hosting;
using Aspire.Hosting.Testing;
using IntegrationTests.Supports;
using IntegrationTests.Supports.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Sdk;
using Xunit.v3;

[assembly: AssemblyFixture(typeof(SluiceBaseStackFactory))]
[assembly: Parallelization(Mode = ParallelMode.None)]

namespace IntegrationTests.Supports;

public sealed class SluiceBaseStackFactory : IAsyncLifetime
{
    private DistributedApplication? App { get; set; }
    public DistributedApplication InitialisedApp => App!;

    public async ValueTask InitializeAsync()
    {
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>(
        [
            "DcpPublisher:RandomizePorts=false" // To get fixed port for login redirect
        ]);
        appHost.MakeTransient();
        appHost.Services.ConfigureHttpClientDefaults(clientBuilder =>
        {
            clientBuilder.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AllowAutoRedirect = false
            });
        });
        App = await appHost.BuildAsync();
        await App.StartAsync();

        await App.ResourceNotifications.WaitForResourceHealthyAsync("keycloak");
        await App.ResourceNotifications.WaitForResourceHealthyAsync("api");
        await App.ResourceNotifications.WaitForResourceHealthyAsync("web");
        await App.ResourceNotifications.WaitForResourceHealthyAsync("gateway");
    }

    public async ValueTask DisposeAsync()
    {
        if (App is not null)
        {
            await App.DisposeAsync();
        }
    }
}