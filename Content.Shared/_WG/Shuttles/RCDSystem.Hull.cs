using Content.Shared._WG.Shuttles;

namespace Content.Shared.RCD.Systems;

public partial class RCDSystem
{
    /// <summary>Records successful RCD removal separately from damage or arbitrary entity deletion.</summary>
    private void WgRecordHullDeconstruction(EntityUid target)
    {
        var ev = new WGHullDeconstructedEvent();
        RaiseLocalEvent(target, ref ev);
    }

    /// <summary>Records an RCD removing the last floor or lattice at a location, which is a design change, not damage.</summary>
    private void WgRecordHullTileDeconstruction(EntityUid grid, Vector2i index)
    {
        var ev = new WGHullTileDeconstructedEvent(index);
        RaiseLocalEvent(grid, ref ev);
    }
}
