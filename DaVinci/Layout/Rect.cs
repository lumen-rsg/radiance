namespace DaVinci.Layout;

public readonly struct Rect : IEquatable<Rect>
{
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }

    public int Right => X + Width;
    public int Bottom => Y + Height;

    public static Rect Empty { get; } = new() { X = 0, Y = 0, Width = 0, Height = 0 };

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public bool Contains(int col, int row) =>
        col >= X && col < X + Width && row >= Y && row < Y + Height;

    public Rect Inset(int left, int top, int right, int bottom) => new()
    {
        X = X + left,
        Y = Y + top,
        Width = Math.Max(0, Width - left - right),
        Height = Math.Max(0, Height - top - bottom)
    };

    public Rect Inset(int all) => Inset(all, all, all, all);

    public Rect Offset(int dx, int dy) => new()
    {
        X = X + dx,
        Y = Y + dy,
        Width = Width,
        Height = Height
    };

    public bool Equals(Rect other) =>
        X == other.X && Y == other.Y &&
        Width == other.Width && Height == other.Height;

    public override bool Equals(object? obj) => obj is Rect other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(X, Y, Width, Height);
    public static bool operator ==(Rect left, Rect right) => left.Equals(right);
    public static bool operator !=(Rect left, Rect right) => !left.Equals(right);

    public override string ToString() => $"Rect({X},{Y} {Width}x{Height})";
}
