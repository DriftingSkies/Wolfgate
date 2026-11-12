using Content.Client.Clothing;
using Content.Client._WF.LegStyle;
using Content.Shared._WF.NeckSuits;
using Content.Shared.DisplacementMap;
using Content.Shared.Humanoid;
using Content.Shared.Inventory;

namespace Content.Client._WF.NeckSuits;

/// <summary>
/// Draws a suit worn in a neck slot with its outer-suit sprite and displacement, so it looks as it would worn as a suit.
/// </summary>
public sealed partial class WFNeckSuitVisualsSystem : EntitySystem
{
    [Dependency] private ClientClothingSystem _clothing = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private LegStyleSystem _legStyle = default!;

    public const string SuitSlot = "outerClothing";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WFNeckSuitComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<WFNeckSuitComponent, ComponentShutdown>(OnShutdown);
    }

    /// <summary>
    /// The component can arrive in the same state as the equip, after the suit was drawn without it, so draw again.
    /// </summary>
    private void OnStartup(Entity<WFNeckSuitComponent> ent, ref ComponentStartup args)
    {
        Redraw(ent);
    }

    private void OnShutdown(Entity<WFNeckSuitComponent> ent, ref ComponentShutdown args)
    {
        Redraw(ent);
    }

    /// <summary>Redraws the wearer's clothes, only while the suit is in an inventory slot rather than held.</summary>
    private void Redraw(EntityUid suit)
    {
        if (TerminatingOrDeleted(suit) || !_inventory.TryGetContainingSlot(suit, out _))
            return;

        if (_inventory.TryGetContainingEntity(suit, out var wearer) && TryComp(wearer, out InventoryComponent? inventory))
            _clothing.InitClothing(wearer.Value, inventory);
    }

    /// <summary>Whether the item is a suit in neck mode; false once the component is shutting down.</summary>
    public bool IsNeckSuit(EntityUid item)
    {
        return TryComp<WFNeckSuitComponent>(item, out var comp) && comp.LifeStage <= ComponentLifeStage.Running;
    }

    /// <summary>The slot whose sprite states and clothing visuals this item draws with.</summary>
    public string GetVisualSlot(EntityUid item, string slot)
    {
        return IsNeckSuit(item) ? SuitSlot : slot;
    }

    /// <summary>The outer-suit displacement map for a neck suit, else the one already picked.</summary>
    public DisplacementData? GetDisplacement(EntityUid wearer, EntityUid item, InventoryComponent inventory,
        DisplacementData? current)
    {
        if (!IsNeckSuit(item))
            return current;

        var data = CompOrNull<HumanoidAppearanceComponent>(wearer)?.Sex switch
        {
            Sex.Male when inventory.MaleDisplacements.Count > 0 => inventory.MaleDisplacements.GetValueOrDefault(SuitSlot),
            Sex.Female when inventory.FemaleDisplacements.Count > 0 => inventory.FemaleDisplacements.GetValueOrDefault(SuitSlot),
            _ => inventory.Displacements.GetValueOrDefault(SuitSlot),
        };

        return _legStyle.GetDisplacement(wearer, SuitSlot, data);
    }
}
