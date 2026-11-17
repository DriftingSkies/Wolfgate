using Content.Shared._WG.ShipCommand;
using Content.Shared.Popups;
using Content.Shared.Wires;

namespace Content.Server._WG.ShipCommand;

public sealed partial class WGShipCommandSystem
{
    [Dependency] private SharedPopupSystem _popup = default!;

    private void InitializeWires()
    {
        SubscribeLocalEvent<WiresPanelComponent, AttemptChangePanelEvent>(OnAttemptChangePanel);
        SubscribeLocalEvent<WGEmagAttemptEvent>(OnEmagAttempt);
    }

    /// <summary>On a ship with staff-only wires, an emag works for the staff list and up alone.</summary>
    private void OnEmagAttempt(ref WGEmagAttemptEvent args)
    {
        if (args.Cancelled || CanTouchWires(args.User, args.Target))
            return;

        args.Cancelled = true;
        _popup.PopupEntity(Loc.GetString("wg-shipcommand-emag-staff-only"), args.Target, args.User);
    }

    /// <summary>On a ship with staff-only wires, a maintenance panel opens and closes for the staff list and up alone.</summary>
    private void OnAttemptChangePanel(Entity<WiresPanelComponent> panel, ref AttemptChangePanelEvent args)
    {
        if (args.Cancelled || args.User is not { } user || CanTouchWires(user, panel))
            return;

        args.Cancelled = true;
        _popup.PopupEntity(Loc.GetString("wg-shipcommand-wires-staff-only"), panel, user);
    }

    /// <summary>Whether someone may work an entity's maintenance panel or emag it: anyone, unless it is aboard a ship
    /// with <see cref="WGShipCommandComponent.StaffOnlyWires"/>, where the staff list and up.</summary>
    public bool CanTouchWires(EntityUid user, EntityUid target)
    {
        if (Transform(target).GridUid is not { } grid
            || !TryComp<WGShipCommandComponent>(grid, out var command)
            || !command.StaffOnlyWires)
            return true;

        return GetAuthority(user, grid) >= WGShipAuthority.Staff;
    }
}
