namespace SluiceBase.Core.Targets;

public interface ITargetEngineRegistry
{
    ITargetEngine Resolve(string kind);
}
