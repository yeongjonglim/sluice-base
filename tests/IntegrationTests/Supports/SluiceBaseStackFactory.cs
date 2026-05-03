using Aspire.Hosting;
using Aspire.Hosting.Testing;
using IntegrationTests.Supports.Extensions;

namespace IntegrationTests.Supports;

public sealed class SluiceBaseStackFactory : IAsyncLifetime
{
    private DistributedApplication? App { get; set; }
    public DistributedApplication InitialisedApp => App!;

    public async Task InitializeAsync()
    {
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>();
        appHost.MakeTransient();
        App = await appHost.BuildAsync();
        await App.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (App is not null)
        {
            await App.DisposeAsync();
        }
    }
}

[CollectionDefinition("Aspire")]
public class SluiceBaseCollectionDefinition : ICollectionFixture<SluiceBaseStackFactory>;