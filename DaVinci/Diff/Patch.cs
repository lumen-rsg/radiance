using DaVinci.Core;

namespace DaVinci.Diff;

public sealed record Patch(
    PatchType Type,
    VNode? OldNode,
    VNode? NewNode,
    int ChildIndex,
    IReadOnlyList<Patch>? ChildPatches
);
