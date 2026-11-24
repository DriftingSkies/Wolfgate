using System.Linq;
using System.Numerics;
using Content.Server._WG.NpcCrew.Components;
using Content.Server.Chat.Systems;
using Content.Server.Chemistry.Components;
using Content.Shared._WG.NpcCrew;
using Content.Shared.Chat;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Placeable;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WG.NpcCrew.Systems;

/// <summary>
/// NPC bartenders (<see cref="WGCrewBartenderComponent"/>, duty Bartender). Orders come in by speech or verb
/// (<c>WGCrewBartenderSystem.Orders.cs</c>) and are served in turn: he takes a glass from his rack, walks to each
/// dispenser behind the bar that holds what the drink needs, pours it from the real bottles behind a short do-after,
/// and sets the glass on the counter in front of the customer, or hands it over where there is no counter. Idle, he
/// collects empty glasses left on the bar back onto his rack. The HTN walks him (<c>WGCrewBartenderOperator</c>);
/// this system decides where to and does each step.
/// </summary>
public sealed partial class WGCrewBartenderSystem : EntitySystem
{
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private ExamineSystemShared _examine = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WGCrewSystem _crew = default!;
    [Dependency] private WGCrewWeaponSystem _weapons = default!;

    /// <summary>How close he stands to a dispenser, counter, customer or glass to work at it.</summary>
    public const float Reach = 1.5f;

    /// <summary>How far round a customer a counter may stand and still be the one in front of them.</summary>
    private const float CounterReach = 1.6f;

    private static readonly TimeSpan CollectScanGap = TimeSpan.FromSeconds(3);

    private readonly HashSet<EntityUid> _nearby = new();
    private float _accumulator;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGCrewBartenderComponent, WGCrewBartenderDoAfterEvent>(OnPoured);
        SubscribeLocalEvent<WGCrewBartenderComponent, MobStateChangedEvent>(OnState);
        InitializeOrders();
    }

    private void OnState(Entity<WGCrewBartenderComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Alive)
            Reset(ent);
    }

    /// <summary>Whether he is tending the bar: alive, on the Bartender duty and not played by anyone.</summary>
    public bool CanWork(EntityUid uid)
    {
        return TryComp<WGCrewComponent>(uid, out var crew) && crew.Duty == WGCrewDuties.Bartender
            && _mobs.IsAlive(uid) && !HasComp<ActorComponent>(uid);
    }

    /// <summary>Where he stands when there is nothing to do.</summary>
    private EntityCoordinates Post(EntityUid uid)
    {
        return CompOrNull<WGCrewComponent>(uid)?.Post ?? Transform(uid).Coordinates;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        _accumulator += frameTime;
        if (_accumulator < 1f)
            return;

        _accumulator = 0f;
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WGCrewBartenderComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            var ent = (uid, comp);
            if (!CanWork(uid))
            {
                if (comp.Current != null || comp.Queue.Count > 0 || comp.Collect != null)
                    Reset(ent);
                continue;
            }

            Restock(comp, now);
            comp.Queue.RemoveAll(order => !Waiting(comp, order, now));
            if (comp.Current is { } current)
            {
                // A fight or a pour under way stops the clock on the step.
                if (comp.Pouring != null || _weapons.HasLiveThreat(uid))
                    comp.StepSince = now;
                else if (now - comp.StepSince > comp.StepTimeout || now - current.Placed > comp.OrderTimeout)
                    Abort(ent, "wg-bartender-give-up");
                continue;
            }

            if (comp.Collect is { } glass && (!Collectable(ent, glass) || now - comp.StepSince > comp.StepTimeout))
                comp.Collect = null;

            if (comp.Collect == null && comp.Queue.Count == 0 && now >= comp.NextCollectScan)
            {
                comp.NextCollectScan = now + CollectScanGap;
                comp.Collect = FindEmptyGlass(ent);
                comp.StepSince = now;
            }
        }
    }

    /// <summary>Whether a queued order still stands: its customer is about, alive, and it hasn't waited too long.</summary>
    private bool Waiting(WGCrewBartenderComponent comp, WGCrewDrinkOrder order, TimeSpan now)
    {
        return !TerminatingOrDeleted(order.Customer) && !_mobs.IsDead(order.Customer) && now - order.Placed <= comp.OrderTimeout;
    }

    /// <summary>A glass comes back from the wash every <see cref="WGCrewBartenderComponent.RestockTime"/> while the rack is short.</summary>
    private void Restock(WGCrewBartenderComponent comp, TimeSpan now)
    {
        if (comp.Glasses >= comp.MaxGlasses)
        {
            comp.NextRestock = now + comp.RestockTime;
            return;
        }

        if (now < comp.NextRestock)
            return;

        comp.Glasses++;
        comp.NextRestock = now + comp.RestockTime;
    }

    /// <summary>Drops everything under way: on death, or when he leaves the duty. The glass stays wherever it is.</summary>
    public void Reset(Entity<WGCrewBartenderComponent> ent)
    {
        CancelPour(ent);
        ent.Comp.Queue.Clear();
        ent.Comp.Current = null;
        ent.Comp.Glass = null;
        ent.Comp.Poured.Clear();
        ent.Comp.Collect = null;
    }

    private void CancelPour(Entity<WGCrewBartenderComponent> ent)
    {
        if (ent.Comp.Pouring is { } pouring)
        {
            // Cleared first, so the cancelled do-after's event is seen as one he dropped.
            ent.Comp.Pouring = null;
            if (!TerminatingOrDeleted(ent.Owner) && _doAfter.IsRunning(pouring))
                _doAfter.Cancel(pouring);
        }

        ent.Comp.PouringAt = null;
    }

    /// <summary>Starts the next queued order if none is under way, skipping any he can no longer pour.</summary>
    private void EnsureCurrent(Entity<WGCrewBartenderComponent> ent)
    {
        var comp = ent.Comp;
        while (comp.Current == null && comp.Queue.Count > 0)
        {
            var order = comp.Queue[0];
            comp.Queue.RemoveAt(0);
            if (!_prototypes.TryIndex(order.Drink, out var drink))
                continue;

            if (!CanPour(ent, drink))
            {
                Say(ent, "wg-bartender-out", ("drink", DrinkName(drink)), ("name", Name(order.Customer)));
                continue;
            }

            comp.Current = order;
            comp.Glass = null;
            comp.Poured.Clear();
            comp.Collect = null;
            comp.StepSince = _timing.CurTime;
        }
    }

    #region HTN

    /// <summary>Where he walks next: a dispenser for the order under way, the counter to serve it at, or an empty glass to collect.</summary>
    public bool Destination(EntityUid mob, out EntityCoordinates coordinates)
    {
        coordinates = default;
        if (!TryComp<WGCrewBartenderComponent>(mob, out var comp) || !CanWork(mob))
            return false;

        var ent = (mob, comp);
        EnsureCurrent(ent);
        if (comp.Current is { } order && _prototypes.TryIndex(order.Drink, out var drink))
        {
            if (NextSource(ent, drink) is { } source)
            {
                coordinates = Transform(source).Coordinates;
                return true;
            }

            // Nothing left to pour, or nothing left to pour it from: either way the counter, or a word there.
            coordinates = ServeTarget(ent, order, out _) is { } target ? Transform(target).Coordinates : Post(mob);
            return true;
        }

        if (comp.Collect is { } glass && !TerminatingOrDeleted(glass))
        {
            coordinates = Transform(glass).Coordinates;
            return true;
        }

        return false;
    }

    /// <summary>
    /// One step from where he stands: start pouring at a dispenser, serve the drink, or collect a glass. True when he
    /// must walk again before he can do more; false while he is pouring.
    /// </summary>
    public bool Perform(EntityUid mob)
    {
        if (!TryComp<WGCrewBartenderComponent>(mob, out var comp) || !CanWork(mob))
            return true;

        var ent = (mob, comp);
        if (comp.Pouring is { } pouring)
        {
            if (_doAfter.IsRunning(pouring))
                return false;
            comp.Pouring = null;
            comp.PouringAt = null;
        }

        EnsureCurrent(ent);
        if (comp.Current is { } order)
        {
            if (!_prototypes.TryIndex(order.Drink, out var drink))
            {
                Abort(ent, "wg-bartender-give-up");
                return true;
            }

            // Taken from him, or dropped when he went down: the drink is gone.
            if (comp.Glass is { } held && (TerminatingOrDeleted(held) || !_hands.IsHolding(mob, held, out _)))
            {
                comp.Glass = null;
                Abort(ent, "wg-bartender-spilled");
                return true;
            }

            if (NextSource(ent, drink) is { } source)
            {
                if (!_interaction.InRangeUnobstructed(mob, source, Reach + 0.5f))
                    return true;

                if (comp.Glass == null && !TakeGlass(ent, drink))
                {
                    Abort(ent, "wg-bartender-no-glass", restock: false);
                    return true;
                }

                return !StartPour(ent, source);
            }

            if (Unpoured(comp, drink).Any())
            {
                Abort(ent, "wg-bartender-ran-out");
                return true;
            }

            Serve(ent, order, drink);
            return true;
        }

        if (comp.Collect is { } glass)
        {
            if (!Collectable(ent, glass))
            {
                comp.Collect = null;
                return true;
            }

            if (!_interaction.InRangeUnobstructed(mob, glass, Reach))
                return true;

            CollectGlass(ent, glass);
        }

        return true;
    }

    #endregion

    #region Pouring

    /// <summary>The reagents of a drink not yet in the glass.</summary>
    private static IEnumerable<KeyValuePair<ProtoId<ReagentPrototype>, FixedPoint2>> Unpoured(WGCrewBartenderComponent comp,
        WGCrewDrinkPrototype drink)
    {
        return drink.Pour.Where(pour => !comp.Poured.Contains(pour.Key.Id));
    }

    /// <summary>The dispensers behind his bar: on his home grid, within <see cref="WGCrewBartenderComponent.SourceRange"/> of his post.</summary>
    private List<Entity<ReagentDispenserComponent>> Dispensers(Entity<WGCrewBartenderComponent> ent)
    {
        var found = new List<Entity<ReagentDispenserComponent>>();
        if (_crew.HomeGrid(ent.Owner) is not { } home)
            return found;

        var post = _transform.ToMapCoordinates(Post(ent.Owner));
        var query = EntityQueryEnumerator<ReagentDispenserComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var dispenser, out var xform))
        {
            if (xform.GridUid != home || _containers.IsEntityInContainer(uid))
                continue;

            var at = _transform.GetMapCoordinates(uid, xform);
            if (at.MapId != post.MapId || (at.Position - post.Position).Length() > ent.Comp.SourceRange)
                continue;

            found.Add((uid, dispenser));
        }

        return found;
    }

    /// <summary>A bottle in a dispenser's storage slots holding at least the amount of the reagent.</summary>
    private Entity<SolutionComponent>? Bottle(ReagentDispenserComponent dispenser, string reagent, FixedPoint2 amount)
    {
        foreach (var slot in dispenser.StorageSlots)
        {
            if (slot.Item is not { } bottle || !_solutions.TryGetDrainableSolution(bottle, out var soln, out var solution))
                continue;

            if (solution.GetTotalPrototypeQuantity(reagent) >= amount)
                return soln;
        }

        return null;
    }

    /// <summary>Whether every reagent of a drink is in some bottle behind the bar.</summary>
    public bool CanPour(Entity<WGCrewBartenderComponent> ent, WGCrewDrinkPrototype drink)
    {
        var dispensers = Dispensers(ent);
        foreach (var (reagent, amount) in drink.Pour)
        {
            if (!dispensers.Any(dispenser => Bottle(dispenser.Comp, reagent.Id, amount) != null))
                return false;
        }

        return true;
    }

    /// <summary>The nearest dispenser that can give the glass something it still lacks; null when nothing can.</summary>
    private EntityUid? NextSource(Entity<WGCrewBartenderComponent> ent, WGCrewDrinkPrototype drink)
    {
        var missing = Unpoured(ent.Comp, drink).ToList();
        if (missing.Count == 0)
            return null;

        var here = _transform.GetMapCoordinates(ent.Owner);
        EntityUid? best = null;
        var bestDistance = float.MaxValue;
        foreach (var dispenser in Dispensers(ent))
        {
            if (!missing.Any(pour => Bottle(dispenser.Comp, pour.Key.Id, pour.Value) != null))
                continue;

            var distance = (_transform.GetMapCoordinates(dispenser.Owner).Position - here.Position).LengthSquared();
            if (distance >= bestDistance)
                continue;

            best = dispenser.Owner;
            bestDistance = distance;
        }

        return best;
    }

    /// <summary>Takes a clean glass from his rack into his hand.</summary>
    private bool TakeGlass(Entity<WGCrewBartenderComponent> ent, WGCrewDrinkPrototype drink)
    {
        if (ent.Comp.Glasses <= 0)
            return false;

        var glass = Spawn(drink.Glass, Transform(ent.Owner).Coordinates);
        if (!_hands.TryPickupAnyHand(ent.Owner, glass, checkActionBlocker: false))
        {
            Del(glass);
            return false;
        }

        // A full rack losing its first glass starts the restock clock; later ones don't move it.
        if (ent.Comp.Glasses >= ent.Comp.MaxGlasses)
            ent.Comp.NextRestock = _timing.CurTime + ent.Comp.RestockTime;
        ent.Comp.Glasses--;
        ent.Comp.Glass = glass;
        return true;
    }

    private bool StartPour(Entity<WGCrewBartenderComponent> ent, EntityUid source)
    {
        var args = new DoAfterArgs(EntityManager, ent.Owner, ent.Comp.PourTime, new WGCrewBartenderDoAfterEvent(),
            ent.Owner, target: source, used: ent.Comp.Glass)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            DistanceThreshold = 2f,
            NeedHand = false,
            BlockDuplicate = true,
        };

        if (!_doAfter.TryStartDoAfter(args, out var id))
            return false;

        ent.Comp.Pouring = id;
        ent.Comp.PouringAt = source;
        if (TryComp<ReagentDispenserComponent>(source, out var dispenser))
            _audio.PlayPvs(dispenser.ClickSound, source, AudioParams.Default.WithVolume(-2f));
        return true;
    }

    private void OnPoured(Entity<WGCrewBartenderComponent> ent, ref WGCrewBartenderDoAfterEvent args)
    {
        // Only the pour under way is his to finish; a do-after left from one he dropped changes nothing.
        if (ent.Comp.Pouring != args.DoAfter.Id)
            return;

        var source = ent.Comp.PouringAt;
        ent.Comp.Pouring = null;
        ent.Comp.PouringAt = null;
        if (args.Cancelled || args.Handled || source is not { } dispenser || TerminatingOrDeleted(dispenser))
            return;

        // Relieved, or the glass knocked out of his hand while he poured: nothing goes in.
        if (!CanWork(ent.Owner))
        {
            Reset(ent);
            return;
        }
        if (ent.Comp.Glass is not { } glass || !_hands.IsHolding(ent.Owner, glass, out _))
            return;

        args.Handled = true;
        Pour(ent, dispenser);
    }

    /// <summary>
    /// Pours into the glass everything this dispenser has that the drink still lacks, from its real bottles. A mix
    /// reacts in the glass as its ingredients go in.
    /// </summary>
    public void Pour(Entity<WGCrewBartenderComponent> ent, EntityUid source)
    {
        var comp = ent.Comp;
        if (comp.Current is not { } order || comp.Glass is not { } glass || !_prototypes.TryIndex(order.Drink, out var drink)
            || !TryComp<ReagentDispenserComponent>(source, out var dispenser)
            || !_solutions.TryGetRefillableSolution(glass, out var glassSoln, out _))
            return;

        foreach (var (reagent, amount) in Unpoured(comp, drink).ToList())
        {
            if (Bottle(dispenser, reagent.Id, amount) is not { } bottle)
                continue;

            var taken = bottle.Comp.Solution.SplitSolutionWithOnly(amount, reagent.Id);
            _solutions.UpdateChemicals(bottle, false);
            if (!_solutions.TryAddSolution(glassSoln.Value, taken))
            {
                _solutions.TryAddSolution(bottle, taken);
                Abort(ent, "wg-bartender-spilled");
                return;
            }

            comp.Poured.Add(reagent.Id);
        }

        comp.StepSince = _timing.CurTime;
    }

    #endregion

    #region Serving

    /// <summary>
    /// Where the drink goes: the counter in front of the customer, else the customer's own hands. A customer gone or
    /// wandered off gets it on the counter nearest the bartender's post, if any.
    /// </summary>
    private EntityUid? ServeTarget(Entity<WGCrewBartenderComponent> ent, WGCrewDrinkOrder order, out bool counter)
    {
        counter = false;
        var post = _transform.ToMapCoordinates(Post(ent.Owner));
        var home = _crew.HomeGrid(ent.Owner);
        if (!TerminatingOrDeleted(order.Customer) && _mobs.IsAlive(order.Customer))
        {
            var at = _transform.GetMapCoordinates(order.Customer);
            if (at.MapId == post.MapId && (at.Position - post.Position).Length() <= ent.Comp.ServeRange)
            {
                if (NearestCounter(at, CounterReach, home, post) is { } found)
                {
                    counter = true;
                    return found;
                }

                return order.Customer;
            }
        }

        var fallback = NearestCounter(post, ent.Comp.CounterRange, home, post);
        counter = fallback != null;
        return fallback;
    }

    /// <summary>The anchored counter within range of a spot on the grid, the nearest to it; ties go to the one nearer the post.</summary>
    private EntityUid? NearestCounter(MapCoordinates around, float range, EntityUid? grid, MapCoordinates post)
    {
        EntityUid? best = null;
        var bestScore = float.MaxValue;
        Nearby(around, range, _nearby, LookupFlags.Static);
        foreach (var uid in _nearby)
        {
            var xform = Transform(uid);
            if (!xform.Anchored || xform.GridUid != grid || !HasComp<PlaceableSurfaceComponent>(uid))
                continue;

            var position = _transform.GetWorldPosition(xform);
            var score = (position - around.Position).Length() * 10f + (position - post.Position).Length();
            if (score >= bestScore)
                continue;

            best = uid;
            bestScore = score;
        }

        return best;
    }

    /// <summary>Sets the drink down on the counter, or hands it over; with nowhere to put it, at his feet.</summary>
    private void Serve(Entity<WGCrewBartenderComponent> ent, WGCrewDrinkOrder order, WGCrewDrinkPrototype drink)
    {
        var mob = ent.Owner;
        if (ent.Comp.Glass is not { } glass)
            return;

        var target = ServeTarget(ent, order, out var counter);
        if (target is { } spot && !_interaction.InRangeUnobstructed(mob, spot, Reach + 0.5f))
            return;
        if (target == null && !_transform.InRange(Transform(mob).Coordinates, Post(mob), Reach + 0.5f))
            return;

        if (!_hands.TryDrop(mob, glass, checkActionBlocker: false))
            return;

        if (target is { } surface && counter)
        {
            var offset = new Vector2(_random.NextFloat(-0.15f, 0.15f), _random.NextFloat(-0.15f, 0.15f));
            _transform.SetCoordinates(glass, Transform(surface).Coordinates.Offset(offset));
        }
        else if (target is { } customer)
        {
            _transform.SetCoordinates(glass, Transform(customer).Coordinates);
            _hands.TryPickupAnyHand(customer, glass, checkActionBlocker: false);
        }

        SayVariant(ent, "wg-bartender-serve", ("drink", DrinkName(drink)), ("name", Name(order.Customer)));
        ent.Comp.Current = null;
        ent.Comp.Glass = null;
        ent.Comp.Poured.Clear();
        ent.Comp.StepSince = _timing.CurTime;
    }

    /// <summary>Gives the order under way up with a word to its customer; a glass in hand is poured away and goes back on the rack.</summary>
    private void Abort(Entity<WGCrewBartenderComponent> ent, string line, bool restock = true)
    {
        var comp = ent.Comp;
        CancelPour(ent);
        if (comp.Glass is { } glass && !TerminatingOrDeleted(glass) && _hands.IsHolding(ent.Owner, glass, out _))
        {
            QueueDel(glass);
            if (restock)
                comp.Glasses = Math.Min(comp.MaxGlasses, comp.Glasses + 1);
        }

        if (comp.Current is { } order && _prototypes.TryIndex(order.Drink, out var drink))
        {
            var name = TerminatingOrDeleted(order.Customer) ? string.Empty : Name(order.Customer);
            Say(ent, line, ("drink", DrinkName(drink)), ("name", name));
        }

        comp.Current = null;
        comp.Glass = null;
        comp.Poured.Clear();
        comp.StepSince = _timing.CurTime;
    }

    #endregion

    #region Collecting

    /// <summary>The glass prototypes his menu serves in: the only empties he takes back.</summary>
    private HashSet<string> GlassKinds(WGCrewBartenderComponent comp)
    {
        var kinds = new HashSet<string>();
        foreach (var id in comp.Menu)
        {
            if (_prototypes.TryIndex(id, out var drink))
                kinds.Add(drink.Glass.Id);
        }

        return kinds;
    }

    /// <summary>Whether a glass is an empty left lying on a counter of his bar.</summary>
    private bool Collectable(Entity<WGCrewBartenderComponent> ent, EntityUid glass)
    {
        if (TerminatingOrDeleted(glass) || _containers.IsEntityInContainer(glass))
            return false;

        var xform = Transform(glass);
        if (xform.Anchored || xform.GridUid != _crew.HomeGrid(ent.Owner)
            || !_transform.InRange(xform.Coordinates, Post(ent.Owner), ent.Comp.CollectRange + 0.5f))
            return false;

        if (MetaData(glass).EntityPrototype?.ID is not { } kind || !GlassKinds(ent.Comp).Contains(kind))
            return false;

        if (!_solutions.TryGetDrainableSolution(glass, out _, out var solution) || solution.Volume > 0)
            return false;

        return OnCounter(glass);
    }

    /// <summary>Whether something lies on a counter's tile.</summary>
    private bool OnCounter(EntityUid item)
    {
        Nearby(_transform.GetMapCoordinates(item), 0.45f, _nearby, LookupFlags.Static);
        return _nearby.Any(uid => uid != item && HasComp<PlaceableSurfaceComponent>(uid) && Transform(uid).Anchored);
    }

    private void Nearby(MapCoordinates around, float range, HashSet<EntityUid> into, LookupFlags flags)
    {
        into.Clear();
        _lookup.GetEntitiesInRange(around.MapId, around.Position, range, into, flags);
    }

    /// <summary>The empty glass on his bar nearest him, if any.</summary>
    private EntityUid? FindEmptyGlass(Entity<WGCrewBartenderComponent> ent)
    {
        if (ent.Comp.Glasses >= ent.Comp.MaxGlasses)
            return null;

        var post = _transform.ToMapCoordinates(Post(ent.Owner));
        var here = _transform.GetMapCoordinates(ent.Owner);
        var candidates = new HashSet<EntityUid>();
        Nearby(post, ent.Comp.CollectRange, candidates, LookupFlags.Dynamic | LookupFlags.Sundries);
        EntityUid? best = null;
        var bestDistance = float.MaxValue;
        foreach (var uid in candidates)
        {
            if (!Collectable(ent, uid))
                continue;

            var distance = (_transform.GetMapCoordinates(uid).Position - here.Position).LengthSquared();
            if (distance >= bestDistance)
                continue;

            best = uid;
            bestDistance = distance;
        }

        return best;
    }

    /// <summary>Picks the glass up and puts it on his rack to wash.</summary>
    private void CollectGlass(Entity<WGCrewBartenderComponent> ent, EntityUid glass)
    {
        // Hands full: the glass stays on the bar, and the next scan may try it again.
        if (!_hands.TryPickupAnyHand(ent.Owner, glass, checkActionBlocker: false))
        {
            ent.Comp.Collect = null;
            ent.Comp.NextCollectScan = _timing.CurTime + CollectScanGap;
            return;
        }

        QueueDel(glass);
        ent.Comp.Glasses = Math.Min(ent.Comp.MaxGlasses, ent.Comp.Glasses + 1);
        ent.Comp.Collect = null;
        ent.Comp.NextCollectScan = _timing.CurTime + CollectScanGap;
    }

    #endregion

    #region Speech

    private string DrinkName(WGCrewDrinkPrototype drink)
    {
        return Loc.GetString(drink.Name);
    }

    private void Say(EntityUid uid, string key, params (string, object)[] args)
    {
        _chat.TrySendInGameICMessage(uid, Loc.GetString(key, args), InGameICChatType.Speak, false);
    }

    /// <summary>Says one of a line's three wordings (<c>key-1</c> to <c>key-3</c>).</summary>
    private void SayVariant(EntityUid uid, string key, params (string, object)[] args)
    {
        Say(uid, $"{key}-{_random.Next(1, 4)}", args);
    }

    #endregion
}
