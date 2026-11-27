using Content.Server._WG.CrewBarks;
using Content.Server._WG.NpcCrew.Components;
using Content.Server._WG.ShipCommand;
using Content.Server.Bed.Components;
using Content.Server.NPC.HTN;
using Content.Server.Nutrition.Components;
using Content.Server.VendingMachines;
using Content.Shared._Shitmed.Medical.Surgery;
using Content.Shared._WG.NpcCrew;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Disposal.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Systems;
using Content.Shared.Nutrition.Components;
using Content.Shared.Tag;
using Content.Shared.VendingMachines;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WG.NpcCrew.Systems;

/// <summary>
/// Breaks for roster crew between duties: now and then, at Code Green with nothing else on, a crewman buys a snack or a
/// drink from a machine aboard, eats it in a free seat and puts the rubbish in a disposal unit, or just sits down a
/// while. <see cref="HTN.WGCrewDowntimeOperator"/> walks him through it in the crew HTN.
/// </summary>
public sealed class WGCrewDowntimeSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IPrototypeManager _protos = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedBuckleSystem _buckle = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private TagSystem _tags = default!;
    [Dependency] private VendingMachineSystem _vending = default!;
    [Dependency] private WGCrewSystem _crew = default!;
    [Dependency] private WGCrewWeaponSystem _weapons = default!;
    [Dependency] private WGCrewWorkSystem _work = default!;
    [Dependency] private WGCrewReadinessSystem _readiness = default!;
    [Dependency] private WGCrewBarkSystem _barks = default!;
    [Dependency] private HTNSystem _htn = default!;

    private static readonly ProtoId<TagPrototype> Trash = "Trash";

    /// <summary>How close he stands to use a machine, sit down or bin something.</summary>
    private const float Reach = 1.5f;

    /// <summary>Most bites or sips he takes before he calls it finished.</summary>
    private const int MaxUses = 12;

    private static readonly TimeSpan UseGap = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan VendWait = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan CollectTime = TimeSpan.FromSeconds(8);

    private float _timer;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGShipPostStaffedEvent>(OnStaffed);
    }

    /// <summary>Roster crew take breaks, except at the helm and the guns.</summary>
    private void OnStaffed(ref WGShipPostStaffedEvent ev)
    {
        if (!TryComp<WGCrewComponent>(ev.Mob, out var crew) || crew.Duty is WGCrewDuties.Pilot or WGCrewDuties.Gunnery)
            return;
        var downtime = EnsureComp<WGCrewDowntimeComponent>(ev.Mob);
        downtime.NextBreak = _timing.CurTime + Gap(downtime);
    }

    /// <summary>Whether a crewman is on a break.</summary>
    public bool OnBreak(EntityUid uid) => TryComp<WGCrewDowntimeComponent>(uid, out var comp) && comp.Stage != WGCrewBreakStage.None;

    /// <summary>Ends a break where it stands: up out of the seat, and back to work holding whatever he holds.</summary>
    public void EndBreak(EntityUid uid)
    {
        if (!TryComp<WGCrewDowntimeComponent>(uid, out var comp) || comp.Stage == WGCrewBreakStage.None)
            return;
        if (CompOrNull<BuckleComponent>(uid)?.BuckledTo is { } seat && seat == comp.Seat)
            _buckle.TryUnbuckle(uid, uid, popup: false);
        comp.Stage = WGCrewBreakStage.None;
        comp.Target = null;
        comp.Seat = null;
        comp.Item = null;
        comp.Vended = null;
        comp.Uses = 0;
        comp.NextBreak = _timing.CurTime + Gap(comp);
        if (TryComp<HTNComponent>(uid, out var htn))
            _htn.Replan(htn);
    }

    private TimeSpan Gap(WGCrewDowntimeComponent comp)
    {
        var spread = Math.Max(0, (comp.MaxGap - comp.MinGap).TotalSeconds);
        return comp.MinGap + TimeSpan.FromSeconds(_random.NextDouble() * spread);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        _timer += frameTime;
        if (_timer < 1f)
            return;
        _timer = 0f;

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WGCrewDowntimeComponent, WGCrewComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var comp, out var crew, out var xform))
        {
            var free = Free(uid, crew, xform);
            if (comp.Stage != WGCrewBreakStage.None)
            {
                if (!free || now - comp.Started > comp.MaxBreak)
                    EndBreak(uid);
                continue;
            }

            if (now < comp.NextBreak)
                continue;
            comp.NextBreak = now + Gap(comp);
            if (free)
                StartBreak(uid, comp, xform);
        }
    }

    /// <summary>Whether he has nothing better to do than a break: Code Green, no fight, no job, no patient, no pest.</summary>
    private bool Free(EntityUid uid, WGCrewComponent crew, TransformComponent xform)
    {
        return _mobs.IsAlive(uid) && !HasComp<ActorComponent>(uid)
            && crew.Post?.EntityId is { } home && xform.GridUid == home
            && _readiness.Readiness(home) == WGCrewReadiness.Normal
            && !_weapons.HasLiveThreat(uid) && !_work.HasJob(uid)
            && CompOrNull<WGCrewMedicComponent>(uid)?.Patient == null
            && CompOrNull<WGCrewPestHunterComponent>(uid)?.Target == null
            && (CompOrNull<WGCrewBartenderComponent>(uid) is not { } bar || bar.Current == null && bar.Queue.Count == 0)
            && !EntityManager.System<WGCrewChefSystem>().HasJob(uid)
            && !EntityManager.System<WGCrewShelterSystem>().IsSheltering(uid)
            && !RoleAway(uid, crew, home);
    }

    /// <summary>Whether another crewman of his role on his ship is already on a break: one away at a time.</summary>
    private bool RoleAway(EntityUid uid, WGCrewComponent crew, EntityUid home)
    {
        var others = EntityQueryEnumerator<WGCrewDowntimeComponent, WGCrewComponent>();
        while (others.MoveNext(out var other, out var comp, out var otherCrew))
        {
            if (other != uid && comp.Stage != WGCrewBreakStage.None && otherCrew.Role == crew.Role
                && otherCrew.Post?.EntityId == home)
                return true;
        }
        return false;
    }

    private void StartBreak(EntityUid uid, WGCrewDowntimeComponent comp, TransformComponent xform)
    {
        var roll = _random.NextFloat();
        var kind = roll < 0.35f ? WGCrewBreakKind.Snack : roll < 0.7f ? WGCrewBreakKind.Drink : WGCrewBreakKind.Rest;
        comp.Started = _timing.CurTime;
        comp.Uses = 0;
        if (kind != WGCrewBreakKind.Rest && FindVendor(uid, comp, xform, kind) is { } vendor)
        {
            comp.Kind = kind;
            comp.Stage = WGCrewBreakStage.ToVendor;
            comp.Target = vendor.Machine;
            comp.Vended = vendor.Item;
        }
        else if (FindSeat(uid, comp, Transform(uid).Coordinates) is { } seat)
        {
            comp.Kind = WGCrewBreakKind.Rest;
            comp.Stage = WGCrewBreakStage.ToSeat;
            comp.Target = seat;
            comp.Seat = seat;
        }
        else
        {
            return;
        }

        _barks.Bark(uid, WGCrewBark.Break);
        if (TryComp<HTNComponent>(uid, out var htn))
            _htn.Replan(htn);
    }

    /// <summary>Where his break takes him next; false when he is not on one.</summary>
    public bool Destination(EntityUid mob, out EntityCoordinates coordinates)
    {
        coordinates = default;
        if (!TryComp<WGCrewDowntimeComponent>(mob, out var comp) || comp.Stage == WGCrewBreakStage.None)
            return false;

        var goal = comp.Stage == WGCrewBreakStage.Seated ? comp.Seat : comp.Target;
        if (goal is not { } target || TerminatingOrDeleted(target))
        {
            coordinates = Transform(mob).Coordinates;
            return comp.Stage == WGCrewBreakStage.Seated;
        }

        coordinates = Transform(target).Coordinates;
        return true;
    }

    /// <summary>
    /// One step of the break where he stands. True when he must walk again (or the break is over), false while he is
    /// busy where he is.
    /// </summary>
    public bool Perform(EntityUid mob)
    {
        if (!TryComp<WGCrewDowntimeComponent>(mob, out var comp))
            return true;

        var now = _timing.CurTime;
        switch (comp.Stage)
        {
            case WGCrewBreakStage.ToVendor:
                if (comp.Target is not { } machine || comp.Vended is not { } item || TerminatingOrDeleted(machine))
                {
                    EndBreak(mob);
                    return true;
                }
                if (!_interaction.InRangeUnobstructed(mob, machine, Reach))
                    return true;
                if (!_vending.TryEjectVendorItem(machine, InventoryType.Regular, item, false))
                {
                    EndBreak(mob);
                    return true;
                }
                comp.Stage = WGCrewBreakStage.Collect;
                comp.NextUse = now + VendWait;
                return false;

            case WGCrewBreakStage.Collect:
                if (now < comp.NextUse)
                    return false;
                if (comp.Target is { } vendor && Dropped(vendor, comp.Vended) is { } bought && _hands.TryPickupAnyHand(mob, bought))
                {
                    comp.Item = bought;
                    comp.Seat = FindSeat(mob, comp, Transform(vendor).Coordinates);
                    comp.Target = comp.Seat;
                    comp.Stage = comp.Seat != null ? WGCrewBreakStage.ToSeat : WGCrewBreakStage.Seated;
                    comp.Until = now + Rest(comp);
                    return true;
                }
                if (now > comp.NextUse + CollectTime)
                    EndBreak(mob);
                return false;

            case WGCrewBreakStage.ToSeat:
                if (comp.Seat is not { } seat || TerminatingOrDeleted(seat) || !SeatFree(seat, mob))
                {
                    // Someone took it: eat standing.
                    comp.Seat = null;
                    comp.Stage = WGCrewBreakStage.Seated;
                    comp.Until = now + Rest(comp);
                    return false;
                }
                if (!_interaction.InRangeUnobstructed(mob, seat, Reach))
                    return true;
                _transform.SetCoordinates(mob, Transform(seat).Coordinates);
                _buckle.TryBuckle(mob, mob, seat, popup: false);
                comp.Stage = WGCrewBreakStage.Seated;
                comp.Until = now + Rest(comp);
                return false;

            case WGCrewBreakStage.Seated:
                return Sit(mob, comp, now);

            case WGCrewBreakStage.ToBin:
                if (comp.Target is not { } bin || TerminatingOrDeleted(bin) || comp.Item is not { } rubbish || TerminatingOrDeleted(rubbish))
                {
                    EndBreak(mob);
                    return true;
                }
                if (!_interaction.InRangeUnobstructed(mob, bin, Reach))
                    return true;
                _interaction.InteractUsing(mob, rubbish, bin, Transform(bin).Coordinates, checkCanInteract: false);
                EndBreak(mob);
                return true;

            default:
                return true;
        }
    }

    /// <summary>Sitting: a bite or a sip every few seconds until it is gone, then the rubbish to a bin, or up again.</summary>
    private bool Sit(EntityUid mob, WGCrewDowntimeComponent comp, TimeSpan now)
    {
        if (comp.Item is { } item && !Finished(mob, comp, item))
        {
            if (now >= comp.NextUse)
            {
                comp.NextUse = now + UseGap;
                comp.Uses++;
                _interaction.UseInHandInteraction(mob, item, checkCanInteract: false);
            }
            return false;
        }

        // Done with it: whatever he is left holding goes in a bin.
        if (Rubbish(mob, comp) is { } rubbish)
        {
            comp.Item = rubbish;
            if (FindBin(mob, comp) is { } bin)
            {
                if (CompOrNull<BuckleComponent>(mob)?.BuckledTo is { } seat && seat == comp.Seat)
                    _buckle.TryUnbuckle(mob, mob, popup: false);
                comp.Stage = WGCrewBreakStage.ToBin;
                comp.Target = bin;
                return true;
            }

            _hands.TryDrop(mob, rubbish);
            comp.Item = null;
        }

        if (now < comp.Until)
            return false;
        EndBreak(mob);
        return true;
    }

    /// <summary>Whether he is through with what he bought: eaten, drunk dry, or he has had enough of it.</summary>
    private bool Finished(EntityUid mob, WGCrewDowntimeComponent comp, EntityUid item)
    {
        if (TerminatingOrDeleted(item) || !_hands.IsHolding(mob, item, out _) || comp.Uses >= MaxUses)
            return true;
        return HasComp<DrinkComponent>(item) && comp.Uses > 0
            && _solutions.TryGetDrainableSolution(item, out _, out var solution) && solution.Volume <= 0;
    }

    /// <summary>What he holds after eating or drinking that belongs in a bin: the empty, or the wrapper the food left.</summary>
    private EntityUid? Rubbish(EntityUid mob, WGCrewDowntimeComponent comp)
    {
        if (comp.Item is { } item && !TerminatingOrDeleted(item) && _hands.IsHolding(mob, item, out _))
            return item;
        foreach (var held in _hands.EnumerateHeld(mob))
        {
            if (_tags.HasTag(held, Trash))
                return held;
        }
        return null;
    }

    private TimeSpan Rest(WGCrewDowntimeComponent comp)
    {
        var spread = Math.Max(0, (comp.MaxRest - comp.MinRest).TotalSeconds);
        return comp.MinRest + TimeSpan.FromSeconds(_random.NextDouble() * spread);
    }

    /// <summary>The nearest working machine on his ship that sells something of the kind, and one such item at random.</summary>
    private (EntityUid Machine, string Item)? FindVendor(EntityUid mob, WGCrewDowntimeComponent comp, TransformComponent xform, WGCrewBreakKind kind)
    {
        (EntityUid, string)? best = null;
        var distance = comp.Range * comp.Range;
        var here = _transform.GetWorldPosition(xform);
        var items = new List<string>();
        var machines = EntityQueryEnumerator<VendingMachineComponent, TransformComponent>();
        while (machines.MoveNext(out var machine, out var vending, out var there))
        {
            if (there.GridUid != xform.GridUid || !there.Anchored || vending.Broken)
                continue;
            var squared = (_transform.GetWorldPosition(there) - here).LengthSquared();
            if (squared >= distance)
                continue;

            items.Clear();
            foreach (var entry in _vending.GetAvailableInventory(machine, vending))
            {
                if (entry.Type == InventoryType.Regular && Sells(entry.ID, kind))
                    items.Add(entry.ID);
            }
            if (items.Count == 0)
                continue;
            best = (machine, _random.Pick(items));
            distance = squared;
        }
        return best;
    }

    /// <summary>Whether an item is a snack (food that needs no utensil) or a drink.</summary>
    private bool Sells(string id, WGCrewBreakKind kind)
    {
        if (!_protos.TryIndex<EntityPrototype>(id, out var proto))
            return false;
        return kind switch
        {
            WGCrewBreakKind.Snack => proto.TryGetComponent<FoodComponent>(out var food, EntityManager.ComponentFactory)
                && !food.UtensilRequired,
            WGCrewBreakKind.Drink => proto.TryGetComponent<DrinkComponent>(out _, EntityManager.ComponentFactory),
            _ => false,
        };
    }

    /// <summary>What the machine just dropped: a loose item of that prototype beside it.</summary>
    private EntityUid? Dropped(EntityUid machine, string? proto)
    {
        if (proto == null)
            return null;
        foreach (var near in EntityManager.System<EntityLookupSystem>().GetEntitiesInRange(machine, 2f))
        {
            if (MetaData(near).EntityPrototype?.ID == proto && !Transform(near).Anchored
                && !EntityManager.System<Robust.Shared.Containers.SharedContainerSystem>().IsEntityInContainer(near))
                return near;
        }
        return null;
    }

    /// <summary>The free chair, stool or bench on his ship nearest a point: no bed, no operating table.</summary>
    private EntityUid? FindSeat(EntityUid mob, WGCrewDowntimeComponent comp, EntityCoordinates near)
    {
        EntityUid? best = null;
        var distance = comp.Range * comp.Range;
        var at = _transform.ToMapCoordinates(near);
        var grid = Transform(mob).GridUid;
        var seats = EntityQueryEnumerator<StrapComponent, TransformComponent>();
        while (seats.MoveNext(out var seat, out var strap, out var xform))
        {
            if (xform.GridUid != grid || !xform.Anchored || strap.Position != StrapPosition.Stand || !SeatFree(seat, mob)
                || HasComp<HealOnBuckleComponent>(seat) || HasComp<WGMedicalBedComponent>(seat) || HasComp<OperatingTableComponent>(seat))
                continue;
            var map = _transform.GetMapCoordinates(seat, xform);
            if (map.MapId != at.MapId)
                continue;
            var squared = (map.Position - at.Position).LengthSquared();
            if (squared < distance)
            {
                best = seat;
                distance = squared;
            }
        }
        return best;
    }

    /// <summary>Nobody in it, and no other crewman on a break headed for it.</summary>
    private bool SeatFree(EntityUid seat, EntityUid mob)
    {
        if (!TryComp<StrapComponent>(seat, out var strap) || !strap.Enabled
            || strap.BuckledEntities.Count > 0 && CompOrNull<BuckleComponent>(mob)?.BuckledTo != seat)
            return false;
        var others = EntityQueryEnumerator<WGCrewDowntimeComponent>();
        while (others.MoveNext(out var other, out var comp))
        {
            if (other != mob && comp.Stage != WGCrewBreakStage.None && comp.Seat == seat)
                return false;
        }
        return true;
    }

    /// <summary>The nearest disposal unit on his ship.</summary>
    private EntityUid? FindBin(EntityUid mob, WGCrewDowntimeComponent comp)
    {
        EntityUid? best = null;
        var distance = comp.Range * comp.Range;
        var xform = Transform(mob);
        var here = _transform.GetWorldPosition(xform);
        var bins = EntityQueryEnumerator<DisposalUnitComponent, TransformComponent>();
        while (bins.MoveNext(out var bin, out _, out var there))
        {
            if (there.GridUid != xform.GridUid || !there.Anchored)
                continue;
            var squared = (_transform.GetWorldPosition(there) - here).LengthSquared();
            if (squared < distance)
            {
                best = bin;
                distance = squared;
            }
        }
        return best;
    }
}
