using Content.Server._WG.CargoLoad;
using Content.Shared._Mono.Ships;

namespace Content.Server.Shuttles.Systems;

public sealed partial class ShuttleConsoleSystem
{
    [Dependency] private WGCargoLoadSystem _wgCargoLoad = default!;

    /// <summary>Seconds of FTL spool the cargo aboard adds to a jump.</summary>
    private float WgCargoSpoolPenalty(EntityUid shuttle, FTLDriveComponent drive) => _wgCargoLoad.SpoolPenalty(shuttle, drive);
}
