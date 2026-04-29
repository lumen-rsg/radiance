namespace DaVinci.Terminal;

public sealed record TextStyle
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
}
