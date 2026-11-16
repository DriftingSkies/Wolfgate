using Content.Client.Administration.Managers;
using Content.Shared._WF.ShipAccess;
using Content.Shared._WF.ShipCommand;
using Content.Shared.Administration;

namespace Content.Client._WF.ShipCommand;

/// <summary>Mirrors the server's admin path into ship access management, so the access tab opens its controls to admins.</summary>
public sealed partial class WFShipCommandClientSystem : EntitySystem
{
    [Dependency] private IClientAdminManager _admin = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFShipCommandComponent, WFShipManagerEvent>(OnShipManager);
    }

    /// <summary>The local player, as an active admin (or event staff where they count), manages it unless the owner switched that off.</summary>
    private void OnShipManager(Entity<WFShipCommandComponent> grid, ref WFShipManagerEvent args)
    {
        if (args.Allowed || !grid.Comp.AdminsAtConsole)
            return;

        args.Allowed = _admin.HasFlag(AdminFlags.Admin) || grid.Comp.EventStaffAreAdmins && _admin.HasFlag(AdminFlags.Event);
    }
}
