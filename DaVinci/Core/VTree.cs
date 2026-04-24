namespace DaVinci.Core;

public sealed class VTree
{
    public VNode Root { get; private set; }

    public VTree(VNode root)
    {
        Root = root;
    }

    public DiffResult Update(VNode newRoot)
    {
        var diff = DaVinci.Diff.Differ.Diff(Root, newRoot);
        Root = newRoot;
        return new DiffResult(diff);
    }
}
