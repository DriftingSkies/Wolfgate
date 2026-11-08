using Robust.Shared.Prototypes;

namespace Content.Server._WF.Outposts.Components;

/// <summary>A basement hatch kit: used on an outpost's floor, it digs a room below and opens a ladder down to it.</summary>
[RegisterComponent, Access(typeof(WFOutpostSystem))]
public sealed partial class WFOutpostHatchKitComponent : Component
{
    /// <summary>The ladder set in the basement under the hatch.</summary>
    [DataField]
    public EntProtoId Ladder = "WFOutpostBasementLadder";

    /// <summary>The pillar that binds the basement to the outpost above, set in a corner of the room.</summary>
    [DataField]
    public EntProtoId Connector = "CEZGridConnector";

    /// <summary>The floor of the room dug below.</summary>
    [DataField]
    public string Tile = "Plating";

    /// <summary>Tiles the room reaches out from the hatch on each side.</summary>
    [DataField]
    public int Reach = 1;
}
