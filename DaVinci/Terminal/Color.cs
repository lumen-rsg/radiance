namespace DaVinci.Terminal;

public readonly struct Color : IEquatable<Color>
{
    public ColorKind Kind { get; init; }
    public byte R { get; init; }
    public byte G { get; init; }
    public byte B { get; init; }
    public byte Index { get; init; }
    public AnsiNamedColor Named { get; init; }

    public static Color Default => new() { Kind = ColorKind.Default };
    public static Color Black => new() { Kind = ColorKind.Named, Named = AnsiNamedColor.Black };
    public static Color Red => new() { Kind = ColorKind.Named, Named = AnsiNamedColor.Red };
    public static Color Green => new() { Kind = ColorKind.Named, Named = AnsiNamedColor.Green };
    public static Color Yellow => new() { Kind = ColorKind.Named, Named = AnsiNamedColor.Yellow };
    public static Color Blue => new() { Kind = ColorKind.Named, Named = AnsiNamedColor.Blue };
    public static Color Magenta => new() { Kind = ColorKind.Named, Named = AnsiNamedColor.Magenta };
    public static Color Cyan => new() { Kind = ColorKind.Named, Named = AnsiNamedColor.Cyan };
    public static Color White => new() { Kind = ColorKind.Named, Named = AnsiNamedColor.White };

    public static Color BrightBlack => new() { Kind = ColorKind.Named, Named = AnsiNamedColor.BrightBlack };
    public static Color BrightRed => new() { Kind = ColorKind.Named, Named = AnsiNamedColor.BrightRed };
    public static Color BrightGreen => new() { Kind = ColorKind.Named, Named = AnsiNamedColor.BrightGreen };
    public static Color BrightYellow => new() { Kind = ColorKind.Named, Named = AnsiNamedColor.BrightYellow };
    public static Color BrightBlue => new() { Kind = ColorKind.Named, Named = AnsiNamedColor.BrightBlue };
    public static Color BrightMagenta => new() { Kind = ColorKind.Named, Named = AnsiNamedColor.BrightMagenta };
    public static Color BrightCyan => new() { Kind = ColorKind.Named, Named = AnsiNamedColor.BrightCyan };
    public static Color BrightWhite => new() { Kind = ColorKind.Named, Named = AnsiNamedColor.BrightWhite };

    public static Color FromRgb(byte r, byte g, byte b) => new()
    {
        Kind = ColorKind.Rgb,
        R = r, G = g, B = b
    };

    public static Color FromRgb(int r, int g, int b) => new()
    {
        Kind = ColorKind.Rgb,
        R = (byte)r, G = (byte)g, B = (byte)b
    };

    public static Color FromNamed(AnsiNamedColor named) => new()
    {
        Kind = ColorKind.Named,
        Named = named
    };

    public static Color FromIndex(byte index) => new()
    {
        Kind = ColorKind.Palette256,
        Index = index
    };

    public static Color FromHex(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 3)
            hex = $"{hex[0]}{hex[0]}{hex[1]}{hex[1]}{hex[2]}{hex[2]}";

        var r = Convert.ToByte(hex[..2], 16);
        var g = Convert.ToByte(hex[2..4], 16);
        var b = Convert.ToByte(hex[4..6], 16);
        return FromRgb(r, g, b);
    }

    public string ToFgAnsi() => Kind switch
    {
        ColorKind.Default => "\x1b[39m",
        ColorKind.Named => $"\x1b[{(int)Named}m",
        ColorKind.Palette256 => $"\x1b[38;5;{Index}m",
        ColorKind.Rgb => $"\x1b[38;2;{R};{G};{B}m",
        _ => ""
    };

    public string ToBgAnsi() => Kind switch
    {
        ColorKind.Default => "\x1b[49m",
        ColorKind.Named => $"\x1b[{(int)Named + 10}m",
        ColorKind.Palette256 => $"\x1b[48;5;{Index}m",
        ColorKind.Rgb => $"\x1b[48;2;{R};{G};{B}m",
        _ => ""
    };

    public bool Equals(Color other) =>
        Kind == other.Kind &&
        R == other.R && G == other.G && B == other.B &&
        Index == other.Index && Named == other.Named;

    public override bool Equals(object? obj) => obj is Color other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Kind, R, G, B, Index, Named);
    public static bool operator ==(Color left, Color right) => left.Equals(right);
    public static bool operator !=(Color left, Color right) => !left.Equals(right);

    public override string ToString() => Kind switch
    {
        ColorKind.Default => "Default",
        ColorKind.Named => $"Named({Named})",
        ColorKind.Palette256 => $"Index({Index})",
        ColorKind.Rgb => $"Rgb({R},{G},{B})",
        _ => "Unknown"
    };
}

public enum ColorKind { Default, Named, Palette256, Rgb }

public enum AnsiNamedColor
{
    Black = 30,
    Red = 31,
    Green = 32,
    Yellow = 33,
    Blue = 34,
    Magenta = 35,
    Cyan = 36,
    White = 37,
    Default = 39,
    BrightBlack = 90,
    BrightRed = 91,
    BrightGreen = 92,
    BrightYellow = 93,
    BrightBlue = 94,
    BrightMagenta = 95,
    BrightCyan = 96,
    BrightWhite = 97
}
