namespace DaVinci.Terminal;

public sealed class TextStyle : IEquatable<TextStyle>
{
    public Color? Foreground { get; init; }
    public Color? Background { get; init; }
    public bool Bold { get; init; }
    public bool Dim { get; init; }
    public bool Italic { get; init; }
    public bool Underline { get; init; }
    public bool Strikethrough { get; init; }
    public bool Blink { get; init; }
    public bool Reverse { get; init; }

    public static TextStyle Empty { get; } = new();

    public string ToAnsiOpen()
    {
        if (Equals(Empty)) return "";

        var parts = new List<string>();

        if (Bold) parts.Add("\x1b[1m");
        if (Dim) parts.Add("\x1b[2m");
        if (Italic) parts.Add("\x1b[3m");
        if (Underline) parts.Add("\x1b[4m");
        if (Blink) parts.Add("\x1b[5m");
        if (Reverse) parts.Add("\x1b[7m");
        if (Strikethrough) parts.Add("\x1b[9m");
        if (Foreground.HasValue) parts.Add(Foreground.Value.ToFgAnsi());
        if (Background.HasValue) parts.Add(Background.Value.ToBgAnsi());

        return string.Concat(parts);
    }

    public string ToAnsiClose()
    {
        if (Equals(Empty)) return "";
        return "\x1b[0m";
    }

    public bool Equals(TextStyle? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        if (Bold != other.Bold ||
            Dim != other.Dim ||
            Italic != other.Italic ||
            Underline != other.Underline ||
            Strikethrough != other.Strikethrough ||
            Blink != other.Blink ||
            Reverse != other.Reverse)
            return false;

        if (Foreground.HasValue != other.Foreground.HasValue) return false;
        if (Foreground.HasValue && !Foreground.Value.Equals(other.Foreground!.Value)) return false;

        if (Background.HasValue != other.Background.HasValue) return false;
        if (Background.HasValue && !Background.Value.Equals(other.Background!.Value)) return false;

        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as TextStyle);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Foreground);
        hash.Add(Background);
        hash.Add(Bold);
        hash.Add(Dim);
        hash.Add(Italic);
        hash.Add(Underline);
        hash.Add(Strikethrough);
        hash.Add(Blink);
        hash.Add(Reverse);
        return hash.ToHashCode();
    }

    public static bool operator ==(TextStyle? left, TextStyle? right) =>
        left?.Equals(right) ?? right is null;
    public static bool operator !=(TextStyle? left, TextStyle? right) =>
        !(left == right);
}
