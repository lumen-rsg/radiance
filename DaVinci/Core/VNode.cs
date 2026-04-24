namespace DaVinci.Core;

public sealed class VNode
{
    public Type ComponentType { get; init; } = null!;
    public ComponentProps Props { get; init; } = null!;
    public string? Key { get; init; }
    public List<VNode> Children { get; init; } = [];
    public Component? Instance { get; set; }
}
