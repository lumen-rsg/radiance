namespace DaVinci.Events;

public sealed class KeyPressEvent(char key, ConsoleKey keyCode, ConsoleModifiers modifiers)
    : TerminalEvent
{
    public char Key { get; } = key;
    public ConsoleKey KeyCode { get; } = keyCode;
    public ConsoleModifiers Modifiers { get; } = modifiers;
}
