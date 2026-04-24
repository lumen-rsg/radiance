using DaVinci.Core;
using DaVinci.Events;
using DaVinci.Terminal;

namespace DaVinci.Components;

public sealed record SelectorProps : ComponentProps
{
    public IReadOnlyList<string> Options { get; init; } = [];
    public int SelectedIndex { get; init; } = 0;
    public bool MultiSelect { get; init; }
    public TextStyle? SelectedStyle { get; init; }
    public TextStyle? ActiveStyle { get; init; }
    public string Checkbox { get; init; } = "○";
    public string CheckboxSelected { get; init; } = "●";
    public Action<int, bool>? OnSelect { get; init; }
    public Action<int>? OnConfirm { get; init; }
}

public sealed class SelectorState : ComponentState
{
    public int CursorIndex { get; set; }
    public HashSet<int> SelectedIndices { get; } = [];
}

public sealed class SelectorComponent : Component
{
    private Action<KeyPressEvent>? _keyHandler;

    public SelectorComponent(ComponentProps props) : base(props)
    {
        State = new SelectorState();
    }

    protected internal override void OnMount()
    {
        var selectorProps = (SelectorProps)Props;
        var state = (SelectorState)State!;
        state.CursorIndex = Math.Clamp(selectorProps.SelectedIndex, 0,
            Math.Max(0, selectorProps.Options.Count - 1));

        if (selectorProps.SelectedIndex >= 0 && selectorProps.SelectedIndex < selectorProps.Options.Count)
            state.SelectedIndices.Add(selectorProps.SelectedIndex);
    }

    protected internal override void OnUnmount()
    {
        _keyHandler = null;
    }

    private void HandleKey(KeyPressEvent evt, SelectorProps props)
    {
        var state = (SelectorState)State!;
        var changed = false;

        switch (evt.KeyCode)
        {
            case ConsoleKey.UpArrow:
                if (state.CursorIndex > 0)
                {
                    state.CursorIndex--;
                    changed = true;
                }
                break;

            case ConsoleKey.DownArrow:
                if (state.CursorIndex < props.Options.Count - 1)
                {
                    state.CursorIndex++;
                    changed = true;
                }
                break;

            case ConsoleKey.Spacebar when props.MultiSelect:
                if (state.SelectedIndices.Contains(state.CursorIndex))
                    state.SelectedIndices.Remove(state.CursorIndex);
                else
                    state.SelectedIndices.Add(state.CursorIndex);
                changed = true;
                break;

            case ConsoleKey.Enter:
                if (props.MultiSelect)
                    props.OnConfirm?.Invoke(state.CursorIndex);
                else
                    props.OnSelect?.Invoke(state.CursorIndex, true);
                break;
        }

        if (changed)
        {
            SetState(state);
            props.OnSelect?.Invoke(state.CursorIndex, state.SelectedIndices.Contains(state.CursorIndex));
        }
    }

    public override int ComputeHeight(int availableWidth)
    {
        var selectorProps = (SelectorProps)Props;
        return selectorProps.Options.Count;
    }

    public override void Render(TerminalBuffer buffer)
    {
        var selectorProps = (SelectorProps)Props;
        var state = (SelectorState?)State ?? new SelectorState();

        var activeStyle = selectorProps.ActiveStyle ?? new TextStyle
        {
            Foreground = Color.BrightCyan,
            Bold = true
        };
        var selectedStyle = selectorProps.SelectedStyle ?? new TextStyle
        {
            Foreground = Color.Green
        };
        var normalStyle = TextStyle.Empty;

        for (var i = 0; i < selectorProps.Options.Count; i++)
        {
            var row = LayoutRect.Y + i;
            if (row >= buffer.Height) break;

            var isCursor = i == state.CursorIndex;
            var isSelected = state.SelectedIndices.Contains(i);

            var prefix = selectorProps.MultiSelect
                ? (isSelected ? $" {selectorProps.CheckboxSelected} " : $" {selectorProps.Checkbox} ")
                : (isCursor ? " > " : "   ");

            var optionStyle = isCursor ? activeStyle : isSelected ? selectedStyle : normalStyle;
            var prefixStyle = isCursor ? activeStyle : new TextStyle { Dim = true };

            buffer.SetText(LayoutRect.X, row, prefix, prefixStyle);

            var text = selectorProps.Options[i];
            var textX = LayoutRect.X + prefix.Length;

            // Truncate if needed
            var maxWidth = LayoutRect.Width - prefix.Length;
            if (AnsiCodes.VisibleWidth(text) > maxWidth)
                text = text[..maxWidth];

            buffer.SetText(textX, row, text, optionStyle);
        }
    }
}
