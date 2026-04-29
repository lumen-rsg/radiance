namespace Radiance.Multiplexer;

/// <summary>
/// Binary tree layout for multiplexer panes.
/// Leaf nodes hold a pane; inner nodes hold a split direction.
/// </summary>
public abstract class PaneLayout
{
    /// <summary>
    /// Compute the rectangle for every leaf in the tree, mapped to its pane index.
    /// Returns one Rect per leaf, in order.
    /// </summary>
    public abstract List<(int PaneIndex, Rect Rect)> ComputeRects(Rect area);

    /// <summary>Count of leaf (pane) nodes in this tree.</summary>
    public abstract int PaneCount { get; }

    /// <summary>Replace a leaf by pane index with a new layout (split).</summary>
    public abstract PaneLayout ReplaceLeaf(int paneIndex, PaneLayout replacement);

    /// <summary>Remove a leaf by pane index. Returns null if this node becomes empty.</summary>
    public abstract PaneLayout? RemoveLeaf(int paneIndex);

    /// <summary>
    /// Find the pane index at the given leaf position (0-based from left).
    /// Returns -1 if not found.
    /// </summary>
    public abstract int LeafIndexOf(int paneIndex);

    // ── Leaf node ────────────────────────────────────────────────────

    public sealed class Leaf(int paneIndex) : PaneLayout
    {
        public int PaneIndex { get; } = paneIndex;

        public override int PaneCount => 1;

        public override List<(int PaneIndex, Rect Rect)> ComputeRects(Rect area)
        {
            return [(PaneIndex, area)];
        }

        public override PaneLayout ReplaceLeaf(int targetPaneIndex, PaneLayout replacement)
        {
            return PaneIndex == targetPaneIndex ? replacement : this;
        }

        public override PaneLayout? RemoveLeaf(int targetPaneIndex)
        {
            return PaneIndex == targetPaneIndex ? null : this;
        }

        public override int LeafIndexOf(int targetPaneIndex)
        {
            return PaneIndex == targetPaneIndex ? 0 : -1;
        }
    }

    // ── Split node ───────────────────────────────────────────────────

    public sealed class Split(SplitDir direction, PaneLayout first, PaneLayout second, float ratio = 0.5f) : PaneLayout
    {
        public SplitDir Direction { get; } = direction;
        public PaneLayout First { get; } = first;
        public PaneLayout Second { get; } = second;
        public float Ratio { get; set; } = ratio;

        public override int PaneCount => First.PaneCount + Second.PaneCount;

        public override List<(int PaneIndex, Rect Rect)> ComputeRects(Rect area)
        {
            var results = new List<(int, Rect)>();

            if (Direction == SplitDir.Horizontal)
            {
                // Side by side
                var splitX = (int)(area.X + area.Width * Ratio);
                var firstWidth = splitX - area.X;
                var secondWidth = area.Width - firstWidth - 1; // -1 for border
                if (secondWidth < 1) secondWidth = 1;

                results.AddRange(First.ComputeRects(new Rect(area.X, area.Y, firstWidth, area.Height)));
                results.AddRange(Second.ComputeRects(new Rect(splitX + 1, area.Y, secondWidth, area.Height)));
            }
            else
            {
                // Stacked
                var splitY = (int)(area.Y + area.Height * Ratio);
                var firstHeight = splitY - area.Y;
                var secondHeight = area.Height - firstHeight - 1; // -1 for border
                if (secondHeight < 1) secondHeight = 1;

                results.AddRange(First.ComputeRects(new Rect(area.X, area.Y, area.Width, firstHeight)));
                results.AddRange(Second.ComputeRects(new Rect(area.X, splitY + 1, area.Width, secondHeight)));
            }

            return results;
        }

        public override PaneLayout ReplaceLeaf(int targetPaneIndex, PaneLayout replacement)
        {
            var newFirst = First.ReplaceLeaf(targetPaneIndex, replacement);
            var newSecond = Second.ReplaceLeaf(targetPaneIndex, replacement);

            if (ReferenceEquals(newFirst, First) && ReferenceEquals(newSecond, Second))
                return this;

            return new Split(Direction, newFirst, newSecond, Ratio);
        }

        public override PaneLayout? RemoveLeaf(int targetPaneIndex)
        {
            var removedFirst = First.RemoveLeaf(targetPaneIndex);
            var removedSecond = Second.RemoveLeaf(targetPaneIndex);

            // If neither changed, return this
            if (ReferenceEquals(removedFirst, First) && ReferenceEquals(removedSecond, Second))
                return this;

            // If one side is gone, return the other
            if (removedFirst is null) return removedSecond;
            if (removedSecond is null) return removedFirst;

            return new Split(Direction, removedFirst, removedSecond, Ratio);
        }

        public override int LeafIndexOf(int targetPaneIndex)
        {
            var idx = First.LeafIndexOf(targetPaneIndex);
            if (idx >= 0) return idx;
            var secondIdx = Second.LeafIndexOf(targetPaneIndex);
            return secondIdx >= 0 ? First.PaneCount + secondIdx : -1;
        }
    }
}

public enum SplitDir { Vertical, Horizontal }
