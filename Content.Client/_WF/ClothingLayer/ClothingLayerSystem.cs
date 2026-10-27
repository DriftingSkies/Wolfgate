using System.Linq;
using Content.Client.Clothing;
using Content.Client.Inventory;
using Content.Shared._WF.ClothingLayer;
using Content.Shared.Clothing;
using Content.Shared.Clothing.Components;
using Content.Shared.Humanoid;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Item;
using Robust.Client.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Reflection;

namespace Content.Client._WF.ClothingLayer;

/// <summary>
/// SS13's rules for how worn clothing stacks, which <see cref="ClientClothingSystem"/> asks before drawing a slot.
/// Placement: the uniform draws over the feet, a draping uniform over the shoes, and an item with
/// <see cref="WFClothingLayerComponent"/> wherever it names. Hiding: a suit that encloses the body hides the
/// uniform, and an item with <see cref="WFHideClothingSlotsComponent"/> hides the slots it lists.
/// </summary>
public sealed partial class ClothingLayerSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IReflectionManager _reflection = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedItemSystem _item = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    private static readonly ProtoId<ClothingShapesPrototype> Shapes = "WFDefault";
    private const string Uniform = "jumpsuit";
    private const string Shoes = "shoes";
    private const string Suit = "outerClothing";

    private bool _redrawing;

    public override void Initialize()
    {
        base.Initialize();

        // Broadcast, as an enclosing suit hides with no component of its own. Both only redraw what an item covers.
        SubscribeLocalEvent<EquipmentVisualsUpdatedEvent>(OnVisualsUpdated);
        SubscribeLocalEvent<GotUnequippedEvent>(OnUnequipped);
    }

    /// <summary>
    /// The wearer's sprite index to insert the clothing's layers after, or false to use its slot's bookmark.
    /// </summary>
    public bool TryGetIndex(Entity<SpriteComponent> wearer, EntityUid equipment, string slot,
        InventorySlotsComponent slots, out int index)
    {
        index = 0;
        if (TryComp<WFClothingLayerComponent>(equipment, out var layer))
            return TryGetIndex(wearer, slots, layer.Layer, layer.Below, out index);

        if (slot != Uniform)
            return false;

        if (_proto.Index(Shapes).Drapes.Contains(RsiPath(equipment)))
            return TryGetIndex(wearer, slots, Shoes, false, out index);

        // SS13 draws the uniform over the feet. Only where they come between it and the shoes, as on most bodies;
        // a species that draws its feet higher on purpose keeps them there.
        if (!_sprite.LayerMapTryGet(wearer.AsNullable(), Uniform, out var uniform, false)
            || !_sprite.LayerMapTryGet(wearer.AsNullable(), Shoes, out var shoes, false)
            || !_sprite.LayerMapTryGet(wearer.AsNullable(), HumanoidVisualLayers.LFoot, out var left, false)
            || !_sprite.LayerMapTryGet(wearer.AsNullable(), HumanoidVisualLayers.RFoot, out var right, false))
        {
            return false;
        }

        index = Math.Max(left, right);
        return Math.Min(left, right) > uniform && index < shoes;
    }

    /// <summary>
    /// Whether something else the wearer has on covers this slot, so its clothing isn't drawn.
    /// </summary>
    public bool IsHidden(EntityUid wearer, EntityUid equipment, string slot)
    {
        return IsHidden(wearer, equipment, slot, 0);
    }

    /// <summary>
    /// A hider counts only while it's drawn itself: a costume under a hardsuit doesn't hide the shoes.
    /// Past <see cref="MaxHideDepth"/> a hider counts regardless, which ends cycles of items hiding each other.
    /// </summary>
    private bool IsHidden(EntityUid wearer, EntityUid equipment, string slot, int depth)
    {
        var enumerator = _inventory.GetSlotEnumerator(wearer);
        while (enumerator.NextItem(out var item, out var itemSlot))
        {
            if (item != equipment
                && Covers(item, itemSlot).Contains(slot)
                && (depth >= MaxHideDepth || !IsHidden(wearer, item, itemSlot.Name, depth + 1)))
            {
                return true;
            }
        }

        return false;
    }

    private const int MaxHideDepth = 3;

    /// <summary>
    /// The slots an item hides while it's in this slot. Only when worn where it's made to be, and as made: a coat
    /// worn open or a mask pulled down (any equipped prefix) hides nothing, and an item in a pocket hides nothing.
    /// </summary>
    private IReadOnlyCollection<string> Covers(EntityUid item, SlotDefinition slot)
    {
        if (!TryComp<ClothingComponent>(item, out var clothing)
            || (clothing.Slots & slot.SlotFlags) == 0
            || clothing.EquippedPrefix != null)
        {
            return Array.Empty<string>();
        }

        if (TryComp<WFHideClothingSlotsComponent>(item, out var hide))
            return hide.Slots;

        return slot.Name == Suit && _proto.Index(Shapes).Encloses.Contains(RsiPath(item))
            ? EnclosedSlots
            : Array.Empty<string>();
    }

    private static readonly string[] EnclosedSlots = { Uniform };

    private bool TryGetIndex(Entity<SpriteComponent> wearer, InventorySlotsComponent slots, string target,
        bool below, out int index)
    {
        var found = target.StartsWith("enum.") && _reflection.TryParseEnumReference(target, out var @enum, false)
            ? _sprite.LayerMapTryGet(wearer.AsNullable(), @enum, out index, false)
            : _sprite.LayerMapTryGet(wearer.AsNullable(), target, out index, false);
        if (!found)
            return false;

        if (below)
        {
            index--;
            return true;
        }

        // A slot's clothing sits right after its bookmark layer, so go past the run of layers that slot is drawing.
        // Only the run: something else can add a layer to the slot's keys elsewhere, such as a suit's tank on top.
        if (slots.VisualLayerKeys.TryGetValue(target, out var drawn))
        {
            var mine = new HashSet<int>();
            foreach (var key in drawn)
            {
                if (_sprite.LayerMapTryGet(wearer.AsNullable(), key, out var i, false))
                    mine.Add(i);
            }

            while (mine.Contains(index + 1))
                index++;
        }

        return true;
    }

    /// <summary>
    /// The item's clothing RSI path under <c>/Textures</c>, as the shapes list keys it.
    /// </summary>
    private string RsiPath(EntityUid item)
    {
        var path = CompOrNull<ClothingComponent>(item)?.RsiPath
            ?? CompOrNull<SpriteComponent>(item)?.BaseRSI?.Path.ToString()
            ?? string.Empty;
        if (path.StartsWith("/Textures/"))
            path = path["/Textures/".Length..];
        return path.TrimStart('/');
    }

    /// <summary>
    /// The slots an item could hide in some state, whatever its prefix: the ones to redraw when it changes.
    /// </summary>
    private IReadOnlyCollection<string> CouldCover(EntityUid item, string slot)
    {
        if (TryComp<WFHideClothingSlotsComponent>(item, out var hide))
            return hide.Slots;

        return slot == Suit && _proto.Index(Shapes).Encloses.Contains(RsiPath(item))
            ? EnclosedSlots
            : Array.Empty<string>();
    }

    // Drawn on equip, on a visuals change such as pulling a mask down or opening a coat, and on a full redraw.
    private void OnVisualsUpdated(EquipmentVisualsUpdatedEvent args)
    {
        if (_inventory.TryGetSlotEntity(args.Equipee, args.Slot, out var item))
            Redraw(args.Equipee, CouldCover(item.Value, args.Slot));
    }

    private void OnUnequipped(GotUnequippedEvent args)
    {
        if (!TerminatingOrDeleted(args.Equipee))
            Redraw(args.Equipee, CouldCover(args.Equipment, args.Slot));
    }

    /// <summary>
    /// Draws again what the wearer has in these slots, now that something over them changed.
    /// </summary>
    private void Redraw(EntityUid wearer, IReadOnlyCollection<string> slots)
    {
        // Redrawing a slot can draw another hider, which would land back here.
        if (_redrawing || slots.Count == 0)
            return;

        // A redrawn slot that hides others can change what they show too, so take in what it could cover as well.
        var pending = new Queue<string>(slots);
        var seen = new HashSet<string>(slots);
        var items = new List<EntityUid>();
        while (pending.TryDequeue(out var slot))
        {
            if (!_inventory.TryGetSlotEntity(wearer, slot, out var item))
                continue;

            items.Add(item.Value);
            foreach (var covered in CouldCover(item.Value, slot))
            {
                if (seen.Add(covered))
                    pending.Enqueue(covered);
            }
        }

        _redrawing = true;
        try
        {
            foreach (var item in items)
            {
                _item.VisualsChanged(item);
            }
        }
        finally
        {
            _redrawing = false;
        }
    }
}
