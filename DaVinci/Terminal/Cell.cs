namespace DaVinci.Terminal;

public readonly struct Cell : IEquatable<Cell>
{
    public char Character { get; init; }
    public TextStyle Style { get; init; }
    public int DisplayWidth { get; init; }

    public static Cell Empty { get; } = new()
    {
        Character = ' ',
        Style = TextStyle.Empty,
        DisplayWidth = 1
    };

    public static Cell FromChar(char c, TextStyle? style = null)
    {
        var width = GetDisplayWidth(c);
        return new Cell
        {
            Character = c,
            Style = style ?? TextStyle.Empty,
            DisplayWidth = width
        };
    }

    public bool Equals(Cell other) =>
        Character == other.Character &&
        Style == other.Style;

    public override bool Equals(object? obj) => obj is Cell other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Character, Style);
    public static bool operator ==(Cell left, Cell right) => left.Equals(right);
    public static bool operator !=(Cell left, Cell right) => !left.Equals(right);

    /// <summary>
    /// Get the display width of a character.
    /// CJK characters and most emoji take 2 columns.
    /// </summary>
    public static int GetDisplayWidth(char c)
    {
        if (c <= 0x7F) return 1;

        // CJK Unified Ideographs and other wide characters
        if (c >= 0x1100 && (
            c <= 0x115F ||   // Hangul Jamo
            c >= 0x2E80 && c <= 0x303E ||   // CJK and Korean
            c >= 0x3040 && c <= 0x33BF ||   // Japanese
            c >= 0x3400 && c <= 0x4DBF ||   // CJK Extension A
            c >= 0x4E00 && c <= 0x9FFF ||   // CJK Unified Ideographs
            c >= 0xAC00 && c <= 0xD7AF ||   // Hangul Syllables
            c >= 0xF900 && c <= 0xFAFF ||   // CJK Compatibility Ideographs
            c >= 0xFE30 && c <= 0xFE6F ||   // CJK Compatibility Forms
            c >= 0xFF01 && c <= 0xFF60 ||   // Fullwidth Forms
            c >= 0xFFE0 && c <= 0xFFE6))    // Fullwidth Signs
            return 2;

        return 1;
    }
}
