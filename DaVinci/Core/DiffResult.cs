using DaVinci.Diff;

namespace DaVinci.Core;

public sealed class DiffResult
{
    public IReadOnlyList<Patch> Patches { get; }
    public bool HasChanges => Patches.Count > 0;

    public DiffResult(IReadOnlyList<Patch> patches)
    {
        Patches = patches;
    }
}
