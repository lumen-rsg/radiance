using System.Text;
using DaVinci.Core;
using DaVinci.Events;
using DaVinci.Terminal;

namespace DaVinci.Components;

public sealed record InputProps : ComponentProps
{
    public string Placeholder { get; init; } = "";
    public string Value { get; init; } = "";
    public TextStyle? Style { get; init; }
    public bool Password { get; init; }
    public string? Prompt { get; init; }
    public Action<string>? OnSubmit { get; init; }
    public Action<string>? OnChange { get; init; }
}

public sealed class InputState : ComponentState
{
    public StringBuilder Buffer { get; } = new();
    public int CursorPosition { get; set; }
    public string DisplayValue { get; set; } = "";
}

public sealed class InputComponent : Component
{
    private Action<KeyPressEvent>? _keyHandler;

    public InputComponent(ComponentProps props) : base(props)
    {
        State = new InputState();
    }

    protected internal override void OnMount()
    {
        var inputProps = (InputProps)Props;
        var state = (InputState)State!;

        if (!string.IsNullOrEmpty(inputProps.Value))
        {
            state.Buffer.Clear();
            state.Buffer.Append(inputProps.Value);
            state.CursorPosition = inputProps.Value.Length;
            state.DisplayValue = MaskValue(inputProps.Value, inputProps.Password);
        }

        _keyHandler = evt =>
        {
            HandleKey(evt, inputProps);
        };
    }

    protected internal override void OnUnmount()
    {
        _keyHandler = null;
    }

    private void HandleKey(KeyPressEvent evt, InputProps props)
    {
        var state = (InputState)State!;
        var changed = false;

        if (evt.KeyCode == ConsoleKey.Enter)
        {
            props.OnSubmit?.Invoke(state.Buffer.ToString());
            return;
        }

        if (evt.KeyCode == ConsoleKey.Backspace)
        {
            if (state.CursorPosition > 0)
            {
                state.Buffer.Remove(state.CursorPosition - 1, 1);
                state.CursorPosition--;
                changed = true;
            }
        }
        else if (evt.KeyCode == ConsoleKey.Delete)
        {
            if (state.CursorPosition < state.Buffer.Length)
            {
                state.Buffer.Remove(state.CursorPosition, 1);
                changed = true;
            }
        }
        else if (evt.KeyCode == ConsoleKey.LeftArrow)
        {
            if (state.CursorPosition > 0)
                state.CursorPosition--;
        }
        else if (evt.KeyCode == ConsoleKey.RightArrow)
        {
            if (state.CursorPosition < state.Buffer.Length)
                state.CursorPosition++;
        }
        else if (evt.KeyCode == ConsoleKey.Home)
        {
            state.CursorPosition = 0;
        }
        else if (evt.KeyCode == ConsoleKey.End)
        {
            state.CursorPosition = state.Buffer.Length;
        }
        else if (!char.IsControl(evt.Key) && evt.Key != '\0')
        {
            state.Buffer.Insert(state.CursorPosition, evt.Key);
            state.CursorPosition++;
            changed = true;
        }

        if (changed)
        {
            state.DisplayValue = MaskValue(state.Buffer.ToString(), props.Password);
            SetState(state);
            props.OnChange?.Invoke(state.Buffer.ToString());
        }
        else
        {
            // Cursor-only change, still need re-render
            SetState(state);
        }
    }

    public override int ComputeHeight(int availableWidth) => 1;

    public override void Render(TerminalBuffer buffer)
    {
        var inputProps = (InputProps)Props;
        var state = (InputState?)State ?? new InputState();
        var style = inputProps.Style ?? TextStyle.Empty;

        var x = LayoutRect.X;
        var width = LayoutRect.Width;

        // Render prompt
        if (!string.IsNullOrEmpty(inputProps.Prompt))
        {
            buffer.SetText(x, LayoutRect.Y, inputProps.Prompt, new TextStyle { Bold = true });
            x += inputProps.Prompt.Length;
            width -= inputProps.Prompt.Length;
        }

        // Render value or placeholder
        if (state.Buffer.Length > 0)
        {
            var value = state.DisplayValue;
            var cursorOffset = state.CursorPosition;

            // Scroll if cursor is beyond visible area
            if (cursorOffset >= width)
            {
                var start = cursorOffset - width + 1;
                value = value[start..];
                cursorOffset = width - 1;
            }

            buffer.SetText(x, LayoutRect.Y, value, style);

            // Show cursor position
            if (cursorOffset < value.Length)
            {
                var cursorChar = cursorOffset < state.Buffer.Length
                    ? state.Buffer[cursorOffset]
                    : ' ';
                buffer.SetCell(x + cursorOffset, LayoutRect.Y,
                    Cell.FromChar(cursorChar, new TextStyle { Reverse = true }));
            }
        }
        else
        {
            // Show placeholder
            var placeholder = inputProps.Placeholder;
            if (placeholder.Length > width)
                placeholder = placeholder[..width];

            buffer.SetText(x, LayoutRect.Y, placeholder, new TextStyle { Dim = true });

            // Cursor at start
            if (placeholder.Length > 0)
            {
                buffer.SetCell(x, LayoutRect.Y,
                    Cell.FromChar(placeholder[0], new TextStyle { Reverse = true }));
            }
        }
    }

    private static string MaskValue(string value, bool password)
    {
        return password ? new string('*', value.Length) : value;
    }
}
