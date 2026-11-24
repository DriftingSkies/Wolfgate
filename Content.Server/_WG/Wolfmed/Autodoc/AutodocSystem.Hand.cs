using Content.Shared._WG.Wolfmed.Autodoc;
using Content.Shared.Body.Part;
using Content.Shared.Clothing.Components;
using Content.Shared.Inventory;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.Wolfmed.Autodoc;

/// <summary>
/// What a surgeon working by hand takes from the pod (NpcCrew's table surgery): its anaesthetic rules, run from the
/// off-map pod that holds his instruments, and undressing done whole and by hand.
/// </summary>
public sealed partial class AutodocSystem
{
    /// <summary>Puts a body under for one procedure, by the pod's rules, from an instrument pod's reservoirs.</summary>
    public void AnaesthetiseFor(Entity<AutodocComponent> kit, EntityUid body, EntProtoId surgery)
    {
        kit.Comp.Queue.Clear();
        kit.Comp.Queue.Add(new AutodocQueued { Surgery = surgery, Requirements = BuildRequirements(kit, surgery) });
        MaintainAnaesthesia(kit, body);
        kit.Comp.Queue.Clear();
    }

    /// <summary>What a patient comes to the table with stays theirs; what turns up from here is the hand work's.</summary>
    public void SnapshotHandWounds(Entity<AutodocComponent> kit, EntityUid body) => SnapshotWounds(kit, body);

    /// <summary>Marks what the hand work added, as a pod marks its own, so the planner doesn't read a suture as new work.</summary>
    public void MarkHandWounds(Entity<AutodocComponent> kit, EntityUid body) => MarkPodWounds(kit, body);

    /// <summary>
    /// Takes off everything the armour check reads on a part, whole, with the refusals a person would meet; it lands
    /// on the floor (<see cref="ToFloor"/>). True when nothing is left in the way.
    /// </summary>
    public bool TakeOffWhole(EntityUid actor, EntityUid body, BodyPartType type)
    {
        var flags = ArmorSlots(type);
        if (flags == SlotFlags.NONE || !_inventory.TryGetSlots(body, out var slots))
            return true;

        foreach (var slot in slots)
        {
            if ((slot.SlotFlags & flags) == 0 || !_inventory.TryGetSlotEntity(body, slot.Name, out var item))
                continue;

            // A hardsuit helmet comes off with its suit, if the suit would come off for a person.
            if (TryComp(item, out AttachedClothingComponent? attached) && WornIn(body, attached.AttachedUid, SlotFlags.OUTERCLOTHING) is { } suit)
            {
                if (Array.Find(slots, s => s.Name == suit) is { } suitSlot && CanTakeOff(actor, body, suitSlot, attached.AttachedUid)
                    && _inventory.TryUnequip(body, suit, out var suitTaken, silent: true, force: true))
                    ToFloor(actor, body, suitTaken.Value);
                continue;
            }

            if (CanTakeOff(actor, body, slot, item.Value) && _inventory.TryUnequip(body, slot.Name, out var taken, silent: true, force: true))
                ToFloor(actor, body, taken.Value);
        }

        foreach (var slot in slots)
        {
            if ((slot.SlotFlags & flags) != 0 && _inventory.TryGetSlotEntity(body, slot.Name, out _))
                return false;
        }

        return true;
    }

    /// <summary>
    /// A surgeon attending a pod that waits on clothing takes it off by hand, whole, before the pod's shears would cut
    /// it. True when nothing is left in the way.
    /// </summary>
    public bool TakeOffBlocking(Entity<AutodocComponent> pod, EntityUid actor, EntityUid body)
    {
        if (BlockedPart(pod, body) is not { } blocked)
            return true;
        var clear = TakeOffWhole(actor, body, blocked.Type);
        if (clear)
        {
            pod.Comp.BlockingGarment = null;
            pod.Comp.BlockingSlot = null;
            pod.Comp.GarmentStuck = false;
        }
        return clear;
    }

    /// <summary>
    /// Leaves a garment just taken off on the floor: beside the patient, or beside whoever undressed them when the
    /// patient lies in a pod or is carried, where dropping it beside them would put it in there with them.
    /// </summary>
    private void ToFloor(EntityUid actor, EntityUid body, EntityUid item)
    {
        var beside = _containers.IsEntityInContainer(body) ? actor : body;
        if (_containers.IsEntityInContainer(item))
            _containers.TryRemoveFromContainer(item, true);
        _xform.DropNextTo(item, beside);
    }
}
