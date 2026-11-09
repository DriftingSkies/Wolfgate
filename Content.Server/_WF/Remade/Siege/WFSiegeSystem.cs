using System.Numerics;
using Content.Server._WF.Encounters;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Encounters.Systems;
using Content.Server._WF.Lattice;
using Content.Server._WF.Remade.Corruption;
using Content.Server._WF.Remade.Monolith;
using Content.Server.Shuttles.Systems;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._WF.Announcements;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.Lattice;
using Content.Shared._WF.NpcCrew;
using Content.Shared._WF.Remade;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Maps;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Remade.Siege;

/// <summary>
/// A Scion that wakes past Harvest sends flesh ships against the reality anchors: Choir-crewed hulls overgrown with
/// corruption, carrying Remade. A flesh ship near its anchor bleeds the anchor's shield, then the core once the shield is
/// down, and puts boarding parties on the station. The anchor's own defence answers it.
/// </summary>
public sealed class WFSiegeSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private DamageableSystem _damage = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ShuttleSystem _shuttle = default!;
    [Dependency] private TurfSystem _turf = default!;
    [Dependency] private WFCorruptionSystem _corruption = default!;
    [Dependency] private WFEncounterSchedulerSystem _scheduler = default!;
    [Dependency] private WFEncounterSystem _encounters = default!;
    [Dependency] private WFLatticeSystem _lattice = default!;

    private const string FleetPrototype = "WFEncounterNecroticFleet";
    private static readonly TimeSpan TickRate = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ProvokeGap = TimeSpan.FromSeconds(10);
    private static readonly Color FleshColor = Color.FromHex("#9b2a4a");
    private static readonly ProtoId<DamageTypePrototype> Structural = "Structural";
    private static readonly SoundSpecifier SiegeSound = new SoundCollectionSpecifier("WFRemadeBruteShoutLong");

    /// <summary>What a boarding party is drawn from.</summary>
    private static readonly EntProtoId[] Boarders = { "WFRemadeSlasher", "WFRemadeSlasher", "WFRemadeLeaper", "WFRemadeHopper" };

    /// <summary>How far out from its anchor a flesh ship arrives, in metres.</summary>
    private const float ArriveMin = 2500f;
    private const float ArriveMax = 3500f;

    /// <summary>How long the crew keep after their anchor, in seconds; the encounter ends long before.</summary>
    private const float OrderDuration = 7200f;

    /// <summary>How long a flesh ship lasts before its encounter is ended, in seconds.</summary>
    private const float Lifetime = 3600f;

    /// <summary>Share of a flesh ship's floor that comes overgrown, and most tiles grown.</summary>
    private const float Overgrowth = 0.3f;
    private const int MaxOvergrowth = 80;

    /// <summary>Most boarders alive on one station from one ship, and how far from the core they land, in tiles.</summary>
    private const int MaxBoarders = 6;
    private const int BoardReach = 12;

    private TimeSpan _next;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFEncounterAdminAttackEvent>(OnAdminAttack);
    }

    /// <summary>A flesh ship an admin sets on an anchor station besieges it as a wave's would.</summary>
    private void OnAdminAttack(ref WFEncounterAdminAttackEvent args)
    {
        if (!TryComp<WFEncounterComponent>(args.Encounter, out var encounter) || encounter.Prototype != FleetPrototype
            || !TryComp<WFLatticeAnchorStationComponent>(args.Target, out var station) || !Standing(station.Core))
            return;

        var ship = EnsureComp<WFNecroticShipComponent>(args.Ship);
        ship.Encounter = args.Encounter;
        ship.Core = station.Core;
        ship.Station = args.Target;
        if (ship.Expires == TimeSpan.Zero)
            ship.Expires = _timing.CurTime + TimeSpan.FromSeconds(Lifetime);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        if (now < _next)
            return;

        _next = now + TickRate;
        if (!_config.GetCVar(WFSiegeCVars.Enabled))
            return;

        Waves(now);
        Sieges(now, (float) TickRate.TotalSeconds);
        Report();
    }

    /// <summary>Tells each Lattice how many flesh ships are out, for the status app.</summary>
    private void Report()
    {
        var count = CountShips();
        var fields = EntityQueryEnumerator<WFLatticeFieldComponent>();
        while (fields.MoveNext(out var uid, out var field))
        {
            if (field.FleshShips == count)
                continue;

            field.FleshShips = count;
            Dirty(uid, field);
        }
    }

    /// <summary>Sends each woken Scion's next wave when it is due.</summary>
    private void Waves(TimeSpan now)
    {
        var query = EntityQueryEnumerator<WFMonolithComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var monolith, out var xform))
        {
            if (monolith.Local || monolith.Stage < WFMonolithStage.Harvest
                || xform.MapUid is not { } map || !TryComp<WFLatticeFieldComponent>(map, out var field) || field.Unstrung)
                continue;

            if (!TryComp<WFSiegeComponent>(uid, out var siege))
            {
                siege = AddComp<WFSiegeComponent>(uid);
                siege.NextWave = now + TimeSpan.FromSeconds(_config.GetCVar(WFSiegeCVars.FirstWave));
                continue;
            }

            if (now < siege.NextWave)
                continue;

            siege.NextWave = now + TimeSpan.FromSeconds(_config.GetCVar(WFSiegeCVars.WaveInterval) * IntervalScale(monolith.Stage));
            SendWave((uid, monolith), map);
        }
    }

    private static float IntervalScale(WFMonolithStage stage)
    {
        return stage switch
        {
            WFMonolithStage.Crescendo => 0.5f,
            WFMonolithStage.Rapture => 0.66f,
            _ => 1f,
        };
    }

    private static int WaveSize(WFMonolithStage stage)
    {
        return stage switch
        {
            WFMonolithStage.Crescendo => 3,
            WFMonolithStage.Rapture => 2,
            _ => 1,
        };
    }

    /// <summary>Sends a wave of flesh ships at the standing anchor nearest the Scion.</summary>
    public int SendWave(Entity<WFMonolithComponent> scion, EntityUid map)
    {
        var from = _transform.GetWorldPosition(scion);
        if (NearestCore(map, from) is not { } target)
            return 0;

        var room = _config.GetCVar(WFSiegeCVars.MaxShips) - CountShips();
        var size = Math.Min(WaveSize(scion.Comp.Stage), room);
        var sent = 0;
        for (var i = 0; i < size; i++)
        {
            if (SpawnShip(target.Core, target.Station, map))
                sent++;
        }

        if (sent > 0)
        {
            var anchor = Name(target.Core);
            RaiseNetworkEvent(new WFAnnouncementChipEvent
            {
                Level = WFAnnounceChipLevel.Warning,
                Accent = FleshColor,
                Title = Loc.GetString("wf-siege-wave-title"),
                Text = Loc.GetString("wf-siege-wave", ("anchor", anchor), ("count", sent)),
                Sender = Loc.GetString("wf-lattice-chip-sender"),
                Duration = 20f,
                Flash = true,
            });
            Log.Info($"The Scion {ToPrettyString(scion)} sent {sent} flesh ships at {ToPrettyString(target.Core)}.");
        }

        return sent;
    }

    private int CountShips()
    {
        var count = 0;
        var query = EntityQueryEnumerator<WFNecroticShipComponent>();
        while (query.MoveNext(out _, out _))
        {
            count++;
        }

        return count;
    }

    /// <summary>The standing primary anchor nearest a point, and its station.</summary>
    private (EntityUid Core, EntityUid Station)? NearestCore(EntityUid map, Vector2 from)
    {
        (EntityUid, EntityUid)? best = null;
        var bestDistance = float.MaxValue;
        var query = EntityQueryEnumerator<WFLatticeAnchorComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var anchor, out var xform))
        {
            if (!anchor.Primary || !anchor.Active || xform.MapUid != map || xform.GridUid is not { } station
                || TerminatingOrDeleted(uid))
                continue;

            var distance = Vector2.DistanceSquared(_transform.GetWorldPosition(xform), from);
            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            best = (uid, station);
        }

        return best;
    }

    /// <summary>Puts one flesh ship a few km out from an anchor, on its far side from the middle, and sets it on the anchor.</summary>
    private bool SpawnShip(EntityUid core, EntityUid station, EntityUid map)
    {
        if (!_proto.TryIndex<WFEncounterPrototype>(FleetPrototype, out var fleet))
            return false;

        var anchorAt = _transform.GetWorldPosition(core);
        var outward = anchorAt.LengthSquared() > 1f ? Vector2.Normalize(anchorAt) : Vector2.UnitX;
        var mapId = Transform(map).MapID;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var angle = new Angle(_random.NextFloat(-1.2f, 1.2f));
            var point = anchorAt + angle.RotateVec(outward) * _random.NextFloat(ArriveMin, ArriveMax);
            if (!_scheduler.IsClearSpace(mapId, point, 400f, 1500f))
                continue;

            // Pinned, so a fight already raging at the anchor doesn't turn it away; it is ended here when its time is up.
            if (!_encounters.TrySpawn(fleet, new MapCoordinates(point, mapId), out var encounter, pinned: true, offBudget: true)
                || !TryComp<WFEncounterComponent>(encounter, out var encounterComp))
                return false;

            foreach (var (key, state) in encounterComp.Ships)
            {
                if (TerminatingOrDeleted(state.Grid))
                    continue;

                var ship = EnsureComp<WFNecroticShipComponent>(state.Grid);
                ship.Encounter = encounter;
                ship.Expires = _timing.CurTime + TimeSpan.FromSeconds(Lifetime);
                Order((state.Grid, ship), key, core, station);
                Overgrow(state.Grid);
                _meta.SetEntityName(state.Grid, Loc.GetString("wf-siege-ship-name"));
                _shuttle.SetIFFColor(state.Grid, FleshColor);
            }

            return true;
        }

        return false;
    }

    /// <summary>Sets a flesh ship's crew on an anchor.</summary>
    private void Order(Entity<WFNecroticShipComponent> ship, string key, EntityUid core, EntityUid station)
    {
        ship.Comp.Core = core;
        ship.Comp.Station = station;
        var queue = new List<WFCrewObjective>
        {
            new() { Kind = WFCrewObjectiveKind.Attack, Target = GetNetEntity(station), Range = 600f, Duration = OrderDuration },
        };
        _encounters.SetOrders(ship.Comp.Encounter, key, queue);
    }

    /// <summary>Grows corruption over a share of a ship's floor.</summary>
    private void Overgrow(EntityUid grid)
    {
        if (!TryComp<MapGridComponent>(grid, out var gridComp))
            return;

        var grown = 0;
        foreach (var tile in _maps.GetAllTiles(grid, gridComp))
        {
            if (grown >= MaxOvergrowth)
                break;

            if (_turf.IsSpace(tile) || !_random.Prob(Overgrowth))
                continue;

            if (_corruption.GrowLoose(_maps.GridTileToLocal(grid, gridComp, tile.GridIndices)) != null)
                grown++;
        }
    }

    /// <summary>Lets each flesh ship near its anchor bleed it, and puts boarders aboard once the shield is down.</summary>
    private void Sieges(TimeSpan now, float seconds)
    {
        var range = _config.GetCVar(WFSiegeCVars.Range);
        var query = EntityQueryEnumerator<WFNecroticShipComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var ship, out var xform))
        {
            if (now >= ship.Expires)
            {
                if (!TerminatingOrDeleted(ship.Encounter))
                    _encounters.End(ship.Encounter);

                RemCompDeferred<WFNecroticShipComponent>(uid);
                continue;
            }

            if (!Standing(ship.Core))
            {
                if (xform.MapUid is not { } map || NearestCore(map, _transform.GetWorldPosition(xform)) is not { } next)
                    continue;

                if (TryComp<WFEncounterComponent>(ship.Encounter, out var encounter))
                {
                    foreach (var (key, state) in encounter.Ships)
                    {
                        if (state.Grid == uid)
                            Order((uid, ship), key, next.Core, next.Station);
                    }
                }

                continue;
            }

            if (Vector2.Distance(_transform.GetWorldPosition(xform), _transform.GetWorldPosition(ship.Core)) > range)
                continue;

            if (now >= ship.NextProvoke)
            {
                ship.NextProvoke = now + ProvokeGap;
                _lattice.Provoke(ship.Station, uid);
                _audio.PlayPvs(SiegeSound, ship.Core);
            }

            if (ShieldUp(ship.Station, out var emitter))
            {
                emitter.Damage += _config.GetCVar(WFSiegeCVars.ShieldRate) * seconds;
                continue;
            }

            var damage = new DamageSpecifier();
            damage.DamageDict[Structural] = _config.GetCVar(WFSiegeCVars.CoreRate) * seconds;
            _damage.TryChangeDamage(ship.Core, damage, ignoreResistances: true, origin: uid);

            if (now >= ship.NextBoard)
            {
                ship.NextBoard = now + TimeSpan.FromSeconds(_config.GetCVar(WFSiegeCVars.BoardInterval));
                Board(ship);
            }
        }
    }

    private bool Standing(EntityUid core)
    {
        return !TerminatingOrDeleted(core) && TryComp<WFLatticeAnchorComponent>(core, out var anchor) && anchor.Active;
    }

    /// <summary>Whether the station's shield stands, and its emitter.</summary>
    private bool ShieldUp(EntityUid station, out ShipShieldEmitterComponent emitter)
    {
        emitter = default!;
        var shields = EntityQueryEnumerator<WFLatticeAnchorShieldComponent, ShipShieldEmitterComponent, TransformComponent>();
        while (shields.MoveNext(out _, out _, out var comp, out var xform))
        {
            if (xform.GridUid != station)
                continue;

            emitter = comp;
            if (comp.Shield is { } field && !TerminatingOrDeleted(field)
                && (!TryComp<PhysicsComponent>(field, out var body) || body.CanCollide))
                return true;
        }

        return false;
    }

    /// <summary>Puts two Remade on open floor near the core, while the ship has fewer than its share alive there.</summary>
    private void Board(WFNecroticShipComponent ship)
    {
        ship.Boarders.RemoveAll(b => TerminatingOrDeleted(b) || _mobs.IsDead(b));
        if (ship.Boarders.Count >= MaxBoarders || !TryComp<MapGridComponent>(ship.Station, out var grid))
            return;

        var centre = _maps.TileIndicesFor(ship.Station, grid, Transform(ship.Core).Coordinates);
        for (var i = 0; i < 2 && ship.Boarders.Count < MaxBoarders; i++)
        {
            for (var attempt = 0; attempt < 12; attempt++)
            {
                var tile = centre + new Vector2i(_random.Next(-BoardReach, BoardReach + 1), _random.Next(-BoardReach, BoardReach + 1));
                if (!_maps.TryGetTileRef(ship.Station, grid, tile, out var tileRef) || _turf.IsSpace(tileRef)
                    || _turf.IsTileBlocked(tileRef, Content.Shared.Physics.CollisionGroup.MobMask))
                    continue;

                var boarder = Spawn(_random.Pick(Boarders), _maps.GridTileToLocal(ship.Station, grid, tile));
                ship.Boarders.Add(boarder);
                break;
            }
        }
    }
}
