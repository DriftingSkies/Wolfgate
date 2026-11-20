using Content.Server.Cargo.Systems;
using Content.Shared._WG.Modsuits.Components;
using Content.Shared.Power.Components;

namespace Content.Server._WG.Modsuits;

/// <summary>Prices a cell scaled by a MOD core by its real capacity, so a core can't inflate a cell's sale value.</summary>
public sealed partial class ModsuitCellPriceSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ModsuitCellComponent, PriceCalculationEvent>(OnPriceCalculation);
    }

    private void OnPriceCalculation(Entity<ModsuitCellComponent> ent, ref PriceCalculationEvent args)
    {
        if (ent.Comp.ScaledMaxCharge <= 0f || !TryComp<BatteryComponent>(ent, out var battery))
            return;

        // BatterySystem prices the scaled charge; take back the part the real capacity wouldn't hold.
        var realCharge = battery.CurrentCharge * ent.Comp.OriginalMaxCharge / ent.Comp.ScaledMaxCharge;
        args.Price -= (battery.CurrentCharge - realCharge) * battery.PricePerJoule;
    }
}
