using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Postgres;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace IntegrationTests.Supports.Extensions;

public static class DistributedApplicationTestingBuilderExtensions
{
    extension(IDistributedApplicationTestingBuilder appHost)
    {
        public void MakeTransient()
        {
            appHost.Services.ConfigureHttpClientDefaults(clientBuilder =>
            {
                clientBuilder.AddStandardResilienceHandler(b =>
                {
                    var defaultTimeout = TimeSpan.FromMinutes(1);
                    b.AttemptTimeout = b.TotalRequestTimeout = new HttpTimeoutStrategyOptions
                    {
                        Timeout = defaultTimeout,
                    };
                    b.CircuitBreaker = new HttpCircuitBreakerStrategyOptions
                    {
                        SamplingDuration = defaultTimeout * 2,
                    };
                });
            });

            // Remove all volume mounts
            foreach (var resource in appHost.Resources)
            {
                // remove mount annotation for tests
                if (resource.TryGetAnnotationsOfType<ContainerMountAnnotation>(out var mountAnnotations))
                {
                    foreach (var containerMountAnnotation in mountAnnotations.Where(x => x.Type == ContainerMountType.Volume))
                    {
                        resource.Annotations.Remove(containerMountAnnotation);
                    }
                }

                if (resource.TryGetAnnotationsOfType<ContainerLifetimeAnnotation>(out var lifetimeAnnotations))
                {
                    // remove container lifetime annotation for tests
                    foreach (var containerLifetimeAnnotation in lifetimeAnnotations.Where(x => x.Lifetime == ContainerLifetime.Persistent))
                    {
                        resource.Annotations.Remove(containerLifetimeAnnotation);
                    }
                }
            }

            var pg = appHost.Resources.OfType<PgAdminContainerResource>().FirstOrDefault();
            if (pg is not null)
            {
                appHost.Resources.Remove(pg);
            }
        }
    }
}