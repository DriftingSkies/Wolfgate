using Robust.Shared.Prototypes;

namespace Content.Server._WF.Outposts.Components;

/// <summary>A packed outpost console: used on natural planet ground, it founds an outpost there.</summary>
[RegisterComponent, Access(typeof(WFOutpostSystem))]
public sealed partial class WFOutpostKitComponent : Component
{
    /// <summary>The console that stands in the middle of the new outpost.</summary>
    [DataField]
    public EntProtoId Console = "WFOutpostConsole";

    /// <summary>The floor of the new outpost.</summary>
    [DataField]
    public string Tile = "Plating";

    /// <summary>Side of the square the outpost is founded as, in tiles; odd, so the console stands in the middle.</summary>
    [DataField]
    public int Size = 3;
}
