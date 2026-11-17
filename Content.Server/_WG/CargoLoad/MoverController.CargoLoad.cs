using Content.Server._WG.CargoLoad;

namespace Content.Server.Physics.Controllers;

public sealed partial class MoverController
{
    [Dependency] private WGCargoLoadSystem _wgCargoLoad = default!;

    /// <summary>Share of thrust a grid's docked set keeps in space under its cargo.</summary>
    private float WgCargoSpaceFactor(EntityUid grid) => _wgCargoLoad.GetSpaceFactor(grid);
}
