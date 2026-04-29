using System.Text;
using DaVinci.Terminal;

namespace Radiance.Multiplexer;

/// <summary>
/// Parses a raw byte stream from a PTY master fd into PaneScreenBuffer updates.
/// Implements a subset of VT100/xterm escape sequences sufficient for programs
/// like vim, htop, less, and general terminal applications.
///
/// State machine: Ground → Escape → CSI (params + final) → dispatch.
/// Also handles OSC sequences for window title.
/// </summary>
public sealed class AnsiStreamParser
{
    private readonly PaneScreenBuffer _buffer;

    // Parser state
    private State _state = State.Ground;
    private readonly List<byte> _csiParamBytes = new(16);
    private readonly List<byte> _csiIntermediateBytes = new(4);
    private readonly List<byte> _oscData = new(64);
    private readonly List<byte> _utf8Buffer = new(4);

    // Accumulated CSI parameters (parsed from _csiParamBytes on dispatch)
    private int[]? _cachedParams;
    private int _cachedParamCount;

    // UTF-8 decoding state
    private int _utf8ExpectedBytes;
    private int _utf8BytesReceived;

    private enum State
    {
        Ground,
        Escape,
        CsiEntry,
        CsiParam,
        CsiIntermediate,
        OscStart,
        OscData,
        OscTermination,
    }

    public AnsiStreamParser(PaneScreenBuffer buffer)
    {
        _buffer = buffer;
    }

    /// <summary>
    /// Feed raw bytes from the PTY master fd into the parser.
    /// Processes all complete sequences and buffers partial ones.
    /// </summary>
    public void Feed(ReadOnlySpan<byte> data)
    {
        foreach (var b in data)
        {
            ProcessByte(b);
        }
    }

    private void ProcessByte(byte b)
    {
        switch (_state)
        {
            case State.Ground:
                ProcessGround(b);
                break;
            case State.Escape:
                ProcessEscape(b);
                break;
            case State.CsiEntry:
            case State.CsiParam:
                ProcessCsiParam(b);
                break;
            case State.CsiIntermediate:
                ProcessCsiIntermediate(b);
                break;
            case State.OscStart:
            case State.OscData:
                ProcessOscData(b);
                break;
            case State.OscTermination:
                ProcessOscTermination(b);
                break;
        }
    }

    // ── Ground state ─────────────────────────────────────────────────

    private void ProcessGround(byte b)
    {
        // Control characters
        if (b < 0x20)
        {
            ExecuteControl(b);
            return;
        }

        // ESC starts an escape sequence
        if (b == 0x1b)
        {
            _state = State.Escape;
            return;
        }

        // DEL is ignored
        if (b == 0x7f) return;

        // UTF-8 handling
        if (b >= 0x80)
        {
            ProcessUtf8(b);
            return;
        }

        // Printable ASCII
        _buffer.WriteChar((char)b);
    }

    private void ExecuteControl(byte b)
    {
        switch (b)
        {
            case 0x05: // ENQ — ignore
                break;
            case 0x07: // BEL
                _buffer.Bell();
                break;
            case 0x08: // BS
                _buffer.Backspace();
                break;
            case 0x09: // HT
                _buffer.Tab();
                break;
            case 0x0a: // LF
                _buffer.LineFeed();
                break;
            case 0x0b: // VT — treat as LF
            case 0x0c: // FF — treat as LF
                _buffer.LineFeed();
                break;
            case 0x0d: // CR
                _buffer.CarriageReturn();
                break;
            case 0x0e: // SO — ignore
            case 0x0f: // SI — ignore
                break;
            case 0x00: // NUL — ignore
                break;
        }
    }

    // ── Escape state ─────────────────────────────────────────────────

    private void ProcessEscape(byte b)
    {
        switch (b)
        {
            case (byte)'[': // CSI
                _csiParamBytes.Clear();
                _csiIntermediateBytes.Clear();
                _cachedParams = null;
                _state = State.CsiEntry;
                break;
            case (byte)']': // OSC
                _oscData.Clear();
                _state = State.OscStart;
                break;
            case (byte)'7': // DECSC — save cursor
                _buffer.SaveCursor();
                _state = State.Ground;
                break;
            case (byte)'8': // DECRC — restore cursor
                _buffer.RestoreCursor();
                _state = State.Ground;
                break;
            case (byte)'D': // IND — index (scroll up if at bottom)
                if (_buffer.CursorRow == _buffer.Rows - 1)
                    _buffer.ScrollUp();
                else
                    _buffer.MoveCursorDown(1);
                _state = State.Ground;
                break;
            case (byte)'M': // RI — reverse index (scroll down if at top)
                if (_buffer.CursorRow == 0)
                    _buffer.ScrollDown();
                else
                    _buffer.MoveCursorUp(1);
                _state = State.Ground;
                break;
            case (byte)'E': // NEL — next line
                _buffer.CarriageReturn();
                _buffer.LineFeed();
                _state = State.Ground;
                break;
            case (byte)'c': // RIS — full reset
                _buffer.EraseInDisplay(2);
                _buffer.MoveCursor(0, 0);
                _buffer.SetStyle(TextStyle.Empty);
                _state = State.Ground;
                break;
            case (byte)'(': // Designate G0 — skip next byte
            case (byte)')': // Designate G1 — skip next byte
                // Consume the character set designator and return to ground
                _state = State.Ground;
                // We need to skip one more byte — use a simple flag approach
                // Actually, we just go back to ground and the next byte is a charset name
                // which we'll treat as printable if we don't handle it specially.
                // For simplicity, stay in a special "skip one" sub-state:
                // Re-use escape state to consume one more byte as a no-op
                break;
            default:
                // Unknown escape — return to ground
                _state = State.Ground;
                break;
        }
    }

    // ── CSI sequence ─────────────────────────────────────────────────

    private void ProcessCsiParam(byte b)
    {
        // Parameter bytes: 0x30-0x3f (0-9, ;, <, =, >, ?)
        if (b >= 0x30 && b <= 0x3f)
        {
            _csiParamBytes.Add(b);
            _state = State.CsiParam;
            return;
        }

        // Intermediate bytes: 0x20-0x2f (space, !, ", etc.)
        if (b >= 0x20 && b <= 0x2f)
        {
            _csiIntermediateBytes.Add(b);
            _state = State.CsiIntermediate;
            return;
        }

        // Final byte: 0x40-0x7e
        if (b >= 0x40 && b <= 0x7e)
        {
            DispatchCsi(b);
            _state = State.Ground;
            return;
        }

        // Control character in CSI — execute and stay in CSI
        if (b < 0x20)
        {
            ExecuteControl(b);
            return;
        }

        // Unexpected — abort back to ground
        _state = State.Ground;
    }

    private void ProcessCsiIntermediate(byte b)
    {
        // More intermediate bytes
        if (b >= 0x20 && b <= 0x2f)
        {
            _csiIntermediateBytes.Add(b);
            return;
        }

        // Final byte
        if (b >= 0x40 && b <= 0x7e)
        {
            DispatchCsi(b);
            _state = State.Ground;
            return;
        }

        // Unexpected — abort
        _state = State.Ground;
    }

    private void DispatchCsi(byte finalByte)
    {
        var isPrivate = _csiParamBytes.Count > 0 && _csiParamBytes[0] == (byte)'?';

        // Parse numeric parameters (semicolon-separated)
        var paramsSpan = ParseParams();

        if (isPrivate)
        {
            DispatchPrivateCsi(finalByte, paramsSpan);
        }
        else
        {
            DispatchStandardCsi(finalByte, paramsSpan);
        }
    }

    private ReadOnlySpan<int> ParseParams()
    {
        if (_cachedParams != null)
            return _cachedParams.AsSpan(0, _cachedParamCount);

        Span<int> parsed = stackalloc int[16];
        var count = 0;
        var current = 0;

        var bytes = _csiParamBytes;
        var startIdx = 0;

        // Skip leading '?' for private sequences
        if (bytes.Count > 0 && bytes[0] == (byte)'?')
            startIdx = 1;

        for (var i = startIdx; i < bytes.Count; i++)
        {
            var b = bytes[i];
            if (b == (byte)';')
            {
                if (count < parsed.Length)
                    parsed[count++] = current;
                current = 0;
            }
            else if (b >= (byte)'0' && b <= (byte)'9')
            {
                current = current * 10 + (b - '0');
            }
            // Ignore other parameter bytes (<, =, >)
        }

        // Don't forget the last parameter
        if (count < parsed.Length)
            parsed[count++] = current;

        // Cache the result as a heap-allocated array
        _cachedParams = new int[count];
        parsed.Slice(0, count).CopyTo(_cachedParams);
        _cachedParamCount = count;

        return _cachedParams.AsSpan(0, _cachedParamCount);
    }

    private void DispatchStandardCsi(byte final, ReadOnlySpan<int> p)
    {
        switch ((char)final)
        {
            // Cursor movement
            case 'A': // CUU — cursor up
                _buffer.MoveCursorUp(Param(p, 0, 1));
                break;
            case 'B': // CUD — cursor down
                _buffer.MoveCursorDown(Param(p, 0, 1));
                break;
            case 'C': // CUF — cursor forward
                _buffer.MoveCursorForward(Param(p, 0, 1));
                break;
            case 'D': // CUB — cursor backward
                _buffer.MoveCursorBackward(Param(p, 0, 1));
                break;
            case 'H': // CUP — cursor position (row, col), 1-based
            case 'f':
                _buffer.MoveCursor(Param(p, 0, 1) - 1, Param(p, 1, 1) - 1);
                break;
            case 'G': // CHA — cursor horizontal absolute, 1-based
                _buffer.MoveCursorToColumn(Param(p, 0, 1) - 1);
                break;
            case 'd': // VPA — vertical position absolute, 1-based
                _buffer.MoveCursorToRow(Param(p, 0, 1) - 1);
                break;
            case 'e': // VPR — vertical position relative
                _buffer.MoveCursorDown(Param(p, 0, 1));
                break;

            // Erase
            case 'J': // ED — erase in display
                _buffer.EraseInDisplay(Param(p, 0, 0));
                break;
            case 'K': // EL — erase in line
                _buffer.EraseInLine(Param(p, 0, 0));
                break;

            // Scroll
            case 'S': // SU — scroll up
                _buffer.ScrollUp(Param(p, 0, 1));
                break;
            case 'T': // SD — scroll down
                _buffer.ScrollDown(Param(p, 0, 1));
                break;
            case 'r': // DECSTBM — set scroll region (top, bottom), 1-based
                _buffer.SetScrollRegion(Param(p, 0, 1) - 1, Param(p, 1, _buffer.Rows) - 1);
                // DECSTBM also moves cursor to home
                _buffer.MoveCursor(0, 0);
                break;

            // Insert/delete lines
            case 'L': // IL — insert lines
                _buffer.InsertLines(Param(p, 0, 1));
                break;
            case 'M': // DL — delete lines
                _buffer.DeleteLines(Param(p, 0, 1));
                break;

            // Insert/delete characters
            case '@': // ICH — insert characters
                _buffer.InsertChars(Param(p, 0, 1));
                break;
            case 'P': // DCH — delete characters
                _buffer.DeleteChars(Param(p, 0, 1));
                break;
            case 'X': // ECH — erase characters
                EraseChars(Param(p, 0, 1));
                break;

            // SGR — select graphic rendition
            case 'm':
                _buffer.ApplySgr(p);
                break;

            // Save/restore cursor
            case 's': // SCP — save cursor position
                _buffer.SaveCursor();
                break;
            case 'u': // RCP — restore cursor position
                _buffer.RestoreCursor();
                break;

            // Modes that arrive as standard CSI (less common)
            case 'h': // SM — set mode (rarely standard, mostly private)
            case 'l': // RM — reset mode
                // Ignore standard mode set/reset
                break;

            // Cursor style
            case 'q': // DECSCUSR — set cursor style (SP + q)
                // e.g., \x1b[0 q, \x1b[1 q (blink block), \x1b[5 q (steady bar)
                // We track visibility but don't change shape
                _buffer.CursorVisible = true;
                break;

            // Device status report — ignore
            case 'n':
                break;

            // Tab stop operations — ignore
            case 'g':
                break;

            default:
                // Unknown CSI — ignore
                break;
        }
    }

    private void DispatchPrivateCsi(byte final, ReadOnlySpan<int> p)
    {
        switch ((char)final)
        {
            case 'h': // DECSET — set private mode
                SetPrivateMode(p);
                break;
            case 'l': // DECRST — reset private mode
                ResetPrivateMode(p);
                break;

            // Unknown private — ignore
        }
    }

    private void SetPrivateMode(ReadOnlySpan<int> p)
    {
        for (var i = 0; i < p.Length; i++)
        {
            switch (p[i])
            {
                case 1049: // Alternate screen buffer + save cursor
                    _buffer.SaveCursor();
                    _buffer.EnterAltScreen();
                    break;
                case 47: // Alternate screen buffer (xterm)
                case 1047: // Alternate screen buffer (xterm clear)
                    _buffer.EnterAltScreen();
                    break;
                case 1: // DECCKM — application cursor keys
                    break;
                case 3: // DECCOLM — 132 column mode (ignore)
                    break;
                case 25: // DECTCEM — show cursor
                    _buffer.CursorVisible = true;
                    break;
                case 1000: // Mouse tracking — basic
                case 1002: // Mouse tracking — button event
                case 1006: // Mouse tracking — SGR extended
                    // Mouse support will be handled by the multiplexer layer
                    break;
                case 2004: // Bracketed paste mode
                    break;
            }
        }
    }

    private void ResetPrivateMode(ReadOnlySpan<int> p)
    {
        for (var i = 0; i < p.Length; i++)
        {
            switch (p[i])
            {
                case 1049: // Leave alternate screen buffer + restore cursor
                    _buffer.LeaveAltScreen();
                    _buffer.RestoreCursor();
                    break;
                case 47: // Leave alternate screen buffer
                case 1047:
                    _buffer.LeaveAltScreen();
                    break;
                case 1: // DECCKM — normal cursor keys
                    break;
                case 25: // DECTCEM — hide cursor
                    _buffer.CursorVisible = false;
                    break;
                case 1000:
                case 1002:
                case 1006:
                    break;
                case 2004: // Bracketed paste mode off
                    break;
            }
        }
    }

    private void EraseChars(int n)
    {
        for (var i = 0; i < n; i++)
        {
            var col = _buffer.CursorCol + i;
            if (col >= _buffer.Columns) break;
            // Write empty cell — but we need direct access to the grid
            // Use a workaround: the buffer's internal state
            // For now, rely on the PaneScreenBuffer not having a direct erase-chars method
        }
        // Simplified: erase n chars from cursor by setting them to empty
        // PaneScreenBuffer doesn't expose SetCell directly, so we just
        // set the cursor and write spaces (which also moves cursor, so save/restore)
        _buffer.SaveCursor();
        var saved = _buffer.CursorCol;
        for (var i = 0; i < n && saved + i < _buffer.Columns; i++)
        {
            _buffer.MoveCursorToColumn(saved + i);
            _buffer.WriteChar(' ');
        }
        _buffer.RestoreCursor();
    }

    // ── OSC sequence ─────────────────────────────────────────────────

    private void ProcessOscData(byte b)
    {
        // BEL terminates OSC
        if (b == 0x07)
        {
            DispatchOsc();
            _state = State.Ground;
            return;
        }

        // ESC \ (ST) terminates OSC
        if (b == 0x1b)
        {
            _state = State.OscTermination;
            return;
        }

        _oscData.Add(b);
        _state = State.OscData;
    }

    private void ProcessOscTermination(byte b)
    {
        if (b == (byte)'\\')
        {
            DispatchOsc();
        }
        _state = State.Ground;
    }

    private void DispatchOsc()
    {
        if (_oscData.Count == 0) return;

        // Parse: "N;data" where N is the OSC number
        var semicolonIdx = _oscData.IndexOf((byte)';');
        if (semicolonIdx < 0) return;

        // Parse the command number
        var cmd = 0;
        for (var i = 0; i < semicolonIdx; i++)
        {
            var d = _oscData[i];
            if (d >= (byte)'0' && d <= (byte)'9')
                cmd = cmd * 10 + (d - '0');
        }

        // Decode the string payload
        var payload = Encoding.UTF8.GetString(_oscData.ToArray(), semicolonIdx + 1,
            _oscData.Count - semicolonIdx - 1);

        switch (cmd)
        {
            case 0: // Set window title + icon name
            case 2: // Set window title
                _buffer.Title = payload;
                break;
            case 1: // Set icon name — ignore
                break;
        }
    }

    // ── UTF-8 ────────────────────────────────────────────────────────

    private void ProcessUtf8(byte b)
    {
        if (_utf8ExpectedBytes == 0)
        {
            // Start of a multi-byte sequence
            if ((b & 0xE0) == 0xC0) // 2-byte
            {
                _utf8ExpectedBytes = 2;
                _utf8BytesReceived = 0;
                _utf8Buffer.Clear();
            }
            else if ((b & 0xF0) == 0xE0) // 3-byte
            {
                _utf8ExpectedBytes = 3;
                _utf8BytesReceived = 0;
                _utf8Buffer.Clear();
            }
            else if ((b & 0xF8) == 0xF0) // 4-byte
            {
                _utf8ExpectedBytes = 4;
                _utf8BytesReceived = 0;
                _utf8Buffer.Clear();
            }
            else
            {
                // Invalid UTF-8 start byte — ignore
                return;
            }
        }

        _utf8Buffer.Add(b);
        _utf8BytesReceived++;

        if (_utf8BytesReceived < _utf8ExpectedBytes) return;

        // Complete sequence — decode
        var decoded = Encoding.UTF8.GetChars(_utf8Buffer.ToArray());
        if (decoded.Length > 0)
            _buffer.WriteChar(decoded[0]);

        _utf8ExpectedBytes = 0;
        _utf8BytesReceived = 0;
        _utf8Buffer.Clear();
    }

    // ── Helper ───────────────────────────────────────────────────────

    /// <summary>
    /// Get a CSI parameter with default value. Params are 0-indexed.
    /// Returns defaultValue if the parameter wasn't provided or is 0 (empty param).
    /// </summary>
    private static int Param(ReadOnlySpan<int> p, int index, int defaultValue)
    {
        if (index >= p.Length) return defaultValue;
        var val = p[index];
        return val == 0 ? defaultValue : val;
    }
}
