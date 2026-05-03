using Aspire.Hosting;
using Aspire.Hosting.Testing;
using IntegrationTests.Supports.Extensions;
using Microsoft.Extensions.DependencyInjection;

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
    }

    public async ValueTask DisposeAsync()
    {
        if (App is not null)
        {
            await App.DisposeAsync();
        }
    }
}

[CollectionDefinition("Aspire")]
public class SluiceBaseCollectionDefinition : ICollectionFixture<SluiceBaseStackFactory>;