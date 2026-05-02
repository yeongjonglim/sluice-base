using Aspire.Hosting.Eventing;
using Aspire.Hosting.Lifecycle;

namespace AppHost;

internal sealed class DevSeedHook(ResourceNotificationService notifications)
    : IDistributedApplicationEventingSubscriber
{
    public Task SubscribeAsync(
        IDistributedApplicationEventing eventing,
        DistributedApplicationExecutionContext context,
        CancellationToken cancellationToken)
    {
        eventing.Subscribe<AfterResourcesCreatedEvent>(SeedWhenApiReadyAsync);
        return Task.CompletedTask;
    }

    private async Task SeedWhenApiReadyAsync(AfterResourcesCreatedEvent @event, CancellationToken ct)
    {
        string? apiUrl = null;

        await foreach (var evt in notifications.WatchAsync(ct))
        {
            if (evt.Resource.Name != "api")
            {
                continue;
            }

            var url = evt.Snapshot.Urls.FirstOrDefault(u => !u.IsInternal)?.Url;
            if (url is not null)
            {
                apiUrl = url;
            }

            if (evt.Snapshot.HealthStatus == Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy
                && apiUrl is not null)
            {
                break;
            }
        }

        if (apiUrl is null)
        {
            return;
        }

        using var http = new HttpClient();
        try
        {
            var res = await http.PostAsync($"{apiUrl.TrimEnd('/')}/api/internal/dev-seed", null, ct);
            res.EnsureSuccessStatusCode();
            Console.WriteLine("[DevSeedHook] Dev data seeded.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[DevSeedHook] Seed failed: {ex.Message}");
        }
    }
}
