using Docker.DotNet;
using DotNet.Testcontainers.Containers;

namespace IntegrationTests.Supports;

internal static class ContainerStartup
{
    // Docker's libnetwork port programming can transiently fail with
    // "address already in use" when many containers start close together in CI (the Aspire
    // stack already holds a batch of ports; see TestcontainersConfig). It is a race in the
    // Docker daemon, not a real conflict — a fresh container gets a new random host port and
    // succeeds. So on that specific failure, discard the container and rebuild on a retry.
    //
    // The factory is invoked once per attempt so each retry gets a brand-new port mapping.
    public static async Task<T> StartWithRetryAsync<T>(
        Func<T> build,
        int maxAttempts = 5,
        CancellationToken ct = default)
        where T : IContainer
    {
        for (var attempt = 1; ; attempt++)
        {
            var container = build();
            try
            {
                await container.StartAsync(ct);
                return container;
            }
            catch (Exception ex) when (attempt < maxAttempts && IsTransientPortConflict(ex))
            {
                await container.DisposeAsync();
                await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt), ct);
            }
        }
    }

    private static bool IsTransientPortConflict(Exception ex) =>
        ex is DockerApiException docker
        && docker.Message.Contains("address already in use", StringComparison.OrdinalIgnoreCase);
}
