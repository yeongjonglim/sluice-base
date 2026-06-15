using System.Runtime.CompilerServices;

namespace IntegrationTests.Supports;

internal static class TestcontainersConfig
{
    [ModuleInitializer]
    internal static void Configure()
    {
        // Testcontainers' Ryuk reaper container intermittently fails to bind a host port
        // ("address already in use") in CI when running alongside the Aspire stack, which
        // already holds many ports. The CI job is ephemeral and tears everything down on
        // completion, so the reaper is unnecessary. Disable it to avoid the port conflict.
        Environment.SetEnvironmentVariable("TESTCONTAINERS_RYUK_DISABLED", "true");
    }
}
