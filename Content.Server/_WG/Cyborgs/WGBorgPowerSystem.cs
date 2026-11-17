using Content.Server.PowerCell;
using Content.Shared._WG.Silicons;
using Content.Shared.Examine;
using Content.Shared.Silicons.Borgs.Components;

namespace Content.Server._WG.Cyborgs;

/// <summary>
/// Lets a cyborg drain APCs and loose cells into its own cell, and names that cell on examine.
/// </summary>
public sealed partial class WGBorgPowerSystem : EntitySystem
{
    [Dependency] private PowerCellSystem _powerCell = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BorgChassisComponent, GetDrinkerBatteryEvent>(OnGetDrinkerBattery);
        SubscribeLocalEvent<BorgChassisComponent, ExaminedEvent>(OnExamined);
    }

    private void OnGetDrinkerBattery(Entity<BorgChassisComponent> ent, ref GetDrinkerBatteryEvent args)
    {
        if (args.Battery != null)
            return;

        if (_powerCell.TryGetBatteryFromSlot(ent.Owner, out var cell, out var battery))
            args.Battery = (cell.Value, battery);
    }

    private void OnExamined(Entity<BorgChassisComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        // The cell slot's own examine line gives the charge; this one says which cell it is.
        if (_powerCell.TryGetBatteryFromSlot(ent.Owner, out var cell, out _))
            args.PushMarkup(Loc.GetString("wg-borg-examine-cell", ("cell", cell.Value)));
    }
}
