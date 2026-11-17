using Content.Shared._WG.RolledSleeves;
using Content.Shared.Clothing.Components;
using Content.Shared.Inventory;

namespace Content.Server._WG.RolledSleeves;

/// <summary>Gives rollable sleeves to every jumpsuit, so new suits get them without a prototype edit.</summary>
public sealed partial class WGRolledSleevesAvailabilitySystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ClothingComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<ClothingComponent> ent, ref MapInitEvent args)
    {
        var slots = ent.Comp.Slots;
        if ((slots & SlotFlags.INNERCLOTHING) != 0)
            EnsureComp<WGRolledSleevesComponent>(ent);
    }
}
