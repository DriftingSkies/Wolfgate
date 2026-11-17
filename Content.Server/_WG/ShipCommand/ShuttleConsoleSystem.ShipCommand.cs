using Content.Server._WG.ShipCommand;
using Content.Shared._WG.ShipCommand;

namespace Content.Server.Shuttles.Systems;

public sealed partial class ShuttleConsoleSystem
{
    [Dependency] private WGShipCommandSystem _wgShipCommand = default!;

    /// <summary>A helm kept for officers opens only for officers and up of the commanded ship it stands on.</summary>
    private bool WgOfficersAllowPilot(EntityUid user, EntityUid console)
    {
        return !HasComp<WGOfficerHelmComponent>(console)
               || Transform(console).GridUid is not { } grid || !HasComp<WGShipCommandComponent>(grid)
               || _wgShipCommand.CanOperate(user, grid);
    }
}
