using Content.Shared._WG.Shuttles;

namespace Content.Shared.Maps;

public sealed partial class TileSystem
{
    /// <summary>Records a tool removing the last floor or lattice at a location, which is a design change, not damage.</summary>
    private void WgRecordHullTileDeconstruction(EntityUid grid, Vector2i index)
    {
        var ev = new WGHullTileDeconstructedEvent(index);
        RaiseLocalEvent(grid, ref ev);
    }
}
