using Robust.Shared.Timing;

namespace Content.Shared.Light.Components;

public sealed partial class RoofComponent
{
    /// <summary>Tick each roof chunk last changed, so a state carries only the chunks a client hasn't seen.</summary>
    [ViewVariables]
    public Dictionary<Vector2i, GameTick> WFChunkTicks = new();
}
