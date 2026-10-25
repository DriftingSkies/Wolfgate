using Content.Shared._WF.RolledSleeves;
using Content.Shared.Clothing.Components;
using Content.Shared.Inventory;

namespace Content.Server._WF.RolledSleeves;

/// <summary>Gives rollable sleeves to every jumpsuit, so new suits get them without a prototype edit.</summary>
public sealed partial class WFRolledSleevesAvailabilitySystem : EntitySystem
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
            EnsureComp<WFRolledSleevesComponent>(ent);
    }
}
