using Content.Server._WG.NpcCrew.Components;
using Content.Server._WG.ShipCommand;
using Content.Shared._WG.NpcCrew;
using Content.Shared.Buckle.Components;
using Content.Shared.Clothing.Components;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WG.NpcCrew.Systems;

/// <summary>
/// Roster crewmen's clothes: a ship's company is dressed with some variety when the roster staffs it
/// (<see cref="WGCrewWardrobePrototype"/>), what each then wears is remembered (<see cref="WGCrewOutfitComponent"/>),
/// and a crewman stripped of any of it fetches it back and puts it on again once he is up and free.
/// <see cref="HTN.WGCrewRedressOperator"/> walks him to it.
/// </summary>
public sealed class WGCrewOutfitSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WGCrewWeaponSystem _weapons = default!;
    [Dependency] private WGCrewEvaSystem _eva = default!;

    /// <summary>The order pieces go back on: what others hang from first.</summary>
    private static readonly string[] Order =
    {
        "jumpsuit", "shoes", "outerClothing", "suitstorage", "gloves", "mask", "head", "eyes", "ears", "neck",
        "back", "belt", "id", "pocket1", "pocket2",
    };

    /// <summary>How far, in metres, he goes for a piece.</summary>
    private const float Range = 40f;

    private const float Reach = 1.5f;
    private static readonly TimeSpan LookGap = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan FetchTime = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan SkipTime = TimeSpan.FromMinutes(3);

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGShipPostStaffedEvent>(OnStaffed);
    }

    /// <summary>Dresses a newly staffed crewman from his role's wardrobes, then remembers what he wears.</summary>
    private void OnStaffed(ref WGShipPostStaffedEvent ev)
    {
        if (!TryComp<WGCrewComponent>(ev.Mob, out var crew))
            return;
        Dress(ev.Mob, crew);
        Remember(ev.Mob);
    }

    /// <summary>Swaps pieces of his loadout for others his role's wardrobes allow, each slot by its chance.</summary>
    public void Dress(EntityUid mob, WGCrewComponent crew)
    {
        if (crew.Role is not { } role)
            return;
        var coordinates = Transform(mob).Coordinates;
        foreach (var wardrobe in _prototypes.EnumeratePrototypes<WGCrewWardrobePrototype>())
        {
            if (!wardrobe.Roles.Contains(role))
                continue;
            foreach (var (slot, choices) in wardrobe.Slots)
            {
                if (choices.Items.Count == 0 || !_random.Prob(choices.Chance) || !_inventory.HasSlot(mob, slot))
                    continue;
                if (_inventory.TryGetSlotEntity(mob, slot, out var old))
                {
                    // A suit's own helmet or a piece that won't come off stays.
                    if (HasComp<AttachedClothingComponent>(old) || !_inventory.TryUnequip(mob, slot, silent: true, force: true))
                        continue;
                    QueueDel(old);
                }
                var item = Spawn(_random.Pick(choices.Items), coordinates);
                if (!_inventory.TryEquip(mob, item, slot, silent: true, force: true))
                    QueueDel(item);
            }
        }
    }

    /// <summary>Records what he wears now as his own.</summary>
    public void Remember(EntityUid mob)
    {
        var outfit = EnsureComp<WGCrewOutfitComponent>(mob);
        outfit.Worn.Clear();
        foreach (var slot in Order)
        {
            // A suit's helmet comes back with the suit, not by itself.
            if (_inventory.TryGetSlotEntity(mob, slot, out var item) && !HasComp<AttachedClothingComponent>(item))
                outfit.Worn[slot] = item.Value;
        }
    }

    /// <summary>Where he walks for the next missing piece, if he is free to; false when he lacks nothing he can get.</summary>
    public bool Destination(EntityUid mob, out EntityCoordinates coordinates)
    {
        coordinates = default;
        if (!TryComp<WGCrewOutfitComponent>(mob, out var outfit) || !Free(mob))
            return false;

        var now = _timing.CurTime;
        if (outfit.Fetching is { } slot && (now > outfit.Until || !Missing(mob, outfit, slot, out _)))
        {
            if (now > outfit.Until && outfit.Worn.TryGetValue(slot, out var given))
                outfit.Skipped[given] = now + SkipTime;
            outfit.Fetching = null;
        }

        if (outfit.Fetching == null)
        {
            if (now < outfit.NextLook)
                return false;
            outfit.NextLook = now + LookGap;
            foreach (var candidate in Order)
            {
                if (!Missing(mob, outfit, candidate, out _))
                    continue;
                outfit.Fetching = candidate;
                outfit.Until = now + FetchTime;
                break;
            }
        }

        if (outfit.Fetching is not { } fetching || !Missing(mob, outfit, fetching, out var piece))
            return false;
        coordinates = Transform(Holder(piece) ?? piece).Coordinates;
        return true;
    }

    /// <summary>At the piece: takes it from where it lies and puts it on. True when he must walk again or is done.</summary>
    public bool Perform(EntityUid mob)
    {
        if (!TryComp<WGCrewOutfitComponent>(mob, out var outfit) || outfit.Fetching is not { } slot
            || !Missing(mob, outfit, slot, out var piece))
            return true;
        if (!_interaction.InRangeUnobstructed(mob, Holder(piece) ?? piece, Reach))
            return true;

        if (_containers.IsEntityInContainer(piece))
            _containers.TryRemoveFromContainer(piece, true);
        if (_inventory.TryEquip(mob, piece, slot, silent: true, force: true))
        {
            if (slot == "outerClothing")
                _eva.FitSuitHelmet(mob);
        }
        else
        {
            outfit.Skipped[piece] = _timing.CurTime + SkipTime;
        }
        outfit.Fetching = null;
        outfit.NextLook = TimeSpan.Zero;
        return true;
    }

    /// <summary>
    /// Whether a slot of his lacks its piece and the piece can be had: it still exists, the slot is empty, and it lies
    /// loose on his ship within range, or in a container no person holds (an autodoc's tray, a crate).
    /// </summary>
    private bool Missing(EntityUid mob, WGCrewOutfitComponent outfit, string slot, out EntityUid piece)
    {
        piece = default;
        if (!outfit.Worn.TryGetValue(slot, out piece) || TerminatingOrDeleted(piece)
            || outfit.Skipped.TryGetValue(piece, out var until) && _timing.CurTime < until
            || _inventory.TryGetSlotEntity(mob, slot, out _))
            return false;

        var grid = Transform(mob).GridUid;
        var spot = Holder(piece) ?? piece;
        var xform = Transform(spot);
        if (grid == null || xform.GridUid != grid
            || (_transform.GetWorldPosition(xform) - _transform.GetWorldPosition(mob)).LengthSquared() > Range * Range)
            return false;

        if (!_containers.TryGetContainingContainer(piece, out var container))
            return xform.ParentUid == grid && !xform.Anchored;
        // Never off a person, out of something a person carries, or out of anything locked or welded shut.
        var holder = container.Owner;
        if (TryComp<Content.Shared.Lock.LockComponent>(holder, out var locked) && locked.Locked
            || TryComp<Content.Shared.Tools.Components.WeldableComponent>(holder, out var weld) && weld.IsWelded)
            return false;
        return !HasComp<MobStateComponent>(holder) && Transform(holder).ParentUid == grid;
    }

    /// <summary>The thing a piece lies in, if any.</summary>
    private EntityUid? Holder(EntityUid piece)
    {
        return _containers.TryGetContainingContainer(piece, out var container) ? container.Owner : null;
    }

    /// <summary>Up, himself, not fighting, not in a bed or a medic's hands, not on a break or a job.</summary>
    private bool Free(EntityUid mob)
    {
        return _mobs.IsAlive(mob) && !HasComp<ActorComponent>(mob) && !_weapons.HasLiveThreat(mob)
            && CompOrNull<BuckleComponent>(mob)?.BuckledTo == null
            && !EntityManager.System<WGCrewMedicSystem>().IsDown(mob)
            && !EntityManager.System<WGCrewMedicSystem>().UnderCare(mob)
            && !EntityManager.System<WGCrewDowntimeSystem>().OnBreak(mob)
            && !EntityManager.System<WGCrewWorkSystem>().HasJob(mob);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        // Forget pieces gone for good, and lapsed skips, now and then.
        if (_timing.CurTick.Value % 300 != 0)
            return;
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WGCrewOutfitComponent>();
        while (query.MoveNext(out var outfit))
        {
            foreach (var (item, until) in new List<KeyValuePair<EntityUid, TimeSpan>>(outfit.Skipped))
            {
                if (now >= until || TerminatingOrDeleted(item))
                    outfit.Skipped.Remove(item);
            }
        }
    }
}
