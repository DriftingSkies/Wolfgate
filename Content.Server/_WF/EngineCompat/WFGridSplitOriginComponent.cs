namespace Content.Server._WF.EngineCompat;

/// <summary>The grid this one split off from; engine v291 gives a split-off grid its own origin near its tiles.</summary>
[RegisterComponent, UnsavedComponent]
public sealed partial class WFGridSplitOriginComponent : Component
{
    /// <summary>The grid this one split off from.</summary>
    [ViewVariables]
    public EntityUid Source;

    /// <summary>Added to a tile index on this grid, gives the index the tile had on <see cref="Source"/>.</summary>
    [ViewVariables]
    public Vector2i Offset;
}
