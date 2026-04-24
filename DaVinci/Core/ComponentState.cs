namespace DaVinci.Core;

public abstract class ComponentState
{
    internal int Version { get; private set; }

    internal void IncrementVersion() => Version++;
}
