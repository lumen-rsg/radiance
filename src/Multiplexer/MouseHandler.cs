using System.Text;

namespace Radiance.Multiplexer;

/// <summary>
/// Parsed mouse event from an SGR-encoded terminal sequence.
/// </summary>
public readonly struct MouseEvent
{
    public enum Button
    {
        None,
        Left,
        Middle,
        Right,
        ScrollUp,
        ScrollDown,
        Release
    }

    public Button Action { get; init; }
    public int Col { get; init; }   // 0-based
    public int Row { get; init; }   // 0-based
    public bool Shift { get; init; }
    public bool Alt { get; init; }
    public bool Ctrl { get; init; }
    public bool Drag { get; init; }
}

/// <summary>
/// Parses SGR-encoded mouse sequences from terminal input.
/// Format: ESC [ < button ; col ; row M (press) or m (release)
/// Button encoding: 0=left, 1=middle, 2=right, 32=drag, 64=scroll up, 65=scroll down
/// Modifiers: 4=shift, 8=alt, 16=ctrl
/// </summary>
public static class MouseParser
{
    /// <summary>
    /// Try to parse a mouse sequence from a string starting with ESC [&lt;.
    /// Returns the parsed event and the total length of the sequence,
    /// or null if the string doesn't contain a valid mouse sequence.
    /// </summary>
    public static (MouseEvent Event, int Length)? TryParse(ReadOnlySpan<byte> data)
    {
        // Minimum: ESC [ < 0 ; 1 ; 1 M = 10 bytes
        if (data.Length < 6) return null;
        if (data[0] != 0x1b || data[1] != (byte)'[' || data[2] != (byte)'<')
            return null;

        // Find the terminating M or m
        var endIdx = -1;
        for (var i = 3; i < data.Length; i++)
        {
            if (data[i] == (byte)'M' || data[i] == (byte)'m')
            {
                endIdx = i;
                break;
            }
        }

        if (endIdx < 0) return null;

        // Parse: button ; col ; row M/m
        var payload = Encoding.ASCII.GetString(data.Slice(3, endIdx - 3));
        var parts = payload.Split(';');
        if (parts.Length != 3) return null;

        if (!int.TryParse(parts[0], out var buttonCode) ||
            !int.TryParse(parts[1], out var col) ||
            !int.TryParse(parts[2], out var row))
            return null;

        var isRelease = data[endIdx] == (byte)'m';

        // Decode button
        var rawButton = buttonCode & 0x3; // Lower 2 bits
        var isDrag = (buttonCode & 0x20) != 0;
        var isScroll = (buttonCode & 0x40) != 0;

        MouseEvent.Button action;
        if (isRelease)
            action = MouseEvent.Button.Release;
        else if (isScroll)
            action = rawButton == 0 ? MouseEvent.Button.ScrollUp : MouseEvent.Button.ScrollDown;
        else
            action = rawButton switch
            {
                0 => MouseEvent.Button.Left,
                1 => MouseEvent.Button.Middle,
                2 => MouseEvent.Button.Right,
                _ => MouseEvent.Button.None
            };

        // Decode modifiers
        var shift = (buttonCode & 0x04) != 0;
        var alt = (buttonCode & 0x08) != 0;
        var ctrl = (buttonCode & 0x10) != 0;

        var evt = new MouseEvent
        {
            Action = action,
            Col = col - 1,  // Convert to 0-based
            Row = row - 1,
            Shift = shift,
            Alt = alt,
            Ctrl = ctrl,
            Drag = isDrag
        };

        return (evt, endIdx + 1);
    }
}
