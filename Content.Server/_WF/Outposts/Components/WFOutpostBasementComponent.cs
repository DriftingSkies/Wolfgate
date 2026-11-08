using Robust.Shared.Map;

namespace Content.Server._WF.Outposts.Components;

/// <summary>An outpost's basement: a grid one depth down, on the cavern under it, lined up tile for tile with its floor.</summary>
[RegisterComponent, Access(typeof(WFOutpostSystem))]
public sealed partial class WFOutpostBasementComponent : Component
{
    /// <summary>How far below the outpost the grid sits; saved with it.</summary>
    [DataField]
    public int Depth = -1;

    /// <summary>The outpost above.</summary>
    [ViewVariables]
    public EntityUid Outpost;

    /// <summary>Cavern tiles dug out for the basement, by cavern index, put back when it goes.</summary>
    [ViewVariables]
    public Dictionary<Vector2i, Tile> Carved = new();
}
