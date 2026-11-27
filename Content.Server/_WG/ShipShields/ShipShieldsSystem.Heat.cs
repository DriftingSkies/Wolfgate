using Content.Server._Mono.FireControl;
using Content.Server._WG.ShipShields;
using Content.Server.Power.Components;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._WG.ShipShields;
using Content.Shared.Explosion.Components;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Hitscan.Components;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Crescent.ShipShields;

// A ship's heat lives on its hull (WGShipHeatComponent). A generator's damage is that heat as the upstream shield rules
// see it: whatever raises or sets the damage (absorbed hits, other systems) is carried to the hull, and every
// generator on the hull reads the same heat back. A hull holds and sheds heat by its size, plus a built-in sink; a
// ship generator takes the sink's place with its own capacity and cooling. Its damage is the heat scaled to its own range (WolfgateGeneratorShare), so
// the upstream limits are reached when the whole ship is full.
// Every ship cools the same way: a plain rate (hull, plus a powered generator) in its state, field raised, field
// down, overloaded or venting (WGShipShieldEffects.CoolingRate). A bare hull has no field, so it is down.
// Heat that came through a ship's shield is hard: it stays while the field is up and taking hits. The rest is soft
// and cools anyway; a ship's own guns add it with every shot, with or without a shield.
public sealed partial class ShipShieldsSystem
{
    [Dependency] private IComponentFactory _wgHeatFactory = default!;

    /// <summary>Shield damage of one shot of each projectile prototype.</summary>
    private readonly Dictionary<string, float> _wgShotDamage = new();

    /// <summary>Whether each projectile prototype is a missile.</summary>
    private readonly Dictionary<string, bool> _wgMissiles = new();

    /// <summary>Ships too hot to fire.</summary>
    private readonly HashSet<EntityUid> _wgHeatHeld = new();

    /// <summary>The hull each generator reads its heat from and the damage it was last given, so a later change to it can be told apart.</summary>
    private readonly Dictionary<EntityUid, (EntityUid Grid, float Damage)> _wgHeatSinks = new();
    private readonly HashSet<EntityUid> _wgHeatSunkHulls = new();
    private readonly List<EntityUid> _wgHeatGone = new();
    private float _wgHullHeatAccumulator;

    /// <summary>The generators bolted down on each hull, listed once a tick and again after one is bolted down.</summary>
    private readonly Dictionary<EntityUid, List<EntityUid>> _wgHullGenerators = new();
    private readonly Stack<List<EntityUid>> _wgGeneratorLists = new();
    private GameTick _wgHullGeneratorsTick;
    private bool _wgHullGeneratorsStale = true;

    private void InitializeWolfgateShipHeat()
    {
        SubscribeLocalEvent<FireControllableComponent, GunShotEvent>(OnWolfgateShipGunShot);
        SubscribeLocalEvent<WGShipHeatComponent, ComponentStartup>(OnWolfgateHullHeatStartup);
        SubscribeLocalEvent<WGShipHeatComponent, ComponentShutdown>(OnWolfgateHullHeatShutdown);
        SubscribeLocalEvent<ShipShieldEmitterComponent, ComponentStartup>(OnWolfgateGeneratorStartup);
        SubscribeLocalEvent<ShipShieldEmitterComponent, AnchorStateChangedEvent>(OnWolfgateGeneratorAnchor);
    }

    private void OnWolfgateHullHeatStartup(Entity<WGShipHeatComponent> grid, ref ComponentStartup args)
    {
        SizeWolfgateHull(grid, grid.Comp);
    }

    private void OnWolfgateHullHeatShutdown(Entity<WGShipHeatComponent> grid, ref ComponentShutdown args)
    {
        _wgHeatHeld.Remove(grid);
        _wgGunLocks.Remove(grid);
    }

    private void OnWolfgateGeneratorStartup(Entity<ShipShieldEmitterComponent> generator, ref ComponentStartup args)
    {
        _wgHullGeneratorsStale = true;
    }

    private void OnWolfgateGeneratorAnchor(Entity<ShipShieldEmitterComponent> generator, ref AnchorStateChangedEvent args)
    {
        _wgHullGeneratorsStale = true;
    }

    /// <summary>
    /// The generators bolted down on a hull, if any. Every emitter is looked at once a tick, not once for each gun,
    /// panel and shot that asks; one taken off its hull since is still listed, so check each before use.
    /// </summary>
    private List<EntityUid>? WolfgateHullGenerators(EntityUid grid)
    {
        var tick = _wgShieldTiming.CurTick;
        if (_wgHullGeneratorsStale || _wgHullGeneratorsTick != tick)
        {
            foreach (var used in _wgHullGenerators.Values)
            {
                used.Clear();
                _wgGeneratorLists.Push(used);
            }
            _wgHullGenerators.Clear();
            var query = EntityQueryEnumerator<ShipShieldEmitterComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out _, out var transform))
            {
                if (!transform.Anchored || transform.GridUid is not { } hull)
                    continue;
                if (!_wgHullGenerators.TryGetValue(hull, out var list))
                    _wgHullGenerators[hull] = list = _wgGeneratorLists.TryPop(out var spare) ? spare : new List<EntityUid>();
                list.Add(uid);
            }
            _wgHullGeneratorsTick = tick;
            _wgHullGeneratorsStale = false;
        }
        return _wgHullGenerators.GetValueOrDefault(grid);
    }

    /// <summary>Sets what a hull holds and sheds by its tiles, before its sink or generator: a bigger hull holds more and cools faster.</summary>
    private void SizeWolfgateHull(EntityUid grid, WGShipHeatComponent hull)
    {
        hull.Sized = true;
        var count = 0;
        if (TryComp<MapGridComponent>(grid, out var map))
        {
            var tiles = _wgShieldMap.GetAllTilesEnumerator(grid, map);
            while (tiles.MoveNext(out _))
                count++;
        }
        hull.Capacity = WGShipShieldEffects.HullCapacity(hull.BaseCapacity, hull.CapacityPerTile, hull.MaxCapacity, count);
        hull.Dissipation = hull.CoolSeconds > 0f ? hull.Capacity / hull.CoolSeconds : 0f;
    }

    /// <summary>Heat a hull holds by its size, measured again if its tiles changed.</summary>
    private float WolfgateHullCapacity(EntityUid grid, WGShipHeatComponent hull)
    {
        if (!hull.Sized)
            SizeWolfgateHull(grid, hull);
        return hull.Capacity;
    }

    /// <summary>
    /// The share of its ship's heat capacity a generator supplies; the hull's size supplies the rest. A generator's
    /// damage is the hull's heat times this. 1 for an outpost generator, which rules its hull alone.
    /// </summary>
    public float WolfgateGeneratorShare(EntityUid uid, ShipShieldEmitterComponent emitter)
    {
        if (!HasComp<WGShipShieldCostComponent>(uid))
            return 1f;
        var transform = Transform(uid);
        if (!transform.Anchored || transform.GridUid is not { } grid || TerminatingOrDeleted(grid))
            return 1f;
        var hull = EnsureComp<WGShipHeatComponent>(grid);
        var own = WGShipShieldEffects.EffectiveCapacity(emitter.DamageLimit, emitter.MaxDraw, emitter.PowerModifier, emitter.DamageExp);
        var all = own + WolfgateHullCapacity(grid, hull);
        return own > 0f && float.IsFinite(all) && all > own ? own / all : 1f;
    }

    /// <summary>
    /// What a hit its field absorbed charges a generator: its share of the damage, and on a ship that damage at the
    /// generator's <c>hitHeat</c>, weighed by the size of the <paramref name="hit"/> before shunting
    /// (<see cref="WGShipShieldEffects.HitWeight"/>, so point defence can't stunlock a field), and no more than
    /// <see cref="WGShipShieldEffects.HitHeatCap"/> of its capacity, so one warhead can't overrun a cool field.
    /// </summary>
    private float WolfgateHitCharge(EntityUid uid, ShipShieldEmitterComponent emitter, float damage, float hit)
    {
        var charged = damage * WolfgateGeneratorShare(uid, emitter);
        if (!TryComp<WGShipShieldCostComponent>(uid, out var cost))
            return charged;
        charged *= MathF.Max(0f, cost.HitHeat) * WGShipShieldEffects.HitWeight(hit);
        if (TryComp<WGShipShieldHardenedComponent>(Transform(uid).GridUid, out var hardened))
            charged *= Math.Clamp(hardened.HitFactor, 0f, 1f);
        var capacity = WGShipShieldEffects.EffectiveCapacity(emitter.DamageLimit, emitter.MaxDraw, emitter.PowerModifier, emitter.DamageExp);
        return capacity > 0f ? MathF.Min(charged, capacity * WGShipShieldEffects.HitHeatCap) : charged;
    }

    /// <summary>Heat per second the hull under this generator sheds by its own size at the plain rate; 0 for an outpost generator.</summary>
    public float WolfgateHullCooling(EntityUid uid)
    {
        if (!HasComp<WGShipShieldCostComponent>(uid))
            return 0f;
        var transform = Transform(uid);
        if (!transform.Anchored || transform.GridUid is not { } grid || TerminatingOrDeleted(grid))
            return 0f;
        var hull = EnsureComp<WGShipHeatComponent>(grid);
        WolfgateHullCapacity(grid, hull);
        return float.IsFinite(hull.Dissipation) ? MathF.Max(0f, hull.Dissipation) : 0f;
    }

    /// <summary>
    /// A ship's plain cooling under a generator: the generator's own rate while it has power, plus the hull's.
    /// A dead generator is no radiator.
    /// </summary>
    public float WolfgateGeneratorCooling(EntityUid uid, ShipShieldEmitterComponent emitter)
    {
        var own = TryComp<ApcPowerReceiverComponent>(uid, out var power) && !power.Powered ? 0f : emitter.HealPerSecond;
        return own + WolfgateHullCooling(uid);
    }

    /// <summary>
    /// Heat per second a ship sheds now (<see cref="WGShipShieldEffects.CoolingRate"/>): under the ship generator
    /// cooling it, that generator's rate with the hull's for the whole ship's capacity; with none, the hull's own and
    /// its sink's.
    /// </summary>
    private float WolfgateCoolingRate(EntityUid grid, WGShipHeatComponent hull)
    {
        if (hull.Cooler is { } cooler && _wgHeatSinks.TryGetValue(cooler, out var sink) && sink.Grid == grid &&
            TryComp<ShipShieldEmitterComponent>(cooler, out var emitter) && HasComp<WGShipShieldCostComponent>(cooler))
        {
            return WGShipShieldEffects.CoolingRate(WolfgateGeneratorCooling(cooler, emitter), WolfgateShipCapacity(grid, cooler, emitter),
                GetWolfgateRaisedCost(grid) != null, hull.Venting, IsWolfgateOverloaded(cooler));
        }
        return WGShipShieldEffects.CoolingRate(hull.BareDissipation, hull.BareCapacity, false, hull.Venting, false);
    }

    /// <summary>Most heat a hull holds under a generator: a ship generator's capacity on top of the hull's own, an outpost generator's alone.</summary>
    private float WolfgateShipCapacity(EntityUid grid, EntityUid uid, ShipShieldEmitterComponent emitter)
    {
        var capacity = WGShipShieldEffects.EffectiveCapacity(emitter.DamageLimit, emitter.MaxDraw, emitter.PowerModifier, emitter.DamageExp);
        if (HasComp<WGShipShieldCostComponent>(uid) && TryComp<WGShipHeatComponent>(grid, out var hull))
            capacity += WolfgateHullCapacity(grid, hull);
        return capacity;
    }

    /// <summary>
    /// Carries what changed a generator's damage since the last call to its hull, then sets the damage to the hull's
    /// heat in the generator's range. A generator taken off its hull leaves the heat behind.
    /// </summary>
    private void SyncWolfgateGeneratorHeat(EntityUid uid, ShipShieldEmitterComponent emitter)
    {
        var transform = Transform(uid);
        if (!transform.Anchored || transform.GridUid is not { } grid)
        {
            if (_wgHeatSinks.Remove(uid))
                emitter.Damage = 0f;
            return;
        }
        if (!TryComp<WGShipHeatComponent>(grid, out var hull))
        {
            if (TerminatingOrDeleted(grid))
                return;
            hull = AddComp<WGShipHeatComponent>(grid);
        }
        var damage = float.IsFinite(emitter.Damage) ? MathF.Max(0f, emitter.Damage) : 0f;
        var ship = HasComp<WGShipShieldCostComponent>(uid);
        var share = WolfgateGeneratorShare(uid, emitter);
        if (_wgHeatSinks.TryGetValue(uid, out var sink) && sink.Grid == grid)
        {
            var change = (damage - sink.Damage) / share;
            if (change > 0f)
            {
                hull.Heat += change;
                if (ship)
                    hull.Hard = MathF.Min(hull.Hard + change, hull.Heat);
                hull.LastHit = _wgShieldTiming.CurTime;
            }
            else if (change < 0f)
            {
                // Cooling is done on the hull, so the damage was set from outside, maybe on every generator here.
                hull.Heat = MathF.Min(hull.Heat, damage / share);
                hull.Hard = MathF.Min(hull.Hard, hull.Heat);
            }
        }
        else if (damage / share > hull.Heat)
        {
            // First seen on this hull: just built, loaded from a save, or carried over by a hull split. It brings its
            // heat without adding to what another generator already brought.
            if (ship)
                hull.Hard += damage / share - hull.Heat;
            hull.Heat = damage / share;
        }
        if (ship)
            CapWolfgateShipHeat(grid, hull, uid, emitter);
        emitter.Damage = hull.Heat * share;
        _wgHeatSinks[uid] = (grid, emitter.Damage);
    }

    /// <summary>
    /// Stops a ship's heat at its capacity, as Starsector stops flux. A raised field taken past it overloads, however
    /// big the hit; with the field already down the excess is simply lost.
    /// </summary>
    private void CapWolfgateShipHeat(EntityUid grid, WGShipHeatComponent hull, EntityUid uid, ShipShieldEmitterComponent emitter)
    {
        var capacity = WolfgateShipCapacity(grid, uid, emitter);
        if (capacity <= 0f || hull.Heat <= capacity)
            return;
        hull.Heat = capacity;
        hull.Hard = MathF.Min(hull.Hard, hull.Heat);
        if (emitter.Shield != null)
            emitter.OverloadAccumulator = MathF.Max(emitter.OverloadAccumulator, WolfgateOverloadWait(uid, emitter));
    }

    /// <summary>
    /// A generator's overload wait: a ship generator's grows with its ship's capacity
    /// (<see cref="WGShipShieldEffects.OverloadWait"/>), so a bigger ship is out longer; an outpost's is as configured.
    /// </summary>
    public float WolfgateOverloadWait(EntityUid uid, ShipShieldEmitterComponent emitter)
    {
        if (!HasComp<WGShipShieldCostComponent>(uid) || Transform(uid) is not { Anchored: true, GridUid: { } grid })
            return emitter.DamageOverloadTimePunishment;
        return WGShipShieldEffects.OverloadWait(emitter.DamageOverloadTimePunishment, WolfgateShipCapacity(grid, uid, emitter));
    }

    /// <summary>
    /// Whether this generator is the one cooling its hull. Only one does, so a second generator is no extra heat sink:
    /// the one with the field up, and after the field drops the same one for as long as it stays installed.
    /// </summary>
    private bool IsWolfgateHullCooler(EntityUid uid, EntityUid grid, WGShipHeatComponent hull)
    {
        if (TryComp<ShipShieldedComponent>(grid, out var shielded) && shielded.Source is { } source && IsWolfgateShieldLive(shielded.Shield))
            hull.Cooler = source;
        else if (hull.Cooler is not { } cooler || Deleted(cooler) || !_wgHeatSinks.TryGetValue(cooler, out var sink) || sink.Grid != grid)
            hull.Cooler = uid;
        return hull.Cooler == uid;
    }

    /// <summary>
    /// Brings a hull's heat up to date and returns the generator that sets its capacity: the one with the field up,
    /// else the largest installed.
    /// </summary>
    private EntityUid? SyncWolfgateShipHeat(EntityUid grid)
    {
        if (TryComp<ShipShieldedComponent>(grid, out var shielded) && shielded.Source is { } source &&
            TryComp<ShipShieldEmitterComponent>(source, out var active))
        {
            SyncWolfgateGeneratorHeat(source, active);
            return source;
        }
        EntityUid? largest = null;
        var most = -1f;
        if (WolfgateHullGenerators(grid) is not { } generators)
            return null;
        foreach (var uid in generators)
        {
            if (!TryComp<ShipShieldEmitterComponent>(uid, out var emitter) || Transform(uid) is not { Anchored: true } transform ||
                transform.GridUid != grid)
                continue;
            SyncWolfgateGeneratorHeat(uid, emitter);
            var capacity = WGShipShieldEffects.EffectiveCapacity(emitter.DamageLimit, emitter.MaxDraw, emitter.PowerModifier, emitter.DamageExp);
            if (capacity <= most)
                continue;
            largest = uid;
            most = capacity;
        }
        return largest;
    }

    /// <summary>A ship's heat and the most it can hold: the hull's own capacity, plus its generator's on a ship or its built-in sink's on a bare hull.</summary>
    public (float Heat, float Capacity) GetWolfgateShipHeat(EntityUid grid)
    {
        var generator = SyncWolfgateShipHeat(grid);
        var hull = CompOrNull<WGShipHeatComponent>(grid);
        if (generator is not { } uid)
        {
            // A bare hull's capacity comes from its size, so it is measured the first time anything asks.
            if (hull == null && !TerminatingOrDeleted(grid) && HasComp<MapGridComponent>(grid))
                hull = AddComp<WGShipHeatComponent>(grid);
            // A hull that lost tiles holds less; what no longer fits is lost.
            if (hull != null && hull.Heat > WolfgateHullCapacity(grid, hull) + hull.SinkCapacity)
            {
                hull.Heat = hull.BareCapacity;
                hull.Hard = MathF.Min(hull.Hard, hull.Heat);
            }
            return (hull?.Heat ?? 0f, hull?.BareCapacity ?? WGShipHeatComponent.HullCapacity);
        }
        return (hull?.Heat ?? 0f, WolfgateShipCapacity(grid, uid, Comp<ShipShieldEmitterComponent>(uid)));
    }

    /// <summary>The part of a ship's heat that came through its shield and stays while the field is up.</summary>
    public float GetWolfgateShipHardHeat(EntityUid grid)
    {
        SyncWolfgateShipHeat(grid);
        return CompOrNull<WGShipHeatComponent>(grid)?.Hard ?? 0f;
    }

    /// <summary>
    /// Heats a ship's hull. With <paramref name="limit"/> below 1 the heat stops at that share of capacity, so it can't
    /// collapse the field; a hull with no generator never goes past its capacity.
    /// </summary>
    public void AddWolfgateShipHeat(EntityUid grid, float amount, float limit = float.PositiveInfinity, bool hard = false)
    {
        if (!float.IsFinite(amount) || amount <= 0f || TerminatingOrDeleted(grid))
            return;
        var generator = SyncWolfgateShipHeat(grid);
        var hull = EnsureComp<WGShipHeatComponent>(grid);
        var most = float.PositiveInfinity;
        if (generator is not { } uid)
            most = hull.BareCapacity * MathF.Min(limit, 1f);
        else if (float.IsFinite(limit) && TryComp<ShipShieldEmitterComponent>(uid, out var emitter))
            most = limit * WolfgateShipCapacity(grid, uid, emitter);
        var heated = MathF.Max(hull.Heat, MathF.Min(hull.Heat + amount, most));
        if (hard)
            hull.Hard = MathF.Min(hull.Hard + heated - hull.Heat, heated);
        hull.Heat = heated;
        // The generators read the new heat at once, so the field's rules and the panels see it this tick.
        SyncWolfgateShipHeat(grid);
    }

    /// <summary>
    /// Does one generator tick of cooling on the hull and returns what the upstream rule may still take from the
    /// generator itself: nothing while it reads a hull. An outpost generator cools its hull by the upstream amount.
    /// </summary>
    private float CoolWolfgateHull(EntityUid uid, ShipShieldEmitterComponent emitter, float healed)
    {
        SyncWolfgateGeneratorHeat(uid, emitter);
        if (!_wgHeatSinks.TryGetValue(uid, out var sink) || !TryComp<WGShipHeatComponent>(sink.Grid, out var hull))
            return healed;
        if (IsWolfgateHullCooler(uid, sink.Grid, hull))
        {
            if (HasComp<WGShipShieldCostComponent>(uid))
                CoolWolfgateShip(sink.Grid, hull, EmitterUpdateRate);
            else
            {
                // An outpost keeps the upstream amount: no hard heat, no vent, no ship multipliers.
                hull.Heat = MathF.Max(0f, hull.Heat - (float.IsFinite(healed) ? MathF.Max(0f, healed) : 0f));
                hull.Hard = Math.Clamp(hull.Hard, 0f, hull.Heat);
            }
        }
        emitter.Damage = hull.Heat * WolfgateGeneratorShare(uid, emitter);
        _wgHeatSinks[uid] = (sink.Grid, emitter.Damage);
        return 0f;
    }

    /// <summary>
    /// Cools a ship for <paramref name="seconds"/> at its rate for its state (<see cref="WolfgateCoolingRate"/>).
    /// Under a raised field that was hit lately only soft heat goes; a vent runs until no heat is left and
    /// <see cref="WGShipShieldEffects.MinVentSeconds"/> have passed.
    /// </summary>
    private void CoolWolfgateShip(EntityUid grid, WGShipHeatComponent hull, float seconds)
    {
        var raised = GetWolfgateRaisedCost(grid);
        var amount = WolfgateCoolingRate(grid, hull) * seconds;
        // How long hard heat stays is the raised generator's own number.
        var most = raised is { } cost && _wgShieldTiming.CurTime - hull.LastHit < TimeSpan.FromSeconds(cost.HardHeatHold)
            ? hull.Heat - hull.Hard : hull.Heat;
        if (float.IsFinite(amount) && amount > 0f && most > 0f)
            hull.Heat = MathF.Max(0f, hull.Heat - MathF.Min(amount, most));
        hull.Hard = Math.Clamp(hull.Hard, 0f, hull.Heat);
        if (hull.Venting)
            hull.VentHold -= seconds;
        if (hull.Venting && hull.Heat <= 0f && hull.VentHold <= 0f)
            StopWolfgateVent(grid, hull);
    }

    /// <summary>Whether a hull's guns heat it and hold when it is hot: every hull but one an outpost generator rules.</summary>
    private bool HasWolfgateShipHeatRules(EntityUid grid)
    {
        return SyncWolfgateShipHeat(grid) is not { } generator || HasComp<WGShipShieldCostComponent>(generator);
    }

    /// <summary>Whether a ship is too hot to fire, with or without a shield. It holds from the hold mark down to the resume mark.</summary>
    public bool IsWolfgateShipHeatHeld(EntityUid grid)
    {
        // A hull nothing has heated yet has no heat to hold for.
        if (!HasComp<WGShipHeatComponent>(grid) || !HasWolfgateShipHeatRules(grid))
        {
            _wgHeatHeld.Remove(grid);
            return false;
        }
        var (heat, capacity) = GetWolfgateShipHeat(grid);
        if (capacity <= 0f || heat < capacity * WGShipShieldEffects.GunResumeHeat)
            _wgHeatHeld.Remove(grid);
        else if (heat >= capacity * WGShipShieldEffects.GunHoldHeat)
            _wgHeatHeld.Add(grid);
        return _wgHeatHeld.Contains(grid);
    }

    /// <summary>A ship gun heats its hull by a share of each shot's damage, whether or not the ship has a shield.</summary>
    private void OnWolfgateShipGunShot(Entity<FireControllableComponent> gun, ref GunShotEvent args)
    {
        var transform = Transform(gun);
        if (!transform.Anchored || transform.GridUid is not { } grid || TerminatingOrDeleted(grid) || !HasWolfgateShipHeatRules(grid))
            return;
        var hull = EnsureComp<WGShipHeatComponent>(grid);
        if (hull.FireHeat <= 0f)
            return;
        var own = CompOrNull<WGShipGunHeatComponent>(gun);
        var share = own?.DamageShare ?? hull.FireHeat;
        var heat = 0f;
        foreach (var (uid, shootable) in args.Ammo)
        {
            if (!IsWolfgateMissileShot(uid, shootable))
                heat += own?.HeatPerShot ?? WolfgateShotDamage(gun, uid, shootable) * share;
        }
        // It stops at the hold mark, so a ship's own fire never collapses its field.
        AddWolfgateShipHeat(grid, MathF.Max(0f, heat), WGShipShieldEffects.GunHoldHeat);
        // Latch the hold now; cooling before the next shot would otherwise drop it under the mark unlatched.
        IsWolfgateShipHeatHeld(grid);
    }

    /// <summary>What one shot would cost a shield that absorbed it.</summary>
    private float WolfgateShotDamage(EntityUid gun, EntityUid? uid, IShootable shootable)
    {
        if (shootable is CartridgeAmmoComponent cartridge)
            return WolfgatePrototypeShotDamage(cartridge.Prototype);
        // A beam's entity is deleted before the shot is reported, so its damage is read from the gun's battery.
        if (shootable is HitscanAmmoComponent)
        {
            return TryComp<HitscanBatteryAmmoProviderComponent>(gun, out var battery)
                ? WolfgatePrototypeShotDamage(battery.HitscanEntityProto) * _wgHitscanDamage.UniversalHitscanDamageModifier : 0f;
        }
        return uid is { } shot && !Deleted(shot) && MetaData(shot).EntityPrototype is { } prototype
            ? WolfgatePrototypeShotDamage(prototype.ID) : 0f;
    }

    /// <summary>Whether a shot is a missile or rocket, which heats nothing when fired.</summary>
    private bool IsWolfgateMissileShot(EntityUid? uid, IShootable shootable)
    {
        var id = shootable is CartridgeAmmoComponent cartridge ? cartridge.Prototype.Id
            : uid is { } shot && !Deleted(shot) ? MetaData(shot).EntityPrototype?.ID : null;
        if (id == null)
            return false;
        if (!_wgMissiles.TryGetValue(id, out var missile))
        {
            missile = _prototypeManager.TryIndex<EntityPrototype>(id, out var prototype) && prototype.TryGetComponent<WGShipMissileComponent>(out _, _wgHeatFactory);
            _wgMissiles[id] = missile;
        }
        return missile;
    }

    /// <summary>Projectile, beam and peak blast damage of one shot of a prototype, times its pellets.</summary>
    private float WolfgatePrototypeShotDamage(string id)
    {
        if (_wgShotDamage.TryGetValue(id, out var cached))
            return cached;
        var damage = 0f;
        if (_prototypeManager.TryIndex<EntityPrototype>(id, out var prototype))
        {
            if (prototype.TryGetComponent<ProjectileComponent>(out var projectile, _wgHeatFactory))
                damage += (float) projectile.Damage.GetTotal();
            if (prototype.TryGetComponent<HitscanBasicDamageComponent>(out var beam, _wgHeatFactory))
                damage += (float) beam.Damage.GetTotal();
            if (prototype.TryGetComponent<ExplosiveComponent>(out var explosive, _wgHeatFactory))
                damage += WolfgateBlastDamage(explosive);
            if (prototype.TryGetComponent<ProjectileSpreadComponent>(out var spread, _wgHeatFactory))
                damage *= Math.Max(1, spread.Count);
        }
        damage = float.IsFinite(damage) ? MathF.Max(0f, damage) : 0f;
        _wgShotDamage[id] = damage;
        return damage;
    }

    /// <summary>A blast's damage as the firing ship's heat counts it (<see cref="WGShipShieldEffects.BlastDamage"/>).</summary>
    public float WolfgateBlastDamage(ExplosiveComponent explosive)
    {
        return _prototypeManager.TryIndex(explosive.ExplosionType, out var type)
            ? WGShipShieldEffects.BlastDamage(explosive.TotalIntensity, explosive.MaxIntensity, (float) type.DamagePerIntensity.GetTotal())
            : 0f;
    }

    /// <summary>A blast's damage as a field that stops or slows it is charged (<see cref="WGShipShieldEffects.AbsorbedBlastDamage"/>).</summary>
    public float WolfgateAbsorbedBlastDamage(ExplosiveComponent explosive)
    {
        return _prototypeManager.TryIndex(explosive.ExplosionType, out var type)
            ? WGShipShieldEffects.AbsorbedBlastDamage(explosive.TotalIntensity, (float) type.DamagePerIntensity.GetTotal())
            : 0f;
    }

    /// <summary>Cools hulls with no generator at their own rate; a generator does the cooling for the hull it sits on.</summary>
    private void UpdateWolfgateHullHeat(float frameTime)
    {
        _wgHullHeatAccumulator += frameTime;
        if (_wgHullHeatAccumulator < 1f)
            return;
        var elapsed = _wgHullHeatAccumulator;
        _wgHullHeatAccumulator = 0f;
        _wgHeatSunkHulls.Clear();
        foreach (var (uid, sink) in _wgHeatSinks)
        {
            if (Deleted(uid))
                _wgHeatGone.Add(uid);
            else
                _wgHeatSunkHulls.Add(sink.Grid);
        }
        foreach (var uid in _wgHeatGone)
            _wgHeatSinks.Remove(uid);
        _wgHeatGone.Clear();
        var query = EntityQueryEnumerator<WGShipHeatComponent>();
        while (query.MoveNext(out var grid, out var hull))
        {
            if (!hull.Sized)
                SizeWolfgateHull(grid, hull);
            if ((hull.Heat > 0f || hull.Venting) && !_wgHeatSunkHulls.Contains(grid))
                CoolWolfgateShip(grid, hull, elapsed);
        }
        UpdateWolfgateNpcVents();
    }
}
