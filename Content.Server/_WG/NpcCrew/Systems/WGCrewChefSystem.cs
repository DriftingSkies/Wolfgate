using System.Linq;
using System.Numerics;
using Content.Server._WG.NpcCrew.Components;
using Content.Server.Chat.Systems;
using Content.Server.Kitchen.Components;
using Content.Server.Kitchen.EntitySystems;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Server.Power.Components;
using Content.Server.Storage.Components;
using Content.Server.Storage.EntitySystems;
using Content.Shared._WG.NpcCrew;
using Content.Shared.Chat;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Item;
using Content.Shared.Kitchen;
using Content.Shared.Kitchen.Components;
using Content.Shared.Lock;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Placeable;
using Content.Shared.SmartFridge;
using Content.Shared.Stacks;
using Content.Shared.Tools.Components;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WG.NpcCrew.Systems;

/// <summary>
/// The chef's work: each dish on his menu that is short on the serving counter is cooked again in one of the galley's
/// own appliances. He takes the recipe's ingredients from the stores in reach (a crate, a locker or the smart fridge,
/// or lying loose), puts them in a free appliance that cooks that kind of recipe, starts it with its real recipe
/// timer, takes out what comes of it and puts it on the counter. Nothing is made from nothing: without ingredients he
/// waits, and the only new ones are a pantry's slow restock while he is alive and at work.
/// </summary>
public sealed class WGCrewChefSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private EntityStorageSystem _storage = default!;
    [Dependency] private MicrowaveSystem _microwave = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private NPCSystem _npc = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WGCrewShelterSystem _shelter = default!;
    [Dependency] private WGCrewWeaponSystem _weapons = default!;

    /// <summary>How often an idle chef looks over the counter.</summary>
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(2);

    /// <summary>How long a step of a dish (a fetch, the load, the serve) may take while he is awake before it is given up.</summary>
    private static readonly TimeSpan StepTime = TimeSpan.FromSeconds(60);

    /// <summary>How long past an appliance's own cook time he waits on it.</summary>
    private static readonly TimeSpan CookGrace = TimeSpan.FromSeconds(30);

    /// <summary>How long a chef whose dish went wrong waits before he tries again.</summary>
    private static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(30);

    /// <summary>The HTN walks him to within the interaction range; a little slack for where he stops.</summary>
    private const float Reach = SharedInteractionSystem.InteractionRange + 0.1f;

    /// <summary>How close to a serving spot, or to an appliance, a dish lies to count as on it.</summary>
    private const float SpotRadius = 0.75f;

    /// <summary>How far a dish is put down from the middle of its spot, so a counter's dishes don't all stack.</summary>
    private const float Jitter = 0.2f;

    private const int GreetLines = 3;

    private readonly Dictionary<EntityUid, Job> _jobs = new();
    private readonly HashSet<string> _warned = new();
    private float _timer;

    /// <summary>Whether the chef is making a dish.</summary>
    public bool HasJob(EntityUid chef) => _jobs.ContainsKey(chef);

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        _timer += frameTime;
        if (_timer < 1f)
            return;

        var elapsed = TimeSpan.FromSeconds(_timer);
        _timer = 0f;
        var now = _timing.CurTime;

        foreach (var (uid, job) in _jobs.ToArray())
        {
            if (!TryComp<WGCrewComponent>(uid, out var crew) || !CanWork(uid, crew, Transform(uid)))
                Abort(uid, job, false);
            // An NPC asleep for want of a player nearby has not given up; his time stands still with him.
            else if (!Awake(uid))
                job.Deadline += elapsed;
            else if (now > job.Deadline)
                Abort(uid, job, true);
        }

        var restocked = new HashSet<EntityUid>();
        var query = EntityQueryEnumerator<WGCrewChefComponent, WGCrewComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var chef, out var crew, out var xform))
        {
            if (!CanWork(uid, crew, xform) || xform.GridUid is not { } grid)
                continue;

            if (now >= chef.NextRestock)
            {
                // The first look only starts the clock: a pantry comes aboard stocked.
                if (chef.NextRestock != TimeSpan.Zero && restocked.Add(grid))
                    Restock((uid, chef), grid);
                chef.NextRestock = now + chef.RestockInterval;
            }

            var post = crew.Post ?? xform.Coordinates;
            Greet((uid, chef), post, grid, now);
            if (_jobs.ContainsKey(uid) || now < chef.NextCook)
                continue;

            chef.NextCook = now + CheckInterval;
            TryStart((uid, chef), post, grid);
        }
    }

    /// <summary>Alive, nobody's puppet, on duty in the galley of his own ship, and with no fight or alarm on.</summary>
    private bool CanWork(EntityUid uid, WGCrewComponent crew, TransformComponent xform)
    {
        return crew.Duty == WGCrewDuties.Chef && _mobs.IsAlive(uid) && !HasComp<ActorComponent>(uid)
               && xform.GridUid is { } grid && (crew.Post?.EntityId ?? grid) == grid
               && !_weapons.HasLiveThreat(uid) && !_shelter.IsSheltering(uid);
    }

    private bool Awake(EntityUid uid)
    {
        return TryComp<HTNComponent>(uid, out var htn) && htn.Enabled && _npc.IsAwake(uid, htn);
    }

    /// <summary>Gives a chef the dish most short on the counter that he has an appliance and the ingredients for.</summary>
    private void TryStart(Entity<WGCrewChefComponent> chef, EntityCoordinates post, EntityUid grid)
    {
        var kitchen = Kitchen(post, chef.Comp.KitchenRange, grid);
        var spots = ServeSpots(chef.Comp, post, kitchen);
        if (spots.Count == 0)
            return;

        var stock = Stock(chef.Comp, kitchen, spots, out var stocked);

        // A dish of his left by an appliance, say when a fight took him off it, goes out before anything new is cooked.
        if (StrayDish(chef.Comp, kitchen, stocked) is { } stray)
        {
            _jobs[chef.Owner] = new Job(grid, stray.Recipe, stray.Appliance, new List<EntityUid>())
            {
                Stage = Stage.Collect,
                Meal = stray.Dish,
                Deadline = _timing.CurTime + StepTime,
            };
            return;
        }

        var wanted = new List<(FoodRecipePrototype Recipe, int Short)>();
        foreach (var dish in chef.Comp.Menu)
        {
            if (Recipe(dish) is not { } recipe)
                continue;

            var have = stock.GetValueOrDefault(recipe.Result) + Pending(grid, recipe.Result);
            if (have < dish.Stock)
                wanted.Add((recipe, dish.Stock - have));
        }

        foreach (var (recipe, _) in wanted.OrderByDescending(w => w.Short))
        {
            if (FindAppliance(kitchen, recipe, post) is not { } appliance
                || FindIngredients(kitchen, recipe, stocked) is not { } ingredients)
                continue;

            _jobs[chef.Owner] = new Job(grid, recipe, appliance, ingredients) { Deadline = _timing.CurTime + StepTime };
            return;
        }
    }

    /// <summary>A menu recipe he can cook: one with solid ingredients only.</summary>
    private FoodRecipePrototype? Recipe(WGCrewChefDish dish)
    {
        if (!_prototypes.TryIndex(dish.Recipe, out var recipe))
            return null;

        if (recipe.IngredientsReagents.Count == 0 && recipe.IngredientsSolids.Count > 0)
            return recipe;

        if (_warned.Add(recipe.ID))
            Log.Warning($"Chef menu recipe {recipe.ID} needs reagents or has no solid ingredients; it is skipped.");
        return null;
    }

    /// <summary>Everything within reach of the galley on his ship, stowed things included.</summary>
    private HashSet<EntityUid> Kitchen(EntityCoordinates post, float range, EntityUid grid)
    {
        var found = _lookup.GetEntitiesInRange(_transform.ToMapCoordinates(post), range);
        found.RemoveWhere(uid => Transform(uid).GridUid != grid);
        return found;
    }

    /// <summary>
    /// Where dishes go out: the serving spot markers in the galley, or with none, the tables within
    /// <see cref="WGCrewChefComponent.CounterRange"/> of his post that no machine stands on.
    /// </summary>
    private List<Spot> ServeSpots(WGCrewChefComponent chef, EntityCoordinates post, HashSet<EntityUid> kitchen)
    {
        var spots = new List<Spot>();
        foreach (var uid in kitchen)
        {
            if (HasComp<WGCrewChefServeSpotComponent>(uid))
                spots.Add(new Spot(Transform(uid).Coordinates, SurfaceAt(Transform(uid).Coordinates, kitchen)));
        }

        if (spots.Count > 0)
            return spots;

        foreach (var uid in kitchen)
        {
            var xform = Transform(uid);
            if (!HasComp<PlaceableSurfaceComponent>(uid) || !xform.Anchored || HasComp<MicrowaveComponent>(uid)
                || !_transform.InRange(xform.Coordinates, post, chef.CounterRange) || MachineOn(uid, xform.Coordinates, kitchen))
                continue;

            spots.Add(new Spot(xform.Coordinates, uid));
        }

        return spots;
    }

    /// <summary>The table under a spot, if there is one.</summary>
    private EntityUid? SurfaceAt(EntityCoordinates at, HashSet<EntityUid> kitchen)
    {
        foreach (var uid in kitchen)
        {
            var xform = Transform(uid);
            if (HasComp<PlaceableSurfaceComponent>(uid) && xform.Anchored && _transform.InRange(xform.Coordinates, at, 0.5f))
                return uid;
        }

        return null;
    }

    /// <summary>Whether a powered machine (a microwave, a grinder) stands on a table.</summary>
    private bool MachineOn(EntityUid surface, EntityCoordinates at, HashSet<EntityUid> kitchen)
    {
        foreach (var uid in kitchen)
        {
            if (uid != surface && HasComp<ApcPowerReceiverComponent>(uid) && !_containers.IsEntityInContainer(uid)
                && _transform.InRange(Transform(uid).Coordinates, at, 0.5f))
                return true;
        }

        return false;
    }

    /// <summary>How many of each menu dish are out: on a serving spot, or in a smart fridge in the galley.</summary>
    private Dictionary<string, int> Stock(WGCrewChefComponent chef, HashSet<EntityUid> kitchen, List<Spot> spots,
        out HashSet<EntityUid> stocked)
    {
        var dishes = MenuDishes(chef);
        var stock = new Dictionary<string, int>();
        stocked = new HashSet<EntityUid>();
        foreach (var uid in kitchen)
        {
            if (MetaData(uid).EntityPrototype?.ID is not { } id || !dishes.Contains(id))
                continue;

            if (_containers.TryGetContainingContainer(uid, out var container))
            {
                if (!HasComp<SmartFridgeComponent>(container.Owner))
                    continue;
            }
            else
            {
                var at = Transform(uid).Coordinates;
                if (!spots.Any(spot => _transform.InRange(at, spot.At, SpotRadius)))
                    continue;
            }

            stock[id] = stock.GetValueOrDefault(id) + 1;
            stocked.Add(uid);
        }

        return stock;
    }

    private HashSet<string> MenuDishes(WGCrewChefComponent chef)
    {
        var dishes = new HashSet<string>();
        foreach (var dish in chef.Menu)
        {
            if (_prototypes.TryIndex(dish.Recipe, out var recipe))
                dishes.Add(recipe.Result);
        }

        return dishes;
    }

    /// <summary>Dishes of this kind already on the way to the counter of this ship.</summary>
    private int Pending(EntityUid grid, string result)
    {
        var count = 0;
        foreach (var job in _jobs.Values)
        {
            if (job.Grid == grid && job.Recipe.Result == result)
                count += Math.Max(1, job.Recipe.ResultCount);
        }

        return count;
    }

    /// <summary>A menu dish lying loose by an appliance, not on the counter and not anyone's.</summary>
    private (EntityUid Dish, FoodRecipePrototype Recipe, EntityUid Appliance)? StrayDish(WGCrewChefComponent chef,
        HashSet<EntityUid> kitchen, HashSet<EntityUid> stocked)
    {
        var appliances = kitchen.Where(uid => HasComp<MicrowaveComponent>(uid)).ToList();
        if (appliances.Count == 0)
            return null;

        foreach (var dish in chef.Menu)
        {
            if (!_prototypes.TryIndex(dish.Recipe, out var recipe))
                continue;

            foreach (var uid in kitchen)
            {
                if (stocked.Contains(uid) || MetaData(uid).EntityPrototype?.ID != recipe.Result || Claimed(uid, null)
                    || _containers.IsEntityInContainer(uid))
                    continue;

                var at = Transform(uid).Coordinates;
                foreach (var appliance in appliances)
                {
                    if (_transform.InRange(Transform(appliance).Coordinates, at, SpotRadius))
                        return (uid, recipe, appliance);
                }
            }
        }

        return null;
    }

    /// <summary>The nearest free, powered appliance in the galley that cooks this kind of recipe.</summary>
    private EntityUid? FindAppliance(HashSet<EntityUid> kitchen, FoodRecipePrototype recipe, EntityCoordinates post)
    {
        var size = recipe.IngredientsSolids.Values.Sum(amount => amount.Int());
        var from = _transform.ToMapCoordinates(post).Position;
        EntityUid? best = null;
        var bestDistance = float.MaxValue;
        foreach (var uid in kitchen)
        {
            if (!TryComp<MicrowaveComponent>(uid, out var oven) || oven.Broken || (oven.ValidRecipeTypes & recipe.RecipeType) == 0
                || oven.Capacity < size || oven.Storage.ContainedEntities.Count > 0 || HasComp<ActiveMicrowaveComponent>(uid)
                || !(TryComp<ApcPowerReceiverComponent>(uid, out var power) && power.Powered) || Claimed(uid, null))
                continue;

            var distance = (_transform.GetMapCoordinates(uid).Position - from).LengthSquared();
            if (distance >= bestDistance)
                continue;

            best = uid;
            bestDistance = distance;
        }

        return best;
    }

    /// <summary>Loose ingredients enough for one batch of the recipe, or null when the stores are short.</summary>
    private List<EntityUid>? FindIngredients(HashSet<EntityUid> kitchen, FoodRecipePrototype recipe, HashSet<EntityUid> stocked)
    {
        var found = new List<EntityUid>();
        foreach (var (solid, amount) in recipe.IngredientsSolids)
        {
            var needed = amount.Int();
            foreach (var uid in kitchen)
            {
                if (needed <= 0)
                    break;
                if (found.Contains(uid) || stocked.Contains(uid) || IngredientId(uid) != solid || !Loose(uid, null))
                    continue;

                found.Add(uid);
                needed -= Units(uid);
            }

            if (needed > 0)
                return null;
        }

        return found;
    }

    /// <summary>The id a recipe knows an item by: a stack's own item, else the item's prototype.</summary>
    private string? IngredientId(EntityUid uid)
    {
        if (TryComp<StackComponent>(uid, out var stack))
            return _prototypes.TryIndex<StackPrototype>(stack.StackTypeId, out var type) ? type.Spawn.Id : null;
        return MetaData(uid).EntityPrototype?.ID;
    }

    private int Units(EntityUid uid)
    {
        return TryComp<StackComponent>(uid, out var stack) ? stack.Count : 1;
    }

    /// <summary>
    /// Whether an item is the galley's to cook with: lying about, or stowed in an unlocked crate, locker or smart fridge.
    /// Not in anyone's hands or pockets, in a machine, or claimed by another job.
    /// </summary>
    private bool Loose(EntityUid uid, Job? own)
    {
        if (!HasComp<ItemComponent>(uid) || Claimed(uid, own))
            return false;
        if (!_containers.TryGetContainingContainer(uid, out var container))
            return !Transform(uid).Anchored;

        var owner = container.Owner;
        if (Shut(owner) || !HasComp<SmartFridgeComponent>(owner)
            && !(HasComp<EntityStorageComponent>(owner) && !HasComp<MobStateComponent>(owner)))
            return false;

        // Nothing above it may be shut either: an unlocked crate inside a locked locker is still locked away.
        var above = owner;
        while (_containers.TryGetContainingContainer(above, out var outer))
        {
            above = outer.Owner;
            if (HasComp<MobStateComponent>(above) || Shut(above))
                return false;
        }
        return true;
    }

    /// <summary>Locked or welded shut.</summary>
    private bool Shut(EntityUid uid)
    {
        return TryComp<LockComponent>(uid, out var locked) && locked.Locked
               || TryComp<WeldableComponent>(uid, out var weld) && weld.IsWelded;
    }

    /// <summary>Whether another job than <paramref name="own"/> has an item or appliance.</summary>
    private bool Claimed(EntityUid uid, Job? own)
    {
        foreach (var job in _jobs.Values)
        {
            if (job != own && (job.Appliance == uid || job.Meal == uid || job.Fetch.Contains(uid) || job.Held.Contains(uid)))
                return true;
        }

        return false;
    }

    /// <summary>The crate, locker or fridge an item is stowed in.</summary>
    private EntityUid? Holder(EntityUid item)
    {
        return _containers.TryGetContainingContainer(item, out var container) ? container.Owner : null;
    }

    /// <summary>Where the chef walks for the step his dish is at, aborting a dish that can no longer be made.</summary>
    public bool Destination(EntityUid mob, out EntityCoordinates coordinates)
    {
        coordinates = default;
        if (!_jobs.TryGetValue(mob, out var job))
            return false;

        if (Target(mob, job) is not { } target)
        {
            Abort(mob, job, true);
            return false;
        }

        coordinates = Transform(target).Coordinates;
        if (job.Stage == Stage.Serve)
            coordinates = job.ServeAt;
        return true;
    }

    /// <summary>What the current step is done at, or null when it has fallen through.</summary>
    private EntityUid? Target(EntityUid mob, Job job)
    {
        foreach (var item in job.Held)
        {
            if (!_hands.IsHolding(mob, item, out _))
                return null;
        }

        switch (job.Stage)
        {
            case Stage.Fetch:
                var next = job.Fetch[0];
                if (TerminatingOrDeleted(next) || TerminatingOrDeleted(job.Appliance) || !Loose(next, job))
                    return null;
                return Holder(next) ?? next;
            case Stage.Load:
            case Stage.Cook:
                return TerminatingOrDeleted(job.Appliance) ? null : job.Appliance;
            case Stage.Collect:
                return job.Meal is { } meal && !TerminatingOrDeleted(meal) && !_containers.IsEntityInContainer(meal) ? meal : null;
            case Stage.Serve:
                return job.Meal is { } held && _hands.IsHolding(mob, held, out _) ? held : null;
        }

        return null;
    }

    /// <summary>Does the current step once the chef has walked there; false while he waits on the appliance.</summary>
    public bool Perform(EntityUid mob)
    {
        if (!_jobs.TryGetValue(mob, out var job) || !Destination(mob, out var at))
            return true;

        var target = job.Stage == Stage.Serve ? job.Surface : Target(mob, job);
        if (!_transform.InRange(Transform(mob).Coordinates, at, Reach)
            || !(target is { } reach ? _interaction.InRangeUnobstructed(mob, reach, Reach) : _interaction.InRangeUnobstructed(mob, at, Reach)))
            return true;

        var now = _timing.CurTime;
        switch (job.Stage)
        {
            case Stage.Fetch:
                return Fetch(mob, job, now);
            case Stage.Load:
                return Load(mob, job, now);
            case Stage.Cook:
                return Cook(mob, job, now);
            case Stage.Collect:
                if (job.Meal is not { } meal || !_hands.TryPickupAnyHand(mob, meal))
                {
                    Abort(mob, job, true);
                    return true;
                }

                if (!PickSpot(mob, job))
                {
                    Abort(mob, job, true);
                    return true;
                }

                job.Stage = Stage.Serve;
                job.Deadline = now + StepTime;
                return true;
            case Stage.Serve:
                Serve(mob, job);
                return true;
        }

        return true;
    }

    /// <summary>Takes the next ingredient out of the stores; with his hands full or nothing left to fetch, on to the appliance.</summary>
    private bool Fetch(EntityUid mob, Job job, TimeSpan now)
    {
        var item = job.Fetch[0];
        if (_containers.TryGetContainingContainer(item, out var stowed))
            _containers.Remove(item, stowed);
        if (!_hands.TryPickupAnyHand(mob, item))
        {
            Abort(mob, job, true);
            return true;
        }

        job.Fetch.RemoveAt(0);
        job.Held.Add(item);
        if (job.Fetch.Count == 0 || !_hands.TryGetEmptyHand(mob, out _))
            job.Stage = Stage.Load;
        job.Deadline = now + StepTime;
        return true;
    }

    /// <summary>Puts what he carries in the appliance; with every ingredient in, sets its recipe's time and starts it.</summary>
    private bool Load(EntityUid mob, Job job, TimeSpan now)
    {
        // Power can go while he fetches: then the dish is given up and what he holds is dropped where he stands, not
        // put into a dead oven.
        if (!TryComp<MicrowaveComponent>(job.Appliance, out var oven) || oven.Broken || HasComp<ActiveMicrowaveComponent>(job.Appliance)
            || !(TryComp<ApcPowerReceiverComponent>(job.Appliance, out var power) && power.Powered))
        {
            Abort(mob, job, true);
            return true;
        }

        foreach (var item in job.Held.ToArray())
        {
            if (!_hands.TryDropIntoContainer(mob, item, oven.Storage))
            {
                Abort(mob, job, true);
                return true;
            }

            job.Held.Remove(item);
            job.Loaded = true;
        }

        _microwave.UpdateUserInterfaceState(job.Appliance, oven);
        job.Deadline = now + StepTime;
        if (job.Fetch.Count > 0)
        {
            job.Stage = Stage.Fetch;
            return true;
        }

        oven.CurrentCookTimerTime = job.Recipe.CookTime;
        _microwave.Wzhzhzh(job.Appliance, oven, mob);
        if (!TryComp<ActiveMicrowaveComponent>(job.Appliance, out var active))
        {
            Abort(mob, job, true);
            return true;
        }

        if (active.PortionedRecipe.Item1?.ID != job.Recipe.ID)
            Log.Debug($"{ToPrettyString(job.Appliance)} took {job.Recipe.ID}'s ingredients for {active.PortionedRecipe.Item1?.ID ?? "nothing"}");

        job.Stage = Stage.Cook;
        job.Deadline = now + TimeSpan.FromSeconds(job.Recipe.CookTime * oven.FinalCookTimeMultiplier) + CookGrace;
        return false;
    }

    /// <summary>Waits at the appliance until it is done, then takes the dish out.</summary>
    private bool Cook(EntityUid mob, Job job, TimeSpan now)
    {
        if (HasComp<ActiveMicrowaveComponent>(job.Appliance))
            return false;

        // The appliance empties itself when it is done.
        job.Loaded = false;
        if (FindMeal(job) is not { } meal)
        {
            Say(mob, Loc.GetString("wg-crew-chef-ruined"));
            Abort(mob, job, true);
            return true;
        }

        job.Meal = meal;
        job.Stage = Stage.Collect;
        job.Deadline = now + StepTime;
        return true;
    }

    /// <summary>The dish the appliance turned out, lying where it came out.</summary>
    private EntityUid? FindMeal(Job job)
    {
        foreach (var uid in _lookup.GetEntitiesInRange(_transform.GetMapCoordinates(job.Appliance), SpotRadius, LookupFlags.Uncontained))
        {
            if (MetaData(uid).EntityPrototype?.ID == job.Recipe.Result && !Claimed(uid, job))
                return uid;
        }

        return null;
    }

    /// <summary>Picks the serving spot with the fewest dishes on it, the nearest his post on a tie.</summary>
    private bool PickSpot(EntityUid mob, Job job)
    {
        if (!TryComp<WGCrewChefComponent>(mob, out var chef) || !TryComp<WGCrewComponent>(mob, out var crew))
            return false;

        var post = crew.Post ?? Transform(mob).Coordinates;
        var kitchen = Kitchen(post, chef.KitchenRange, job.Grid);
        var spots = ServeSpots(chef, post, kitchen);
        if (spots.Count == 0)
            return false;

        Stock(chef, kitchen, spots, out var stocked);
        var from = _transform.ToMapCoordinates(post).Position;
        Spot? best = null;
        var bestCount = int.MaxValue;
        var bestDistance = float.MaxValue;
        foreach (var spot in spots)
        {
            var count = stocked.Count(uid => !_containers.IsEntityInContainer(uid)
                                             && _transform.InRange(Transform(uid).Coordinates, spot.At, SpotRadius));
            var distance = (_transform.ToMapCoordinates(spot.At).Position - from).LengthSquared();
            if (count > bestCount || count == bestCount && distance >= bestDistance)
                continue;

            best = spot;
            bestCount = count;
            bestDistance = distance;
        }

        if (best is not { } chosen)
            return false;

        job.ServeAt = chosen.At.Offset(new Vector2(_random.NextFloat(-Jitter, Jitter), _random.NextFloat(-Jitter, Jitter)));
        job.Surface = chosen.Surface;
        return true;
    }

    /// <summary>Puts the dish out on the counter and calls it.</summary>
    private void Serve(EntityUid mob, Job job)
    {
        if (job.Meal is { } meal && _hands.TryDrop(mob, meal, job.ServeAt) && TryComp<WGCrewChefComponent>(mob, out var chef)
            && _timing.CurTime >= chef.NextServeBark)
        {
            chef.NextServeBark = _timing.CurTime + chef.ServeBarkCooldown;
            Say(mob, Loc.GetString("wg-crew-chef-served", ("dish", Name(meal))));
        }

        End(mob, job);
    }

    /// <summary>The single exit for a dish: whatever of it he still holds is put down where he stands.</summary>
    private void End(EntityUid mob, Job job)
    {
        _jobs.Remove(mob);
        if (TerminatingOrDeleted(mob))
            return;

        foreach (var item in job.Held)
        {
            _hands.TryDrop(mob, item);
        }

        if (job.Meal is { } meal && _hands.IsHolding(mob, meal, out _))
            _hands.TryDrop(mob, meal);
    }

    /// <summary>
    /// Gives a dish up: what he holds goes down, and what he had put in an appliance that never started comes back
    /// out, so the ingredients stay in the galley for the next try.
    /// </summary>
    private void Abort(EntityUid mob, Job job, bool failed)
    {
        End(mob, job);
        if (job.Loaded && !TerminatingOrDeleted(job.Appliance) && !HasComp<ActiveMicrowaveComponent>(job.Appliance)
            && TryComp<MicrowaveComponent>(job.Appliance, out var oven))
        {
            _containers.EmptyContainer(oven.Storage);
            _microwave.UpdateUserInterfaceState(job.Appliance, oven);
        }

        if (failed && TryComp<WGCrewChefComponent>(mob, out var chef))
            chef.NextCook = _timing.CurTime + RetryAfter;
    }

    /// <summary>
    /// Adds one dish's worth of ingredients to a pantry on the ship, for the menu dish it holds fewest sets of, unless
    /// it holds <see cref="WGCrewChefComponent.PantrySets"/> of every dish already (an ingredient two dishes share is
    /// counted toward one of them only). The ship's provisioning, at one dish
    /// per <see cref="WGCrewChefComponent.RestockInterval"/>. False when nothing was added.
    /// </summary>
    public bool Restock(Entity<WGCrewChefComponent> chef, EntityUid grid)
    {
        EntityUid? found = null;
        var pantries = EntityQueryEnumerator<WGCrewChefPantryComponent, EntityStorageComponent, TransformComponent>();
        while (pantries.MoveNext(out var uid, out _, out _, out var xform))
        {
            if (xform.GridUid != grid)
                continue;

            found = uid;
            break;
        }

        if (found is not { } pantry)
            return false;

        var have = new Dictionary<string, int>();
        foreach (var item in Comp<EntityStorageComponent>(pantry).Contents.ContainedEntities)
        {
            if (IngredientId(item) is { } id)
                have[id] = have.GetValueOrDefault(id) + Units(item);
        }

        // Shares the pantry out a set at a time round the menu, so an ingredient two dishes use (a bun) counts once.
        var recipes = chef.Comp.Menu.Select(Recipe).OfType<FoodRecipePrototype>().ToList();
        var sets = new int[recipes.Count];
        var shared = true;
        while (shared)
        {
            shared = false;
            for (var i = 0; i < recipes.Count; i++)
            {
                if (sets[i] >= chef.Comp.PantrySets || !recipes[i].IngredientsSolids.All(solid => have.GetValueOrDefault(solid.Key) >= solid.Value.Int()))
                    continue;

                foreach (var (solid, amount) in recipes[i].IngredientsSolids)
                {
                    have[solid] = have.GetValueOrDefault(solid) - amount.Int();
                }

                sets[i]++;
                shared = true;
            }
        }

        var lowest = -1;
        for (var i = 0; i < recipes.Count; i++)
        {
            if (sets[i] < chef.Comp.PantrySets && (lowest < 0 || sets[i] < sets[lowest]))
                lowest = i;
        }

        if (lowest < 0)
            return false;

        var at = Transform(pantry).Coordinates;
        var stored = false;
        foreach (var (solid, amount) in recipes[lowest].IngredientsSolids)
        {
            // Whatever is left over after the sharing out goes toward this set first.
            var missing = amount.Int() - Math.Max(0, have.GetValueOrDefault(solid));
            for (var i = 0; i < missing; i++)
            {
                // One that won't go in (a full or open crate) is not made at all: left beside it, it wasn't counted
                // next time and another came.
                var item = Spawn(solid, at);
                if (_storage.Insert(item, pantry))
                    stored = true;
                else
                    Del(item);
            }
        }

        return stored;
    }

    /// <summary>Calls out what's on to a player who comes up to him, now and then.</summary>
    private void Greet(Entity<WGCrewChefComponent> chef, EntityCoordinates post, EntityUid grid, TimeSpan now)
    {
        if (now < chef.Comp.NextGreeting)
            return;

        foreach (var (player, at) in chef.Comp.Greeted.ToArray())
        {
            if (TerminatingOrDeleted(player) || now - at >= chef.Comp.GreetSamePlayer)
                chef.Comp.Greeted.Remove(player);
        }

        foreach (var (player, _) in _lookup.GetEntitiesInRange<ActorComponent>(_transform.GetMapCoordinates(chef.Owner), chef.Comp.GreetRange))
        {
            if (player == chef.Owner || !_mobs.IsAlive(player) || HasComp<WGCrewComponent>(player)
                || chef.Comp.Greeted.ContainsKey(player))
                continue;

            chef.Comp.Greeted[player] = now;
            chef.Comp.NextGreeting = now + chef.Comp.GreetCooldown;

            var kitchen = Kitchen(post, chef.Comp.KitchenRange, grid);
            Stock(chef.Comp, kitchen, ServeSpots(chef.Comp, post, kitchen), out var stocked);
            var line = stocked.Count > 0
                ? Loc.GetString($"wg-crew-chef-greet-{_random.Next(1, GreetLines + 1)}", ("dish", Name(_random.Pick(stocked.ToList()))))
                : Loc.GetString("wg-crew-chef-greet-cooking");
            Say(chef.Owner, line);
            return;
        }
    }

    private void Say(EntityUid chef, string line)
    {
        _chat.TrySendInGameICMessage(chef, line, InGameICChatType.Speak, ChatTransmitRange.Normal);
    }

    private readonly record struct Spot(EntityCoordinates At, EntityUid? Surface);

    private enum Stage : byte
    {
        /// <summary>Taking ingredients from the stores, as many as his hands hold.</summary>
        Fetch,

        /// <summary>Putting them in the appliance, and starting it once they are all in.</summary>
        Load,

        /// <summary>Waiting on the appliance.</summary>
        Cook,

        /// <summary>Picking the dish up where it came out.</summary>
        Collect,

        /// <summary>Carrying it to the counter.</summary>
        Serve,
    }

    /// <summary>One dish under way.</summary>
    private sealed class Job(EntityUid grid, FoodRecipePrototype recipe, EntityUid appliance, List<EntityUid> fetch)
    {
        public readonly EntityUid Grid = grid;
        public readonly FoodRecipePrototype Recipe = recipe;
        public readonly EntityUid Appliance = appliance;

        /// <summary>Ingredients still to take from the stores, in order.</summary>
        public readonly List<EntityUid> Fetch = fetch;

        /// <summary>Ingredients in his hands on the way to the appliance.</summary>
        public readonly List<EntityUid> Held = new();

        public Stage Stage = Stage.Fetch;

        /// <summary>Whether anything of this dish is in the appliance before it started.</summary>
        public bool Loaded;

        public EntityUid? Meal;
        public EntityCoordinates ServeAt;
        public EntityUid? Surface;

        /// <summary>When the current step is given up, counted only while he is awake.</summary>
        public TimeSpan Deadline;
    }
}
