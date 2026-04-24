using DaVinci.Core;

namespace DaVinci.Layout;

public static class LayoutEngine
{
    /// <summary>
    /// Computes layout rects for a component and all its children.
    /// Returns the total height consumed.
    /// </summary>
    public static int ComputeLayout(Component root, Rect available, LayoutConstraints constraints)
    {
        if (available.IsEmpty) return 0;

        // Leaf components: height is computed by the component itself
        if (root.Children.Count == 0)
        {
            var height = root.ComputeHeight(available.Width);
            root.LayoutRect = new Rect
            {
                X = available.X,
                Y = available.Y,
                Width = available.Width,
                Height = height
            };
            return height;
        }

        // Container components: lay out children in sequence
        var currentY = available.Y;
        var currentX = available.X;
        var maxHeightInRow = 0;

        foreach (var child in root.Children)
        {
            var childConstraints = constraints with
            {
                MaxWidth = constraints.Direction == LayoutDirection.Horizontal
                    ? constraints.MaxWidth
                    : available.Width,
                MaxHeight = available.Y + available.Height - currentY
            };

            if (constraints.Direction == LayoutDirection.Vertical)
            {
                var childAvailable = new Rect
                {
                    X = currentX,
                    Y = currentY,
                    Width = available.Width,
                    Height = Math.Max(0, available.Bottom - currentY)
                };

                var childHeight = ComputeLayout(child, childAvailable, childConstraints);
                currentY += childHeight + constraints.Gap;
            }
            else
            {
                var childAvailable = new Rect
                {
                    X = currentX,
                    Y = currentY,
                    Width = Math.Max(0, available.Right - currentX),
                    Height = available.Height
                };

                var childHeight = ComputeLayout(child, childAvailable, childConstraints);
                currentX += child.LayoutRect.Width + constraints.Gap;
                maxHeightInRow = Math.Max(maxHeightInRow, childHeight);
            }
        }

        var totalHeight = constraints.Direction == LayoutDirection.Vertical
            ? currentY - available.Y
            : maxHeightInRow;

        root.LayoutRect = new Rect
        {
            X = available.X,
            Y = available.Y,
            Width = available.Width,
            Height = totalHeight
        };

        return totalHeight;
    }
}
