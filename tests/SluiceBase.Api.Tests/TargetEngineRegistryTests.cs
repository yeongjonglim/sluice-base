using SluiceBase.Api.Targets;

namespace SluiceBase.Api.Tests;

public class TargetEngineRegistryTests
{
    [Fact]
    public void Resolve_ReturnsEngineMatchingKind()
    {
        var registry = new TargetEngineRegistry([new PostgresTargetEngine()]);

        var engine = registry.Resolve("postgres");

        Assert.Equal("postgres", engine.Kind);
    }

    [Fact]
    public void Resolve_IsCaseInsensitive()
    {
        var registry = new TargetEngineRegistry([new PostgresTargetEngine()]);

        var engine = registry.Resolve("POSTGRES");

        Assert.Equal("postgres", engine.Kind);
    }

    [Fact]
    public void Resolve_ThrowsForUnknownKind()
    {
        var registry = new TargetEngineRegistry([new PostgresTargetEngine()]);

        Assert.Throws<InvalidOperationException>(() => registry.Resolve("mongodb"));
    }
}
