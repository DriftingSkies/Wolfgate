using Content.Server._WG.Dormancy;
using Content.Server._WG.NpcCrew.Components;
using Content.Server._WG.NpcCrew.Systems;
using Content.Server._WG.Vehicles.Core;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Pathfinding;
using Content.Server.NPC.Systems;
using Content.Shared._WG.NpcCrew;
using Content.Shared._WG.NpcVehicleCrew;
using Content.Shared._WG.Vehicles.Core;
using Content.Shared._WG.Vehicles.Hardpoint;
using Content.Shared._WG.Vehicles.Turret;
using Content.Shared._WG.Vehicles.Weapons;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.CombatMode;
using Content.Shared.Damage;
using Content.Shared.Examine;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Roles;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WG.NpcVehicleCrew;

/// <summary>
/// NPC-crewed ground vehicles: spawns a vehicle with its crew strapped in, then drives and fights it as them at 5 Hz
/// until the crew dies or bails out.
/// </summary>
public sealed partial class WGVehicleCrewSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private VehicleInteriorSystem _interior = default!;
    [Dependency] private VehicleWeaponsSystem _weapons = default!;
    [Dependency] private VehicleTurretSystem _turret = default!;
    [Dependency] private VehicleTopologySystem _topology = default!;
    [Dependency] private VehicleHornSystem _horn = default!;
    [Dependency] private WGVehicleEngineSystem _engine = default!;
    [Dependency] private WGCrewSystem _crew = default!;
    [Dependency] private NPCSystem _npc = default!;
    [Dependency] private NpcFactionSystem _factions = default!;
    [Dependency] private SharedBuckleSystem _buckle = default!;
    [Dependency] private SharedGunSystem _gun = default!;
    [Dependency] private SharedCombatModeSystem _combat = default!;
    [Dependency] private ExamineSystemShared _examine = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private PathfindingSystem _pathfinding = default!;
    [Dependency] private WGDormancySystem _dormancy = default!;

    private static readonly TimeSpan ThinkInterval = TimeSpan.FromSeconds(0.2);

    /// <summary>How often a parked crew with no player near looks again whether one has come.</summary>
    private static readonly TimeSpan DormantThinkInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ReseatDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan AttackerMemory = TimeSpan.FromSeconds(60);

    /// <summary>Above this frame fraction past the retreat threshold a retreating vehicle goes back to its orders.</summary>
    private const float RetreatRecoverMargin = 0.2f;

    private readonly List<EntityUid> _pendingSpawners = new();
    private readonly List<EntityUid> _members = new();
    private int _spawnCount;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WGVehicleCrewComponent, ComponentShutdown>(OnCrewShutdown);
        SubscribeLocalEvent<WGVehicleCrewComponent, DamageChangedEvent>(OnVehicleDamaged);
        SubscribeLocalEvent<WGVehicleCrewMemberComponent, DamageChangedEvent>(OnMemberDamaged);
        SubscribeLocalEvent<WGVehicleCrewMemberComponent, MobStateChangedEvent>(OnMemberMobState);
        SubscribeLocalEvent<WGVehicleCrewSpawnerComponent, MapInitEvent>(OnSpawnerMapInit);
        InitializePanel();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Markers spawn here rather than in their map init, so an interior map never loads inside another map's load.
        if (_pendingSpawners.Count > 0)
        {
            var markers = new List<EntityUid>(_pendingSpawners);
            _pendingSpawners.Clear();
            foreach (var marker in markers)
            {
                if (TerminatingOrDeleted(marker) || !TryComp<WGVehicleCrewSpawnerComponent>(marker, out var spawner))
                    continue;

                TrySpawn(spawner.Crew, Transform(marker).Coordinates, out _, spawner.Behaviour, spawner.Group);
                QueueDel(marker);
            }
        }

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WGVehicleCrewComponent>();
        while (query.MoveNext(out var uid, out var crew))
        {
            if (crew.Bailed || now < crew.NextThink)
                continue;

            crew.NextThink = now + ThinkInterval;
            if (!_prototypes.TryIndex(crew.Prototype, out var proto))
                continue;

            // On a planet with no player near, the crew stops where it is until one comes, instead of driving unseen.
            if (!_dormancy.IsActive(uid))
            {
                crew.NextThink = now + DormantThinkInterval;
                Park(uid, crew);
                continue;
            }

            Think((uid, crew), proto, now);
        }
    }

    /// <summary>Takes the crew's hands off the controls and forgets how long it has been stuck.</summary>
    private void Park(EntityUid uid, WGVehicleCrewComponent crew)
    {
        crew.StuckSince = null;
        if (!TryComp<WGVehicleAutopilotComponent>(uid, out var autopilot))
            return;

        autopilot.Throttle = 0f;
        autopilot.Steering = 0f;
        autopilot.Direction = Vector2i.Zero;
    }

    private void OnSpawnerMapInit(Entity<WGVehicleCrewSpawnerComponent> ent, ref MapInitEvent args)
    {
        _pendingSpawners.Add(ent);
    }

    private void OnCrewShutdown(Entity<WGVehicleCrewComponent> ent, ref ComponentShutdown args)
    {
        ent.Comp.PathCancel?.Cancel();
        ent.Comp.PathCancel = null;
        ent.Comp.NextPathCancel?.Cancel();
        ent.Comp.NextPathCancel = null;
    }

    /// <summary>
    /// Spawns a crewed vehicle: the vehicle, its interior, a crewman strapped into each seat, faction, fuel and a running
    /// engine. Null behaviour and group take the prototype's behaviour and a new group.
    /// </summary>
    public bool TrySpawn(ProtoId<WGVehicleCrewPrototype> protoId, EntityCoordinates coords, out EntityUid vehicle,
        WGVehicleBehaviour? behaviour = null, string? group = null)
    {
        vehicle = EntityUid.Invalid;
        if (!_prototypes.TryIndex(protoId, out var proto))
        {
            Log.Error($"Unknown vehicle crew {protoId}");
            return false;
        }

        vehicle = Spawn(proto.Vehicle, coords);
        if (TryCrew(vehicle, protoId, behaviour, group))
            return true;

        QueueDel(vehicle);
        vehicle = EntityUid.Invalid;
        return false;
    }

    /// <summary>
    /// Puts a crew into a vehicle that already exists, loading its interior first; the vehicle is left as it was when
    /// the crew cannot be seated. TrySpawn's second half, for vehicles placed some other way.
    /// </summary>
    public bool TryCrew(EntityUid vehicle, ProtoId<WGVehicleCrewPrototype> protoId, WGVehicleBehaviour? behaviour = null,
        string? group = null)
    {
        if (!_prototypes.TryIndex(protoId, out var proto))
        {
            Log.Error($"Unknown vehicle crew {protoId}");
            return false;
        }

        if (HasComp<WGVehicleCrewComponent>(vehicle))
        {
            Log.Error($"Vehicle crew {protoId}: {ToPrettyString(vehicle)} already has a crew");
            return false;
        }

        if (!_interior.TryLoadInterior(vehicle, out var interior))
        {
            Log.Error($"Vehicle crew {protoId}: {ToPrettyString(vehicle)} has no interior to seat a crew in");
            return false;
        }

        if (!TryFindSeats(interior, out var driverSeat, out var gunnerSeat, out var driverGuns, out var seats))
        {
            Log.Error($"Vehicle crew {protoId}: {ToPrettyString(vehicle)} has no driver seat");
            return false;
        }

        var crew = EnsureComp<WGVehicleCrewComponent>(vehicle);
        crew.Prototype = protoId;
        crew.Group = group ?? $"{proto.ID}-{++_spawnCount}";
        crew.Behaviour = behaviour ?? proto.Behaviour;
        crew.PreviousBehaviour = crew.Behaviour is WGVehicleBehaviour.Patrol ? WGVehicleBehaviour.Patrol : WGVehicleBehaviour.Hold;
        crew.Pursues = proto.Pursues ?? crew.Behaviour == WGVehicleBehaviour.Patrol;
        crew.Home = _transform.GetMapCoordinates(vehicle);

        WGCrewProfilePrototype? profile = null;
        if (proto.Profile is { } profileId)
            _prototypes.TryIndex(profileId, out profile);

        crew.DriverSeat = driverSeat;
        crew.Driver = SpawnMember(vehicle, crew, proto, profile, proto.Driver, Transform(driverSeat).Coordinates,
            driverSeat, WGVehicleCrewRole.Driver);

        var troopers = new List<ProtoId<WGCrewRolePrototype>>(proto.Troopers);
        if (proto.Gunner is { } gunnerRole)
        {
            if (gunnerSeat is { } gunSeat)
            {
                crew.Gunner = SpawnMember(vehicle, crew, proto, profile, gunnerRole, Transform(gunSeat).Coordinates,
                    gunSeat, WGVehicleCrewRole.Gunner);
            }
            else if (driverGuns)
            {
                crew.Gunner = crew.Driver;
            }
            else
            {
                Log.Warning($"Vehicle crew {protoId}: {ToPrettyString(vehicle)} has no weapons seat; the gunner rides as a trooper");
                troopers.Insert(0, gunnerRole);
            }
        }

        var seatIndex = 0;
        foreach (var role in troopers)
        {
            EntityUid? seat = seatIndex < seats.Count ? seats[seatIndex++] : null;
            var at = seat is { } s ? Transform(s).Coordinates : interior.Entry;
            if (SpawnMember(vehicle, crew, proto, profile, role, at, seat, WGVehicleCrewRole.Trooper) is { } trooper)
                crew.Troopers.Add(trooper);
        }

        // The occupant faction system would copy this from the crew within a second; setting it now arms the gunner at once.
        EnsureComp<NpcFactionMemberComponent>(vehicle);
        _factions.ClearFactions(vehicle, false);
        _factions.AddFaction(vehicle, proto.Faction);

        if (TryComp<WGVehicleEngineComponent>(vehicle, out var engine))
        {
            engine.Fuel = engine.Capacity;
            Dirty(vehicle, engine);
            _engine.TryStartForNpc((vehicle, engine));
        }

        EnsureComp<WGVehicleAutopilotComponent>(vehicle).Active = true;
        return true;
    }

    /// <summary>The interior's driver seat, primary weapons seat and plain seats for troopers.</summary>
    private bool TryFindSeats(VehicleInteriorComponent interior, out EntityUid driverSeat, out EntityUid? gunnerSeat,
        out bool driverGuns, out List<EntityUid> seats)
    {
        EntityUid? driver = null;
        gunnerSeat = null;
        driverGuns = false;
        seats = new List<EntityUid>();

        // The seats are children of the interior's grid; a loose one on the map is found too.
        foreach (var parent in new[] { interior.Grid, interior.Map })
        {
            if (!parent.IsValid() || TerminatingOrDeleted(parent))
                continue;

            var children = Transform(parent).ChildEnumerator;
            while (children.MoveNext(out var seat))
            {
                if (!HasComp<StrapComponent>(seat))
                    continue;

                TryComp<VehicleWeaponsSeatComponent>(seat, out var weaponsSeat);
                if (HasComp<VehicleDriverSeatComponent>(seat))
                {
                    if (driver != null)
                        continue;

                    driver = seat;
                    driverGuns = weaponsSeat is { IsPrimaryOperatorSeat: true };
                    continue;
                }

                if (weaponsSeat != null)
                {
                    if (weaponsSeat.IsPrimaryOperatorSeat && gunnerSeat == null)
                        gunnerSeat = seat;

                    continue;
                }

                seats.Add(seat);
            }
        }

        driverSeat = driver ?? EntityUid.Invalid;
        return driver != null;
    }

    /// <summary>Spawns one crewman from the profile at his place, straps him in and puts his own brain to sleep.</summary>
    private EntityUid? SpawnMember(EntityUid vehicle, WGVehicleCrewComponent crew, WGVehicleCrewPrototype proto,
        WGCrewProfilePrototype? profile, ProtoId<WGCrewRolePrototype> role, EntityCoordinates at, EntityUid? seat,
        WGVehicleCrewRole kind)
    {
        ProtoId<StartingGearPrototype>? loadout = null;
        if (profile != null && profile.Loadouts.TryGetValue(role, out var pool) && pool.Count > 0)
            loadout = _random.Pick(pool);

        // A role the profile has no loadout for keeps its own mob and gear, as NpcCrew's setup does.
        var body = loadout != null && profile != null && profile.Bodies.Count > 0 ? _random.Pick(profile.Bodies) : (EntProtoId?) null;
        var name = profile != null ? FlavourName(profile) : null;
        if (_crew.SpawnCrewman(role, at, crew.Group, loadout, body, name) is not { } mob)
            return null;

        if (profile != null && profile.Components.Count > 0)
            EntityManager.AddComponents(mob, profile.Components);

        if (TryComp<WGCrewComponent>(mob, out var crewman))
        {
            crewman.Skill = proto.Skill;
            crewman.Pursues = false;
            // Mounted, the vehicle's brain acts for him; KeepActive would let the NPC pause check wake his own.
            crewman.KeepActive = false;
            if (profile != null && profile.Engagement.TryGetValue(role, out var manner))
                crewman.Engagement = manner;
            if (profile != null && profile.Cowards.TryGetValue(role, out var chance) && _random.Prob(Math.Clamp(chance, 0f, 1f)))
                crewman.Engagement = WGCrewEngagement.Never;
            _crew.Apply((mob, crewman));
        }

        EnsureComp<NpcFactionMemberComponent>(mob);
        _factions.ClearFactions(mob, false);
        _factions.AddFaction(mob, proto.Faction);

        var member = EnsureComp<WGVehicleCrewMemberComponent>(mob);
        member.Vehicle = vehicle;
        member.Seat = seat;
        member.Role = kind;

        _interior.TrackInteriorOccupant(vehicle, mob);
        if (seat is { } strap && !_buckle.TryBuckle(mob, null, strap, popup: false))
            Log.Warning($"Vehicle crewman {ToPrettyString(mob)} could not be strapped into {ToPrettyString(strap)}");

        if (TryComp<HTNComponent>(mob, out var htn))
        {
            member.SleepRange = htn.SleepPlayerCheckRangeOverride;
            htn.SleepPlayerCheckRangeOverride = -1f;
            _npc.SleepNPC(mob, htn);
        }

        return mob;
    }

    /// <summary>A name rolled from a profile's datasets, or null for a profile that leaves crews their bodies' names.</summary>
    private string? FlavourName(WGCrewProfilePrototype profile)
    {
        if (profile.FirstNames is not { } firstId || profile.LastNames is not { } lastId
            || !_prototypes.TryIndex(firstId, out var first) || !_prototypes.TryIndex(lastId, out var last)
            || first.Values.Count == 0 || last.Values.Count == 0)
            return null;

        return $"{_random.Pick(first.Values)} {_random.Pick(last.Values)}";
    }

    /// <summary>One brain tick: crew upkeep, bail and retreat checks, guard, gunnery, orders and driving.</summary>
    private void Think(Entity<WGVehicleCrewComponent> ent, WGVehicleCrewPrototype proto, TimeSpan now)
    {
        var (uid, crew) = ent;
        PruneMembers(crew, now);

        if (!AnyAlive(crew))
        {
            // A vehicle with no living crew is just a vehicle.
            crew.Bailed = true;
            crew.PathCancel?.Cancel();
            crew.PathCancel = null;
            RemComp<WGVehicleAutopilotComponent>(uid);
            return;
        }

        TryReseat(crew, now);
        if (ShouldBail(uid, crew, proto))
        {
            Bail(ent);
            return;
        }

        UpdateDamageState(uid, crew, proto);
        UpdateGuard(uid, crew, proto, now);
        if (now >= crew.NextGunnery)
        {
            crew.NextGunnery = now + GunneryInterval;
            UpdateTarget(uid, crew, proto, now);
        }

        UpdateBehaviour(uid, crew, proto, now);
        UpdateTrigger(uid, crew, proto, now);
        Drive(uid, crew, now);
    }

    /// <summary>Drops deleted crewmen, unstraps the dead and forgets old attackers.</summary>
    private void PruneMembers(WGVehicleCrewComponent crew, TimeSpan now)
    {
        if (crew.Driver is { } driver && TerminatingOrDeleted(driver))
            crew.Driver = null;
        if (crew.Gunner is { } gunner && TerminatingOrDeleted(gunner))
            crew.Gunner = null;
        crew.Troopers.RemoveAll(trooper => TerminatingOrDeleted(trooper));

        CollectMembers(crew);
        foreach (var mob in _members)
        {
            if (_mobState.IsDead(mob) && TryComp<BuckleComponent>(mob, out var buckle) && buckle.Buckled)
                _buckle.Unbuckle(mob, null);
        }

        var expired = new List<EntityUid>();
        foreach (var (attacker, at) in crew.Attackers)
        {
            if (now - at > AttackerMemory || TerminatingOrDeleted(attacker))
                expired.Add(attacker);
        }

        foreach (var attacker in expired)
            crew.Attackers.Remove(attacker);
    }

    /// <summary>Fills <see cref="_members"/> with the crew's driver, gunner and troopers, each once.</summary>
    private void CollectMembers(WGVehicleCrewComponent crew)
    {
        _members.Clear();
        if (crew.Driver is { } driver)
            _members.Add(driver);
        if (crew.Gunner is { } gunner && !_members.Contains(gunner))
            _members.Add(gunner);
        foreach (var trooper in crew.Troopers)
        {
            if (!_members.Contains(trooper))
                _members.Add(trooper);
        }
    }

    private bool AnyAlive(WGVehicleCrewComponent crew)
    {
        CollectMembers(crew);
        foreach (var mob in _members)
        {
            if (IsAlive(mob))
                return true;
        }

        return false;
    }

    private bool IsAlive(EntityUid? mob)
    {
        return mob is { } uid && !TerminatingOrDeleted(uid) && !_mobState.IsDead(uid);
    }

    private bool IsOwnCrew(WGVehicleCrewComponent crew, EntityUid uid)
    {
        return crew.Driver == uid || crew.Gunner == uid || crew.Troopers.Contains(uid);
    }

    /// <summary>The first living, conscious trooper, who can take a dead driver's place.</summary>
    private bool TryGetStandIn(WGVehicleCrewComponent crew, out EntityUid trooper)
    {
        foreach (var candidate in crew.Troopers)
        {
            if (IsAlive(candidate) && !_mobState.IsIncapacitated(candidate))
            {
                trooper = candidate;
                return true;
            }
        }

        trooper = EntityUid.Invalid;
        return false;
    }

    /// <summary>Puts a trooper in the dead driver's seat once the delay is up: unstrapped, moved to the seat, strapped in.</summary>
    private void TryReseat(WGVehicleCrewComponent crew, TimeSpan now)
    {
        // A driver deleted outright raises no death, so the wait starts here as well.
        if (crew.ReseatAt == null && !IsAlive(crew.Driver) && TryGetStandIn(crew, out _))
            crew.ReseatAt = now + ReseatDelay;

        if (crew.ReseatAt is not { } at || now < at)
            return;

        crew.ReseatAt = null;
        if (crew.DriverSeat is not { } seat || TerminatingOrDeleted(seat) || !TryGetStandIn(crew, out var trooper))
            return;

        if (TryComp<BuckleComponent>(trooper, out var buckle) && buckle.Buckled)
            _buckle.Unbuckle(trooper, null);

        _transform.SetCoordinates(trooper, Transform(seat).Coordinates);
        if (!_buckle.TryBuckle(trooper, null, seat, popup: false))
            return;

        crew.Troopers.Remove(trooper);
        crew.Driver = trooper;
        if (TryComp<WGVehicleCrewMemberComponent>(trooper, out var member))
        {
            member.Role = WGVehicleCrewRole.Driver;
            member.Seat = seat;
        }
    }

    /// <summary>Whether the crew must abandon the vehicle: a broken frame, no fuel, or a dead driver nobody can replace.</summary>
    private bool ShouldBail(EntityUid uid, WGVehicleCrewComponent crew, WGVehicleCrewPrototype proto)
    {
        if (TryComp<HardpointIntegrityComponent>(uid, out var frame) &&
            (frame.Integrity <= 0f || frame.MaxIntegrity > 0f && frame.Integrity / frame.MaxIntegrity < proto.BailIntegrity))
        {
            return true;
        }

        if (TryComp<WGVehicleEngineComponent>(uid, out var engine) && engine.Fuel <= 0f)
            return true;

        return !IsAlive(crew.Driver) && crew.ReseatAt == null && !TryGetStandIn(crew, out _);
    }

    /// <summary>The frame's integrity as a fraction of its maximum; 1 for a vehicle without one.</summary>
    private float IntegrityFraction(EntityUid uid)
    {
        if (!TryComp<HardpointIntegrityComponent>(uid, out var frame) || frame.MaxIntegrity <= 0f)
            return 1f;

        return frame.Integrity / frame.MaxIntegrity;
    }

    /// <summary>Everyone alive gets out and fights on foot; the vehicle's brain, autopilot and engine go off.</summary>
    private void Bail(Entity<WGVehicleCrewComponent> ent)
    {
        var (uid, crew) = ent;
        crew.Bailed = true;
        crew.Behaviour = WGVehicleBehaviour.Bail;
        crew.Goal = null;
        crew.Target = null;
        crew.Route.Clear();
        crew.PathCancel?.Cancel();
        crew.PathCancel = null;
        RemComp<WGVehicleAutopilotComponent>(uid);
        _engine.StopForNpc(uid);

        CollectMembers(crew);
        var members = new List<EntityUid>(_members);
        foreach (var mob in members)
        {
            if (TerminatingOrDeleted(mob) || !TryComp<WGVehicleCrewMemberComponent>(mob, out var member))
                continue;

            if (TryComp<BuckleComponent>(mob, out var buckle) && buckle.Buckled)
                _buckle.Unbuckle(mob, null);

            if (!_mobState.IsDead(mob) && !_interior.TryExitVehicle(uid, mob))
                Log.Warning($"Vehicle crewman {ToPrettyString(mob)} found no way out of {ToPrettyString(uid)}");

            Dismount(mob, member);
        }
    }

    /// <summary>Hands a crewman back to his own brain: guard where he stands, no pursuit, awake.</summary>
    private void Dismount(EntityUid mob, WGVehicleCrewMemberComponent member)
    {
        TryComp<HTNComponent>(mob, out var htn);
        if (htn != null)
            htn.SleepPlayerCheckRangeOverride = member.SleepRange;

        if (TryComp<WGCrewComponent>(mob, out var crewman))
        {
            crewman.KeepActive = true;
            crewman.Pursues = false;
            crewman.Duty = WGCrewDuties.Guard;
            crewman.Post = Transform(mob).Coordinates;
            _crew.Apply((mob, crewman));
        }

        if (htn != null && !_mobState.IsDead(mob))
            _npc.WakeNPC(mob, htn);

        RemComp<WGVehicleCrewMemberComponent>(mob);
    }

    private void OnVehicleDamaged(Entity<WGVehicleCrewComponent> ent, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased || args.Origin is not { } origin)
            return;

        NoteAttack(ent, origin);
    }

    private void OnMemberDamaged(Entity<WGVehicleCrewMemberComponent> ent, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased || args.Origin is not { } origin ||
            !TryComp<WGVehicleCrewComponent>(ent.Comp.Vehicle, out var crew))
        {
            return;
        }

        NoteAttack((ent.Comp.Vehicle, crew), origin);
    }

    private void OnMemberMobState(Entity<WGVehicleCrewMemberComponent> ent, ref MobStateChangedEvent args)
    {
        // The body is unstrapped on the next tick; a trooper takes the wheel after the delay.
        if (args.NewMobState != MobState.Dead ||
            ent.Comp.Role != WGVehicleCrewRole.Driver ||
            !TryComp<WGVehicleCrewComponent>(ent.Comp.Vehicle, out var crew) ||
            crew.Bailed ||
            crew.Driver != ent.Owner ||
            crew.ReseatAt != null ||
            !TryGetStandIn(crew, out _))
        {
            return;
        }

        crew.ReseatAt = _timing.CurTime + ReseatDelay;
    }

    /// <summary>Remembers who hurt the vehicle or a crewman; an unarmed vehicle runs from them.</summary>
    private void NoteAttack(Entity<WGVehicleCrewComponent> ent, EntityUid origin)
    {
        var crew = ent.Comp;
        if (crew.Bailed)
            return;

        var attacker = ResolveAttacker(origin);
        if (attacker == ent.Owner || IsOwnCrew(crew, attacker) || TerminatingOrDeleted(attacker))
            return;

        var now = _timing.CurTime;
        crew.Attackers[attacker] = now;
        crew.LastHit = now;

        if (crew.Gunner == null && crew.Behaviour is not (WGVehicleBehaviour.Flee or WGVehicleBehaviour.Retreat or WGVehicleBehaviour.Bail))
            ChangeBehaviour(crew, WGVehicleBehaviour.Flee);
    }

    /// <summary>The one to answer for a hit: the hand behind a gun, or the vehicle a gunner or mounted gun belongs to.</summary>
    private EntityUid ResolveAttacker(EntityUid origin)
    {
        var attacker = _crew.Wielder(origin);
        if (TryComp<VehicleInteriorOccupantComponent>(attacker, out var occupant))
        {
            var inside = occupant.Vehicle;
            if (inside.IsValid() && !TerminatingOrDeleted(inside))
                return inside;
        }

        if (!HasComp<Content.Shared.Mobs.Components.MobStateComponent>(attacker) &&
            _topology.TryGetVehicle(attacker, out var vehicle, includeSelf: false))
        {
            return vehicle;
        }

        return attacker;
    }

    /// <summary>Changes orders; a hunt, a flight or a retreat remembers what to go back to.</summary>
    private void ChangeBehaviour(WGVehicleCrewComponent crew, WGVehicleBehaviour behaviour)
    {
        if (crew.Behaviour == behaviour)
            return;

        if (IsTransient(behaviour) && !IsTransient(crew.Behaviour))
            crew.PreviousBehaviour = crew.Behaviour;

        crew.Behaviour = behaviour;
        crew.Goal = null;
        crew.RouteGoal = null;
        crew.Route.Clear();
        crew.StuckCount = 0;
    }

    private static bool IsTransient(WGVehicleBehaviour behaviour)
    {
        return behaviour is WGVehicleBehaviour.Hunt or WGVehicleBehaviour.Flee or WGVehicleBehaviour.Retreat;
    }

    /// <summary>Gives a live crew new orders.</summary>
    public void SetBehaviour(Entity<WGVehicleCrewComponent?> vehicle, WGVehicleBehaviour behaviour)
    {
        if (!Resolve(vehicle, ref vehicle.Comp, false) || vehicle.Comp.Bailed || behaviour == WGVehicleBehaviour.Bail)
            return;

        ChangeBehaviour(vehicle.Comp, behaviour);
    }

    /// <summary>Sends a crew patrolling through these points.</summary>
    public void SetWaypoints(Entity<WGVehicleCrewComponent?> vehicle, List<MapCoordinates> points)
    {
        if (!Resolve(vehicle, ref vehicle.Comp, false))
            return;

        vehicle.Comp.Waypoints = new List<MapCoordinates>(points);
        vehicle.Comp.WaypointIndex = 0;
        vehicle.Comp.PatrolFrom = _transform.GetMapCoordinates(vehicle);
        vehicle.Comp.PauseUntil = TimeSpan.Zero;
        SetBehaviour(vehicle, WGVehicleBehaviour.Patrol);
    }

    /// <summary>Moves the crew's home, where it holds, retreats to and patrols round.</summary>
    public void SetHome(Entity<WGVehicleCrewComponent?> vehicle, MapCoordinates home)
    {
        if (!Resolve(vehicle, ref vehicle.Comp, false))
            return;

        vehicle.Comp.Home = home;
        vehicle.Comp.Waypoints.Clear();
    }

    /// <summary>Sets the crew escorting a lead entity.</summary>
    public void SetLead(Entity<WGVehicleCrewComponent?> vehicle, EntityUid lead)
    {
        if (!Resolve(vehicle, ref vehicle.Comp, false))
            return;

        vehicle.Comp.Lead = lead;
        SetBehaviour(vehicle, WGVehicleBehaviour.Escort);
    }

    /// <summary>Every crewed vehicle, bailed or not.</summary>
    public List<Entity<WGVehicleCrewComponent>> GetCrews()
    {
        var result = new List<Entity<WGVehicleCrewComponent>>();
        var query = EntityQueryEnumerator<WGVehicleCrewComponent>();
        while (query.MoveNext(out var uid, out var crew))
            result.Add((uid, crew));

        return result;
    }

    /// <summary>How many of a crew are alive.</summary>
    public int CountAlive(WGVehicleCrewComponent crew)
    {
        CollectMembers(crew);
        var count = 0;
        foreach (var mob in _members)
        {
            if (IsAlive(mob))
                count++;
        }

        return count;
    }

    /// <summary>Deletes crewed vehicles and their crews, on foot or not, of one group or all. Returns the vehicles deleted.</summary>
    public int Clear(string? group = null)
    {
        var groups = new HashSet<string>();
        var count = 0;
        foreach (var (uid, crew) in GetCrews())
        {
            if (group != null && crew.Group != group)
                continue;

            CollectMembers(crew);
            foreach (var mob in _members)
                QueueDel(mob);

            groups.Add(crew.Group);
            QueueDel(uid);
            count++;
        }

        foreach (var label in groups)
            _crew.ClearGroup(label);

        return count;
    }

    private static float Distance(MapCoordinates a, MapCoordinates b)
    {
        return a.MapId != b.MapId ? float.MaxValue : (a.Position - b.Position).Length();
    }
}
