using Content.Shared.Clothing.Components;
using Content.Shared.Clothing.EntitySystems;
using Content.Shared.Inventory;

namespace Content.Shared._WG.NeckSuits;

/// <summary>
/// Gives a suit in neck mode the neck slot and back its prototype's slots after, on both sides, since clothing slots
/// aren't in the clothing state and the client predicts equips from them.
/// </summary>
public sealed partial class WGNeckSuitSlotsSystem : EntitySystem
{
    [Dependency] private ClothingSystem _clothing = default!;
    [Dependency] private IComponentFactory _factory = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WGNeckSuitComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<WGNeckSuitComponent, ComponentRemove>(OnRemove);
    }

    private void OnInit(Entity<WGNeckSuitComponent> ent, ref ComponentInit args)
    {
        if (TryComp<ClothingComponent>(ent, out var clothing))
            _clothing.SetSlots(ent, SlotFlags.NECK, clothing);
    }

    private void OnRemove(Entity<WGNeckSuitComponent> ent, ref ComponentRemove args)
    {
        if (TerminatingOrDeleted(ent) || !TryComp<ClothingComponent>(ent, out var clothing))
            return;

        _clothing.SetSlots(ent, GetPrototypeSlots(ent), clothing);
    }

    /// <summary>The slots the suit's prototype gives it, or the outer-suit slot if it has none.</summary>
    public SlotFlags GetPrototypeSlots(EntityUid uid)
    {
        if (MetaData(uid).EntityPrototype is { } proto
            && proto.Components.TryGetValue(_factory.GetComponentName<ClothingComponent>(), out var entry))
        {
            return ((ClothingComponent) entry.Component).Slots;
        }

        return SlotFlags.OUTERCLOTHING;
    }
}
