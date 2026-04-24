using DaVinci.Core;
using DaVinci.Events;
using DaVinci.Terminal;

namespace DaVinci.Components;

public enum SpinnerStyle { Dots, Line, Arc, Bounce, Pulse }

public sealed record SpinnerProps : ComponentProps
{
    public string Label { get; init; } = "";
    public SpinnerStyle Style { get; init; } = SpinnerStyle.Dots;
    public Color? Color { get; init; }
}

public sealed class SpinnerState : ComponentState
{
    public int Frame { get; set; }
}

public sealed class SpinnerComponent : Component
{
    private Timer? _timer;

    private static readonly string[][] Frames =
    [
        ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"],     // Dots
        ["-", "\\", "|", "/"],                                              // Line
        ["◜", "◠", "◝", "◞", "◡", "◟"],                                  // Arc
        ["⠁", "⠂", "⠄", "⡀", "⢀", "⠠", "⠐", "⠈"],                     // Bounce
        ["●", "◐", "○", "◐"]                                               // Pulse
    ];

    public SpinnerComponent(ComponentProps props) : base(props)
    {
        State = new SpinnerState();
    }

    protected internal override void OnMount()
    {
        _timer = new Timer(_ =>
        {
            if (State is SpinnerState spinnerState)
            {
                var style = ((SpinnerProps)Props).Style;
                var frameCount = Frames[(int)style].Length;
                var newState = new SpinnerState { Frame = (spinnerState.Frame + 1) % frameCount };
                SetState(newState);
            }
        }, null, TimeSpan.FromMilliseconds(80), TimeSpan.FromMilliseconds(80));
    }

    protected internal override void OnUnmount()
    {
        _timer?.Dispose();
        _timer = null;
    }

    public override int ComputeHeight(int availableWidth) => 1;

    public override void Render(TerminalBuffer buffer)
    {
        var spinnerProps = (SpinnerProps)Props;
        var spinnerState = (SpinnerState?)State ?? new SpinnerState();

        var frames = Frames[(int)spinnerProps.Style];
        var frame = frames[spinnerState.Frame % frames.Length];

        var style = new TextStyle
        {
            Foreground = spinnerProps.Color ?? Color.Cyan
        };

        var text = string.IsNullOrEmpty(spinnerProps.Label)
            ? $" {frame}"
            : $" {frame} {spinnerProps.Label}";

        buffer.SetText(LayoutRect.X, LayoutRect.Y, text, style);
    }
}
