using System.Text;

namespace Radiance.Multiplexer;

/// <summary>
/// Encodes ConsoleKeyInfo into terminal escape sequences that child processes
/// running inside a PTY expect (VT100/xterm format).
/// </summary>
public static class TerminalKeyEncoder
{
    /// <summary>
    /// Encode a ConsoleKeyInfo into bytes to write to the PTY master.
    /// Returns the raw byte sequence the child terminal application expects.
    /// </summary>
    public static byte[] Encode(ConsoleKeyInfo key)
    {
        var ctrl = key.Modifiers.HasFlag(ConsoleModifiers.Control);
        var alt = key.Modifiers.HasFlag(ConsoleModifiers.Alt);
        var shift = key.Modifiers.HasFlag(ConsoleModifiers.Shift);

        // Handle special keys first
        switch (key.Key)
        {
            // Function keys
            case ConsoleKey.F1: return EscSeq(alt, shift, "P");
            case ConsoleKey.F2: return EscSeq(alt, shift, "Q");
            case ConsoleKey.F3: return EscSeq(alt, shift, "R");
            case ConsoleKey.F4: return EscSeq(alt, shift, "S");
            case ConsoleKey.F5: return EscSeq(alt, shift, "15");
            case ConsoleKey.F6: return EscSeq(alt, shift, "17");
            case ConsoleKey.F7: return EscSeq(alt, shift, "18");
            case ConsoleKey.F8: return EscSeq(alt, shift, "19");
            case ConsoleKey.F9: return EscSeq(alt, shift, "20");
            case ConsoleKey.F10: return EscSeq(alt, shift, "21");
            case ConsoleKey.F11: return EscSeq(alt, shift, "23");
            case ConsoleKey.F12: return EscSeq(alt, shift, "24");

            // Arrow keys
            case ConsoleKey.UpArrow: return ArrowSeq(alt, shift, "A");
            case ConsoleKey.DownArrow: return ArrowSeq(alt, shift, "B");
            case ConsoleKey.RightArrow: return ArrowSeq(alt, shift, "C");
            case ConsoleKey.LeftArrow: return ArrowSeq(alt, shift, "D");

            // Navigation
            case ConsoleKey.Home: return ApplicationCursor(alt, "H");
            case ConsoleKey.End: return ApplicationCursor(alt, "F");
            case ConsoleKey.PageUp: return Esc("\x1b[5~");
            case ConsoleKey.PageDown: return Esc("\x1b[6~");
            case ConsoleKey.Insert: return Esc("\x1b[2~");
            case ConsoleKey.Delete: return Esc("\x1b[3~");

            // Tab / Enter / Escape
            case ConsoleKey.Tab:
                return shift ? Esc("\x1b[Z") : new byte[] { 0x09 };
            case ConsoleKey.Enter:
                return new byte[] { 0x0d };
            case ConsoleKey.Escape:
                return new byte[] { 0x1b };
            case ConsoleKey.Backspace:
                return new byte[] { 0x7f };

            // Space
            case ConsoleKey.Spacebar:
                return ctrl ? new byte[] { 0x00 } : new byte[] { 0x20 };
        }

        // Printable characters
        var ch = key.KeyChar;
        if (ch == '\0')
            return Array.Empty<byte>();

        // Ctrl+A-Z → 0x01-0x1a
        if (ctrl)
        {
            var lower = char.ToLowerInvariant(ch);
            if (lower >= 'a' && lower <= 'z')
                return new byte[] { (byte)(lower - 'a' + 1) };

            // Ctrl+[ \ ] ^ _ → 0x1b-0x1f
            if (lower == '-')  return new byte[] { 0x1f }; // Ctrl+_
            if (ch == '[')     return new byte[] { 0x1b }; // Ctrl+[
        }

        // Alt prefix
        if (alt)
        {
            var encoded = Encoding.UTF8.GetBytes(ch.ToString());
            var result = new byte[1 + encoded.Length];
            result[0] = 0x1b; // ESC prefix
            encoded.CopyTo(result, 1);
            return result;
        }

        // Plain character
        return Encoding.UTF8.GetBytes(ch.ToString());
    }

    /// <summary>
    /// Standard arrow key sequence with modifier support.
    /// Normal: ESC [ A  |  Shift: ESC [ 1;2A  |  Alt: ESC [ 1;3A  |  Ctrl: ESC [ 1;5A
    /// </summary>
    private static byte[] ArrowSeq(bool alt, bool shift, string dir)
    {
        if (shift) return Esc($"\x1b[1;2{dir}");
        if (alt) return Esc($"\x1b[1;3{dir}");
        return Esc($"\x1b[{dir}");
    }

    /// <summary>
    /// Home/End with Alt modifier support.
    /// </summary>
    private static byte[] ApplicationCursor(bool alt, string final)
    {
        if (alt) return Esc($"\x1b[1;3{final}");
        return Esc($"\x1b[{final}");
    }

    /// <summary>
    /// Function key escape sequence with Shift and Alt modifiers.
    /// Normal: ESC [ N  |  Shift: ESC [ N;2~  |  Alt: ESC [ N;3~
    /// </summary>
    private static byte[] EscSeq(bool alt, bool shift, string num)
    {
        if (shift) return Esc($"\x1b[{num};2~");
        if (alt) return Esc($"\x1b[{num};3~");
        return Esc($"\x1b[{num}~");
    }

    private static byte[] Esc(string seq) => Encoding.UTF8.GetBytes(seq);
}
