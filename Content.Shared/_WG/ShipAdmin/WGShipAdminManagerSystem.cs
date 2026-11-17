using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WG.ShipAccess;
using Content.Shared._WG.ShipCommand;
using Content.Shared.Administration.Managers;
using Robust.Shared.Map.Components;

namespace Content.Shared._WG.ShipAdmin;

/// <summary>
/// Lets an active admin manage any ship as its owner would: the console's access tab, the lock verbs and the helm
/// lock. Runs on both sides, so the client's access tab matches the server.
/// </summary>
public sealed partial class WGShipAdminManagerSystem : EntitySystem
{
    [Dependency] private ISharedAdminManager _admin = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MapGridComponent, WGShipManagerEvent>(OnShipManager);
    }

    private void OnShipManager(Entity<MapGridComponent> grid, ref WGShipManagerEvent args)
    {
        // A commanded ship has its own switch for admins. A grid that is not a ship stays out of it: managing one
        // would put ship access on it and rewrite every door reader aboard.
        if (args.Allowed || HasComp<WGShipCommandComponent>(grid) || !IsShip(grid))
            return;

        args.Allowed = _admin.HasAdminFlag(args.User, WGShipAdminPermissions.Base);
    }

    private bool IsShip(EntityUid grid)
    {
        return HasComp<ShuttleDeedComponent>(grid) || HasComp<WGShipAccessComponent>(grid);
    }
}
