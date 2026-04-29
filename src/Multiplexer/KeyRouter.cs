using Radiance.Interop;

namespace Radiance.Multiplexer;

/// <summary>
/// Commands dispatched by the KeyRouter after a Ctrl+B prefix sequence.
/// </summary>
public enum MuxCommand
{
    None,
    CreatePane,         // c — create a new pane
    CreatePaneH,        // | — split horizontally (side by side)
    KillPane,           // x — kill the active pane
    NextPane,           // o — cycle to next pane
    PrevPane,           // ; — cycle to previous pane
    PaneUp,             // Up — switch to pane above
    PaneDown,           // Down — switch to pane below
    PaneLeft,           // Left — switch to pane left
    PaneRight,          // Right — switch to pane right
    NextWindow,         // n — next window
    PrevWindow,         // p — previous window
    NewWindow,          // c — create new window (if no pane concept)
    KillWindow,         // & — kill current window
    RenameWindow,       // , — rename current window
    Detach,             // d — detach from session
    ZoomPane,           // z — toggle pane zoom
    ResizeUp,           // Alt+Up — resize pane up
    ResizeDown,         // Alt+Down — resize pane down
    ResizeLeft,         // Alt+Left — resize pane left
    ResizeRight,        // Alt+Right — resize pane right
    ScrollUp,           // [ — enter copy/scroll mode
    CopyMode,           // [ — alias
    PasteBuffer,        // ] — paste from buffer
    ClockMode,          // t — show clock
    ChooseSession,      // s — choose session
    ChooseWindow,       // w — choose window
    SwapPaneUp,         // { — swap pane with previous
    SwapPaneDown,       // } — swap pane with next
    BreakPane,          // ! — break pane into its own window
    LastWindow,         // l — last active window
    CommandPrompt,      // : — enter command prompt
    Refresh,            // r — refresh client
}

/// <summary>
/// Routes keyboard input for the multiplexer. Implements the Ctrl+B prefix
/// state machine: if Ctrl+B is detected, the next key is interpreted as a
/// multiplexer command; otherwise, the key is forwarded to the active pane.
/// </summary>
public sealed class KeyRouter
{
    private bool _inPrefix;
    private readonly List<MuxPane> _panes;
    private readonly ConsoleKey _prefixKey;
    private readonly char _prefixCtrlChar; // The control character (e.g., 0x02 for Ctrl+B)
    private readonly string _prefixLabel;  // Display label (e.g., "Ctrl+B")

    /// <summary>The prefix label for status display (e.g., "Ctrl+B").</summary>
    public string PrefixLabel => _prefixLabel;

    /// <summary>Index of the currently active pane.</summary>
    public int ActivePaneIndex { get; set; }

    /// <summary>Fired when a multiplexer command is recognized.</summary>
    public event Action<MuxCommand>? OnCommand;

    /// <summary>Whether we're waiting for the second key after Ctrl+B.</summary>
    public bool InPrefix => _inPrefix;

    public KeyRouter(List<MuxPane> panes, string? prefixKey = null)
    {
        _panes = panes;

        // Resolve prefix key: env var > parameter > default (Ctrl+B)
        var prefix = prefixKey
            ?? Environment.GetEnvironmentVariable("MUX_PREFIX")
            ?? "Ctrl+B";

        (_prefixKey, _prefixCtrlChar, _prefixLabel) = prefix.ToUpperInvariant() switch
        {
            "CTRL+A" => (ConsoleKey.A, '\x01', "Ctrl+A"),
            "CTRL+B" => (ConsoleKey.B, '\x02', "Ctrl+B"),
            "CTRL+C" => (ConsoleKey.C, '\x03', "Ctrl+C"),
            "CTRL+O" => (ConsoleKey.O, '\x0f', "Ctrl+O"),
            "CTRL+S" => (ConsoleKey.S, '\x13', "Ctrl+S"),
            "CTRL+Z" => (ConsoleKey.Z, '\x1a', "Ctrl+Z"),
            // Single-letter shorthand: "a" → Ctrl+A, "b" → Ctrl+B
            var c when c.Length == 1 && c[0] is >= 'A' and <= 'Z'
                => (Enum.Parse<ConsoleKey>(c), (char)(c[0] - 'A' + 1), $"Ctrl+{c}"),
            // Default to Ctrl+B
            _ => (ConsoleKey.B, '\x02', "Ctrl+B")
        };
    }

    /// <summary>
    /// Process a key press. Returns true if the key was consumed by the
    /// multiplexer (prefix or command), false if it should be forwarded
    /// to the active pane's child process.
    /// </summary>
    public bool ProcessKey(ConsoleKeyInfo key)
    {
        // Detect Ctrl+B prefix
        if (IsPrefixKey(key))
        {
            if (_inPrefix)
            {
                // Double Ctrl+B → send literal Ctrl+B to child
                _inPrefix = false;
                return false;
            }

            _inPrefix = true;
            return true;
        }

        // If in prefix mode, interpret as a command
        if (_inPrefix)
        {
            _inPrefix = false;
            var cmd = ParseCommand(key);
            if (cmd != MuxCommand.None)
            {
                OnCommand?.Invoke(cmd);
                return true;
            }

            // Unrecognized command — consume the key silently
            return true;
        }

        // Normal key — forward to active pane
        return false;
    }

    /// <summary>
    /// Get the active pane (safe accessor).
    /// </summary>
    public MuxPane? ActivePane =>
        _panes.Count > 0 && ActivePaneIndex >= 0 && ActivePaneIndex < _panes.Count
            ? _panes[ActivePaneIndex]
            : null;

    private bool IsPrefixKey(ConsoleKeyInfo key)
    {
        return (key.KeyChar == _prefixCtrlChar) ||
               (key.Key == _prefixKey && key.Modifiers.HasFlag(ConsoleModifiers.Control));
    }

    private static MuxCommand ParseCommand(ConsoleKeyInfo key)
    {
        var ch = char.ToLowerInvariant(key.KeyChar);
        var noMod = key.Modifiers == 0 || key.Modifiers == ConsoleModifiers.Shift;

        // Shift-modified keys: check the key char directly
        if (key.Modifiers.HasFlag(ConsoleModifiers.Shift))
        {
            return ch switch
            {
                '|' => MuxCommand.CreatePaneH,
                '{' => MuxCommand.SwapPaneUp,
                '}' => MuxCommand.SwapPaneDown,
                '!' => MuxCommand.BreakPane,
                _ => MuxCommand.None
            };
        }

        // Alt-modified arrow keys (resize)
        if (key.Modifiers.HasFlag(ConsoleModifiers.Alt))
        {
            return key.Key switch
            {
                ConsoleKey.UpArrow => MuxCommand.ResizeUp,
                ConsoleKey.DownArrow => MuxCommand.ResizeDown,
                ConsoleKey.LeftArrow => MuxCommand.ResizeLeft,
                ConsoleKey.RightArrow => MuxCommand.ResizeRight,
                _ => MuxCommand.None
            };
        }

        // Arrow keys (pane navigation)
        switch (key.Key)
        {
            case ConsoleKey.UpArrow: return MuxCommand.PaneUp;
            case ConsoleKey.DownArrow: return MuxCommand.PaneDown;
            case ConsoleKey.LeftArrow: return MuxCommand.PaneLeft;
            case ConsoleKey.RightArrow: return MuxCommand.PaneRight;
        }

        // Character commands
        return ch switch
        {
            'c' => MuxCommand.CreatePane,
            'x' => MuxCommand.KillPane,
            'o' => MuxCommand.NextPane,
            ';' => MuxCommand.PrevPane,
            'n' => MuxCommand.NextWindow,
            'p' => MuxCommand.PrevWindow,
            '&' => MuxCommand.KillWindow,
            ',' => MuxCommand.RenameWindow,
            'd' => MuxCommand.Detach,
            'z' => MuxCommand.ZoomPane,
            '[' => MuxCommand.CopyMode,
            ']' => MuxCommand.PasteBuffer,
            't' => MuxCommand.ClockMode,
            's' => MuxCommand.ChooseSession,
            'w' => MuxCommand.ChooseWindow,
            'l' => MuxCommand.LastWindow,
            ':' => MuxCommand.CommandPrompt,
            'r' => MuxCommand.Refresh,
            '%' => MuxCommand.CreatePaneH,
            _ => MuxCommand.None
        };
    }
}
