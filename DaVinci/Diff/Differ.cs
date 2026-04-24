using DaVinci.Core;

namespace DaVinci.Diff;

public static class Differ
{
    public static List<Patch> Diff(VNode? oldRoot, VNode? newRoot)
    {
        var patches = new List<Patch>();
        DiffNode(oldRoot, newRoot, 0, patches);
        return patches;
    }

    private static void DiffNode(VNode? oldNode, VNode? newNode, int index, List<Patch> patches)
    {
        // Both null — nothing to do
        if (oldNode is null && newNode is null)
            return;

        // Added node
        if (oldNode is null)
        {
            patches.Add(new Patch(PatchType.AddNode, null, newNode, index, null));
            return;
        }

        // Removed node
        if (newNode is null)
        {
            patches.Add(new Patch(PatchType.RemoveNode, oldNode, null, index, null));
            return;
        }

        // Different types or keys — full replacement
        if (oldNode.ComponentType != newNode.ComponentType ||
            oldNode.Key != newNode.Key)
        {
            patches.Add(new Patch(PatchType.RemoveNode, oldNode, null, index, null));
            patches.Add(new Patch(PatchType.AddNode, null, newNode, index, null));
            return;
        }

        // Same type — check if props changed
        if (!oldNode.Props.Equals(newNode.Props))
        {
            patches.Add(new Patch(PatchType.UpdateProps, oldNode, newNode, index, null));
        }

        // Recurse into children using keyed reconciliation
        DiffChildren(oldNode.Children, newNode.Children, patches);
    }

    private static void DiffChildren(
        List<VNode> oldChildren, List<VNode> newChildren, List<Patch> patches)
    {
        // Build maps for keyed matching
        var oldByKey = new Dictionary<string, VNode>();
        var oldWithoutKey = new List<VNode>();

        foreach (var child in oldChildren)
        {
            if (child.Key is not null)
                oldByKey[child.Key] = child;
            else
                oldWithoutKey.Add(child);
        }

        var matched = new HashSet<VNode>();

        for (var i = 0; i < newChildren.Count; i++)
        {
            var newChild = newChildren[i];

            // Try to find matching old child by key
            VNode? oldMatch = null;
            if (newChild.Key is not null && oldByKey.TryGetValue(newChild.Key, out var keyed))
            {
                oldMatch = keyed;
                matched.Add(keyed);
            }
            else if (i < oldWithoutKey.Count && !matched.Contains(oldWithoutKey[i]))
            {
                // Fallback: positional matching for unkeyed children
                oldMatch = oldWithoutKey[i];
                matched.Add(oldMatch);
            }

            DiffNode(oldMatch, newChild, i, patches);
        }

        // Any old children that weren't matched are removed
        foreach (var oldChild in oldChildren)
        {
            if (!matched.Contains(oldChild))
            {
                patches.Add(new Patch(PatchType.RemoveNode, oldChild, null, -1, null));
            }
        }
    }
}
