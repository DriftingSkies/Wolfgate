using System.Linq;
using Content.Server._WF.Planets.Flight;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._WF.Lattice;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.Ghost;
using Content.Shared.Maps;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Content.Shared.Shuttles.Components;
using Robust.Server.Player;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Lattice;

/// <summary>
/// The Coda is not a place to linger. A crewed ship out there is found by Remade hunters the longer it stays, has its
/// structure eaten unless a stabiliser runs, and has its thrusters, gyroscopes and power give out now and then.
/// </summary>
public sealed partial class WFCodaHazardSystem : EntitySystem
{
    [Dependency] private ApcSystem _apc = default!;
    [Dependency] private DamageableSystem _damage = default!;
    [Dependency] private SharedDoorSystem _doors = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ThrusterSystem _thrusters = default!;
    [Dependency] private TurfSystem _turf = default!;
    [Dependency] private WFLatticeSystem _lattice = default!;
    [Dependency] private WFCodaFleshSystem _flesh = default!;

    private static readonly ProtoId<DamageTypePrototype> Structural = "Structural";
    private static readonly SoundSpecifier BoardSound = new SoundCollectionSpecifier("WFDreadMenace");
    private static readonly SoundSpecifier GroanSound = new SoundCollectionSpecifier("MetalBreak");

    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(5);
    private const float HunterRollSeconds = 60f;
    private const float RotSeconds = 20f;

    /// <summary>Most doors one fault slams, opens or bolts.</summary>
    private const int DoorFaults = 5;

    /// <summary>Hunters keep this far from the crew when they board, in metres.</summary>
    private const float BoardDistance = 6f;

    private readonly Dictionary<EntityUid, Ship> _ships = new();
    private readonly List<(EntityUid Uid, TimeSpan Until, Failure Kind)> _failed = new();
    private readonly Dictionary<EntityUid, List<EntityUid>> _crew = new();
    private TimeSpan _next;

    private enum Failure : byte
    {
        Thruster,
        Breaker,
        Bolt,
    }

    /// <summary>One crewed ship's time in the Coda.</summary>
    private sealed class Ship
    {
        public float Exposure;
        public float HunterClock;
        public float RotClock;
        public float FailureClock;
        public TimeSpan HuntersAfter;
        public TimeSpan LeviathanAfter;
        public readonly List<EntityUid> Hunters = new();
    }

    public override void Initialize()
    {
        base.Initialize();
        UpdatesBefore.Add(typeof(PowerNetSystem));
        SubscribeLocalEvent<WFCodaFlutterComponent, ComponentShutdown>(OnFlutterShutdown);
        SubscribeLocalEvent<Content.Shared.GameTicking.RoundRestartCleanupEvent>(_ =>
        {
            _ships.Clear();
            _failed.Clear();
        });
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        RestoreFailures(now);
        UpdateLeviathans(now);
        Flutter(now);
        if (now < _next)
            return;

        _next = now + Tick;
        var seconds = (float) Tick.TotalSeconds;
        GatherCrew();

        foreach (var grid in _ships.Keys.ToList())
        {
            if (!_crew.ContainsKey(grid))
                _ships.Remove(grid);
        }

        foreach (var (grid, crew) in _crew)
        {
            var point = _transform.GetMapCoordinates(grid);
            if (_lattice.BandOf(point, out var profile) == null || profile == null
                || !TryComp<MapGridComponent>(grid, out var gridComp))
                continue;

            var (band, depth) = _lattice.Locate(Transform(grid).MapUid!.Value, point.Position);
            if (band < profile.HazardBand)
            {
                _ships.Remove(grid);
                continue;
            }

            if (!_ships.TryGetValue(grid, out var ship))
            {
                ship = new Ship { HuntersAfter = now };
                _ships[grid] = ship;
            }

            ship.Exposure += seconds;
            var sheltered = TryComp<WFLatticeShelterComponent>(grid, out var shelter) && shelter.StopsTaking;

            ship.HunterClock += seconds;
            if (ship.HunterClock >= HunterRollSeconds)
            {
                ship.HunterClock = 0f;
                RollHunters((grid, gridComp), ship, crew, depth, profile, now);
            }

            ship.RotClock += seconds;
            if (ship.RotClock >= RotSeconds)
            {
                ship.RotClock = 0f;
                if (!sheltered)
                    Rot((grid, gridComp), crew, depth, profile);
            }

            // Flesh takes root on an unsheltered ship, and now and then something vast passes by.
            if (!sheltered)
                _flesh.Roll((grid, gridComp), crew, depth, profile, seconds);

            RollLeviathan((grid, gridComp), ship, crew, depth, profile, seconds, now);

            // Faults come quicker the longer the ship stays and the deeper it is.
            ship.FailureClock += seconds * (1f + depth + Math.Min(ship.Exposure / 300f, 1f));
            if (ship.FailureClock >= profile.FailureInterval)
            {
                ship.FailureClock = 0f;
                RollFailure(grid, crew, profile, now);
            }
        }
    }

    /// <summary>Puts a hunter pack aboard a ship now, away from whoever is on it. Returns how many boarded.</summary>
    public int Board(Entity<MapGridComponent> grid, int count, WFLatticeProfilePrototype profile)
    {
        var crew = _crew.TryGetValue(grid, out var list) ? list : new List<EntityUid>();
        if (profile.HunterMobs.Count == 0 || !TryFindBoarding(grid, crew, out var at))
            return 0;

        for (var i = 0; i < count; i++)
        {
            Spawn(_random.Pick(profile.HunterMobs), at);
        }

        return count;
    }

    /// <summary>One rot tick on a ship, at the given depth into the band.</summary>
    public void RotNow(Entity<MapGridComponent> grid, float depth, WFLatticeProfilePrototype profile)
    {
        Rot(grid, new List<EntityUid>(), depth, profile);
    }

    /// <summary>Makes one kind of system give out on a ship: 0 thrusters, 1 gyroscopes, 2 power, 3 flickering power, 4 doors.</summary>
    public bool FailNow(EntityUid grid, int kind, float seconds)
    {
        return Fail(grid, kind, _timing.CurTime + TimeSpan.FromSeconds(seconds)) != null;
    }

    /// <summary>Whether a ship is counted as crewed and in the Coda.</summary>
    public bool IsExposed(EntityUid grid)
    {
        return _ships.ContainsKey(grid);
    }

    /// <summary>Makes one kind of system give out; the popup to show, or null if nothing aboard could fail.</summary>
    private string? Fail(EntityUid grid, int kind, TimeSpan until)
    {
        return kind switch
        {
            0 => FailThrusters(grid, ThrusterType.Linear, until) ? "wf-coda-fail-thrusters" : null,
            1 => FailThrusters(grid, ThrusterType.Angular, until) ? "wf-coda-fail-gyros" : null,
            2 => FailPower(grid, until) ? "wf-coda-fail-power" : null,
            3 => FlutterPower(grid, until) ? "wf-coda-fail-flutter" : null,
            _ => FailDoors(grid, until) ? "wf-coda-fail-doors" : null,
        };
    }

    /// <summary>Which ships have living players aboard, and who.</summary>
    private void GatherCrew()
    {
        _crew.Clear();
        foreach (var session in _players.Sessions)
        {
            if (session.AttachedEntity is not { } mob
                || HasComp<GhostComponent>(mob)
                || HasComp<WFChoirMemberComponent>(mob)
                || _mobState.IsDead(mob)
                || Transform(mob).GridUid is not { } grid
                || HasComp<MapComponent>(grid)
                || !HasComp<ShuttleComponent>(grid))
                continue;

            if (!_crew.TryGetValue(grid, out var list))
            {
                list = new List<EntityUid>();
                _crew[grid] = list;
            }

            list.Add(mob);
        }
    }

    /// <summary>The longer a ship stays, the likelier a pack of Remade comes aboard somewhere away from the crew.</summary>
    private void RollHunters(Entity<MapGridComponent> grid, Ship ship, List<EntityUid> crew, float depth,
        WFLatticeProfilePrototype profile, TimeSpan now)
    {
        ship.Hunters.RemoveAll(uid => TerminatingOrDeleted(uid) || _mobState.IsDead(uid));
        if (profile.HunterMobs.Count == 0 || now < ship.HuntersAfter || ship.Hunters.Count >= profile.HunterCap)
            return;

        var chance = Math.Clamp((ship.Exposure - profile.HunterGrace) / profile.HunterRamp, 0f, 1f) * profile.HunterMaxChance;
        if (!_random.Prob(chance))
            return;

        var size = (int) MathF.Round(MathHelper.Lerp(profile.HunterPack.X, profile.HunterPack.Y, depth));
        size = Math.Min(size, profile.HunterCap - ship.Hunters.Count);
        if (size <= 0 || !TryFindBoarding(grid, crew, out var at))
            return;

        for (var i = 0; i < size; i++)
        {
            ship.Hunters.Add(Spawn(_random.Pick(profile.HunterMobs), at));
        }

        ship.HuntersAfter = now + TimeSpan.FromSeconds(profile.HunterCooldown);
        _audio.PlayPvs(BoardSound, at);
        foreach (var mob in crew)
        {
            _popup.PopupEntity(Loc.GetString("wf-coda-hunters"), mob, mob, PopupType.LargeCaution);
        }
    }

    /// <summary>An open floor tile well away from the crew, for something to take root or come aboard.</summary>
    public bool TryFindQuietFloor(Entity<MapGridComponent> grid, List<EntityUid> crew, out EntityCoordinates at)
    {
        return TryFindBoarding(grid, crew, out at);
    }

    /// <summary>An open floor tile well away from the crew.</summary>
    private bool TryFindBoarding(Entity<MapGridComponent> grid, List<EntityUid> crew, out EntityCoordinates at)
    {
        at = default;
        var tiles = _maps.GetAllTiles(grid.Owner, grid.Comp).ToList();
        for (var attempt = 0; attempt < 40 && tiles.Count > 0; attempt++)
        {
            var tile = _random.Pick(tiles);
            if (tile.Tile.IsEmpty || _turf.IsTileBlocked(tile, CollisionGroup.Impassable))
                continue;

            var coords = _maps.GridTileToLocal(grid.Owner, grid.Comp, tile.GridIndices);
            if (crew.Any(mob => _transform.InRange(Transform(mob).Coordinates, coords, BoardDistance)))
                continue;

            at = coords;
            return true;
        }

        return false;
    }

    /// <summary>The Unmooring eats a few of the ship's structures: walls, machines, anything bolted down.</summary>
    private void Rot(Entity<MapGridComponent> grid, List<EntityUid> crew, float depth, WFLatticeProfilePrototype profile)
    {
        var tiles = _maps.GetAllTiles(grid.Owner, grid.Comp).ToList();
        if (tiles.Count == 0)
            return;

        var targets = (int) MathF.Round(MathHelper.Lerp(profile.RotTargets.X, profile.RotTargets.Y, depth));
        var damage = new DamageSpecifier(_proto.Index(Structural), profile.RotDamage);
        var hit = 0;
        for (var attempt = 0; attempt < targets * 6 && hit < targets; attempt++)
        {
            var tile = _random.Pick(tiles);
            var anchored = _maps.GetAnchoredEntitiesEnumerator(grid.Owner, grid.Comp, tile.GridIndices);
            while (anchored.MoveNext(out var uid))
            {
                if (HasComp<MobStateComponent>(uid) || HasComp<WFLatticeAnchorComponent>(uid) || !HasComp<DamageableComponent>(uid))
                    continue;

                _damage.TryChangeDamage(uid.Value, damage, ignoreResistances: true);
                hit++;
                break;
            }
        }

        if (hit > 0 && crew.Count > 0 && _random.Prob(0.25f))
        {
            var mob = _random.Pick(crew);
            _audio.PlayPvs(GroanSound, mob, AudioParams.Default.WithVolume(-6f));
            _popup.PopupEntity(Loc.GetString("wf-coda-rot"), mob, mob, PopupType.MediumCaution);
        }
    }

    /// <summary>Now and then something aboard gives out: thrusters, gyroscopes or the power.</summary>
    private void RollFailure(EntityUid grid, List<EntityUid> crew, WFLatticeProfilePrototype profile, TimeSpan now)
    {
        if (!_random.Prob(profile.FailureChance))
            return;

        var until = now + TimeSpan.FromSeconds(_random.NextFloat(profile.FailureSeconds.X, profile.FailureSeconds.Y));
        var message = Fail(grid, _random.Pick(FailureKinds), until);

        if (message == null)
            return;

        foreach (var mob in crew)
        {
            _popup.PopupEntity(Loc.GetString(message), mob, mob, PopupType.MediumCaution);
        }
    }

    /// <summary>Cuts out about half the ship's thrusters of one type.</summary>
    private bool FailThrusters(EntityUid grid, ThrusterType type, TimeSpan until)
    {
        var any = false;
        var query = EntityQueryEnumerator<ThrusterComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var thruster, out var xform))
        {
            if (xform.GridUid != grid || thruster.Type != type || !thruster.IsOn || !_random.Prob(0.5f))
                continue;

            _thrusters.DisableThruster(uid, thruster, xform);
            _failed.Add((uid, until, Failure.Thruster));
            any = true;
        }

        return any;
    }

    /// <summary>Trips the ship's APC breakers.</summary>
    private bool FailPower(EntityUid grid, TimeSpan until)
    {
        var any = false;
        var query = EntityQueryEnumerator<ApcComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var apc, out var xform))
        {
            if (xform.GridUid != grid || !apc.MainBreakerEnabled)
                continue;

            _apc.ApcToggleBreaker(uid, apc);
            _failed.Add((uid, until, Failure.Breaker));
            any = true;
        }

        return any;
    }

    /// <summary>Flickering power and doors are the commonest faults; dead breakers and engines the rarest.</summary>
    private static readonly int[] FailureKinds = { 0, 1, 2, 3, 3, 3, 4, 4, 4 };

    /// <summary>Makes every APC aboard cut in and out until the time is up.</summary>
    private bool FlutterPower(EntityUid grid, TimeSpan until)
    {
        var any = false;
        var query = EntityQueryEnumerator<ApcComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid != grid || HasComp<WFCrashApcFaultComponent>(uid))
                continue;

            // A flicker already running keeps its later end and its rhythm.
            var running = HasComp<WFCodaFlutterComponent>(uid);
            var flutter = EnsureComp<WFCodaFlutterComponent>(uid);
            if (!running || until > flutter.Until)
                flutter.Until = until;
            if (!running)
                flutter.Next = _timing.CurTime;
            any = true;
        }

        return any;
    }

    /// <summary>Cuts fluttering APCs' output in and out, a moment to a couple of seconds at a time.</summary>
    private void Flutter(TimeSpan now)
    {
        var query = EntityQueryEnumerator<WFCodaFlutterComponent, ApcComponent, PowerNetworkBatteryComponent>();
        while (query.MoveNext(out var uid, out var flutter, out var apc, out var battery))
        {
            if (now >= flutter.Until)
            {
                RemCompDeferred<WFCodaFlutterComponent>(uid);
                continue;
            }

            if (now >= flutter.Next)
            {
                flutter.Out = !flutter.Out;
                flutter.Next = now + TimeSpan.FromSeconds(flutter.Out ? _random.NextFloat(0.15f, 1.2f) : _random.NextFloat(0.3f, 2.5f));
            }

            battery.CanDischarge = apc.MainBreakerEnabled && !flutter.Out;
        }
    }

    private void OnFlutterShutdown(Entity<WFCodaFlutterComponent> ent, ref ComponentShutdown args)
    {
        if (TryComp<ApcComponent>(ent, out var apc) && TryComp<PowerNetworkBatteryComponent>(ent, out var battery))
            battery.CanDischarge = apc.MainBreakerEnabled;
    }

    /// <summary>A few doors aboard slam, open or bolt themselves; the bolts lift again when the time is up.</summary>
    private bool FailDoors(EntityUid grid, TimeSpan until)
    {
        var doors = new List<EntityUid>();
        var query = EntityQueryEnumerator<DoorComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid == grid)
                doors.Add(uid);
        }

        var any = false;
        _random.Shuffle(doors);
        foreach (var door in doors.Take(_random.Next(2, DoorFaults + 1)))
        {
            // A door that won't move, or one picked to, bolts itself instead.
            if (!_random.Prob(0.35f) && _doors.TryToggleDoor(door))
            {
                any = true;
                continue;
            }

            if (TryComp<DoorBoltComponent>(door, out var bolts) && !bolts.BoltsDown)
            {
                // An unpowered door's bolts don't move, so only count one that took.
                _doors.SetBoltsDown((door, bolts), true);
                if (!bolts.BoltsDown)
                    continue;

                _failed.Add((door, until, Failure.Bolt));
                any = true;
            }
        }

        return any;
    }

    /// <summary>Brings back what gave out, once its time is up.</summary>
    private void RestoreFailures(TimeSpan now)
    {
        for (var i = _failed.Count - 1; i >= 0; i--)
        {
            var (uid, until, kind) = _failed[i];
            if (now < until)
                continue;

            _failed.RemoveAt(i);
            if (TerminatingOrDeleted(uid))
                continue;

            switch (kind)
            {
                case Failure.Thruster when TryComp<ThrusterComponent>(uid, out var thruster)
                                           && !thruster.IsOn && _thrusters.CanEnable(uid, thruster):
                    _thrusters.EnableThruster(uid, thruster);
                    break;
                case Failure.Breaker when TryComp<ApcComponent>(uid, out var apc) && !apc.MainBreakerEnabled:
                    _apc.ApcToggleBreaker(uid, apc);
                    break;
                case Failure.Bolt when TryComp<DoorBoltComponent>(uid, out var bolts) && bolts.BoltsDown:
                    _doors.SetBoltsDown((uid, bolts), false);
                    break;
            }
        }
    }
}
