using SluiceBase.Core.Targets;

namespace SluiceBase.Api.Targets;

internal sealed class TargetEngineRegistry : ITargetEngineRegistry
{
    private readonly Dictionary<string, ITargetEngine> _engines;

    public TargetEngineRegistry(IEnumerable<ITargetEngine> engines) =>
        _engines = engines.ToDictionary(e => e.Kind, StringComparer.OrdinalIgnoreCase);

    public ITargetEngine Resolve(string kind) =>
        _engines.TryGetValue(kind, out var engine)
            ? engine
            : throw new InvalidOperationException($"No target engine registered for kind '{kind}'.");
}
