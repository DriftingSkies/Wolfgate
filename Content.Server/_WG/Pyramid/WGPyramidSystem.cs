using System.Linq;
using System.Numerics;
using Content.Server._WG.Lattice;
using Content.Server.Explosion.EntitySystems;
using Content.Server.GameTicking;
using Content.Server.Shuttles.Components;
using Content.Shared._WG.Lattice;
using Content.Shared._WG.Pyramid;
using Content.Shared.Camera;
using Content.Shared.Damage;
using Content.Shared.Emp;
using Content.Shared.GameTicking;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Spawners;
using Robust.Shared.Timing;

namespace Content.Server._WG.Pyramid;

/// <summary>
/// The Pyramid. Once in a long while it comes for a crewed ship that lingers in the Coda: it circles the ship like a
/// warship, marks patches of its hull and lasers them, shoves off anything that closes in, and sends waves of warped
/// space out to 400 m. It can be shot down or outrun, an EMP stalls it, and it collapses into chrome shards.
/// </summary>
public sealed partial class WGPyramidSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IComponentFactory _factory = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedCameraRecoilSystem _recoil = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private ExplosionSystem _explosion = default!;
    [Dependency] private WGLatticeSystem _lattice = default!;
    [Dependency] private WGDriftMindSystem _drift = default!;
    [Dependency] private GameTicker _ticker = default!;

    public static readonly EntProtoId Proto = "WGPyramid";
    private static readonly EntProtoId MarkProto = "WGPyramidMark";
    private const int Lines = 12;

    /// <summary>
    /// A wave in flight. Who it has still to reach outlives the Pyramid that sent it; each player hears its sound as
    /// the front reaches them, since sound only comes from the Pyramid in waves.
    /// </summary>
    private sealed record Wave(MapId Map, Vector2 Origin, TimeSpan Left, float Speed, float Reach, float Drift, float Strength,
        SoundSpecifier Sound)
    {
        public readonly HashSet<EntityUid> Reached = new();
    }

    private readonly List<Wave> _waves = new();
    private int _spawned;
    private TimeSpan _nextRoll;
    private TimeSpan _nextLook;
    private TimeSpan _nextShake;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGPyramidComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WGPyramidComponent, DamageChangedEvent>(OnDamaged);
        SubscribeLocalEvent<WGPyramidComponent, EmpPulseEvent>(OnEmp);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ =>
        {
            _spawned = 0;
            _waves.Clear();
        });
    }

    private void OnMapInit(Entity<WGPyramidComponent> ent, ref MapInitEvent args)
    {
        var now = _timing.CurTime;
        ent.Comp.LastTarget = now;
        ent.Comp.NextWave = now;
        ent.Comp.NextBarrage = now + ent.Comp.BarrageEvery;
        ent.Comp.Orbit = _random.Prob(0.5f) ? 1f : -1f;
    }

    private void OnDamaged(Entity<WGPyramidComponent> ent, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased || ent.Comp.Collapsing != null)
            return;

        ent.Comp.LastHit = _timing.CurTime;
        if (args.Damageable.TotalDamage >= ent.Comp.Health)
            StartCollapse(ent);
    }

    private void OnEmp(Entity<WGPyramidComponent> ent, ref EmpPulseEvent args)
    {
        args.Affected = true;
        ent.Comp.StalledUntil = _timing.CurTime + ent.Comp.EmpStall;
        ClearMarks(ent.Comp);
        ent.Comp.Volley.Clear();
        _popup.PopupEntity(Loc.GetString("wg-pyramid-stalled"), ent, PopupType.LargeCaution);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        if (now >= _nextRoll)
        {
            _nextRoll = now + TimeSpan.FromMinutes(1);
            RollArrival(now);
        }

        var look = now >= _nextLook;
        if (look)
            _nextLook = now + TimeSpan.FromSeconds(2);

        var shake = now >= _nextShake;
        if (shake)
            _nextShake = now + TimeSpan.FromSeconds(0.5);

        UpdateWaves(now);
        var query = EntityQueryEnumerator<WGPyramidComponent, TransformComponent, PhysicsComponent>();
        while (query.MoveNext(out var uid, out var pyramid, out var xform, out var body))
        {
            var ent = new Entity<WGPyramidComponent>(uid, pyramid);
            if (pyramid.Collapsing is { } since)
            {
                _physics.SetLinearVelocity(uid, body.LinearVelocity * 0.9f, body: body);
                if (shake)
                    ShakeNear(xform, 120f, 1.2f);
                if (now - since >= pyramid.CollapseTime)
                    FoldAway(ent, xform);
                continue;
            }

            Regenerate(ent, now, frameTime);
            if (now < pyramid.StalledUntil)
            {
                _physics.SetLinearVelocity(uid, body.LinearVelocity * 0.95f, body: body);
                continue;
            }

            if (look)
                Look(ent, xform, now);

            if (pyramid.Target == null && now - pyramid.LastTarget >= pyramid.LeaveAfter)
            {
                SinkAway(ent, xform);
                continue;
            }

            Fly(ent, xform, body, frameTime);
            UpdateVolley(ent, xform, now);
            Bubble(ent, xform, frameTime);
            if (now >= pyramid.NextWave)
            {
                pyramid.NextWave = now + pyramid.WaveEvery;
                SendWave(ent, xform, 1f);
            }

            if (pyramid.FireAt is { } fireAt)
            {
                if (now >= fireAt)
                    FireBarrage(ent, xform);
            }
            else if (pyramid.Target is { } target && now >= pyramid.NextBarrage && !TerminatingOrDeleted(target)
                     && InRange(target, xform, pyramid.FireRange))
            {
                MarkBarrage(ent, target, now);
            }
        }
    }

    #region Arrival

    /// <summary>The tuning on the Pyramid's prototype, which also sets how rarely it comes.</summary>
    private WGPyramidComponent Tuning()
    {
        return _proto.Index(Proto).TryComp<WGPyramidComponent>(out var comp, _factory) ? comp : new WGPyramidComponent();
    }

    /// <summary>Once a minute, each crewed ship in the Coda has a very small chance of drawing it, if none is out.</summary>
    private void RollArrival(TimeSpan now)
    {
        var tuning = Tuning();
        if (_spawned >= tuning.PerRound || _ticker.RoundDuration() < tuning.EarliestAt || EntityQuery<WGPyramidComponent>().Any())
            return;

        foreach (var (grid, depth) in CodaShips())
        {
            if (!_random.Prob(tuning.ChancePerMinute * (1f + depth)))
                continue;

            Summon(grid, false);
            return;
        }
    }

    /// <summary>Every crewed, moving ship in the Coda's hazard bands, with its depth there.</summary>
    private List<(EntityUid Grid, float Depth)> CodaShips()
    {
        var ships = new List<(EntityUid, float)>();
        foreach (var grid in CrewedShips())
        {
            if (CodaDepth(grid) is { } depth)
                ships.Add((grid, depth));
        }

        return ships;
    }

    /// <summary>Every crewed, moving ship anywhere.</summary>
    private HashSet<EntityUid> CrewedShips()
    {
        var ships = new HashSet<EntityUid>();
        foreach (var mob in Crew())
        {
            if (Transform(mob).GridUid is { } grid && IsShip(grid))
                ships.Add(grid);
        }

        return ships;
    }

    /// <summary>Its depth if a grid sits in the Coda's hazard bands, else null.</summary>
    private float? CodaDepth(EntityUid grid)
    {
        var point = _transform.GetMapCoordinates(grid);
        if (_lattice.BandOf(point, out var profile) == null || profile == null || Transform(grid).MapUid is not { } map)
            return null;

        var (band, depth) = _lattice.Locate(map, point.Position);
        return band >= profile.HazardBand ? depth : null;
    }

    private bool IsShip(EntityUid grid)
    {
        return HasComp<ShuttleComponent>(grid) && !HasComp<WGCodaHazardImmuneComponent>(grid)
            && TryComp<PhysicsComponent>(grid, out var body) && body.BodyType == BodyType.Dynamic;
    }

    /// <summary>
    /// Brings the Pyramid in on a ship now, out past its radar. One brought in by hand hunts anywhere; one that came on
    /// its own keeps to the Coda. Returns it.
    /// </summary>
    public EntityUid Summon(EntityUid grid, bool anywhere = true)
    {
        var tuning = Tuning();
        var centre = _transform.GetMapCoordinates(grid);
        var at = centre.Offset(_random.NextAngle().ToVec() * tuning.ArriveDistance);
        var pyramid = Spawn(Proto, at);
        _spawned++;
        var comp = Comp<WGPyramidComponent>(pyramid);
        comp.Target = grid;
        comp.Anywhere = anywhere;
        foreach (var (_, mob) in Aboard(grid))
            _popup.PopupEntity(Loc.GetString("wg-pyramid-arrive"), mob, mob, PopupType.LargeCaution);

        PlayNear(Transform(grid), comp.Arrive, 900f, -6f);

        Log.Info($"The Pyramid came for {ToPrettyString(grid)} at {at}.");
        return pyramid;
    }

    #endregion

    #region Hunting

    /// <summary>Picks the nearest crewed ship in the Coda within reach, keeping its current one while it can.</summary>
    private void Look(Entity<WGPyramidComponent> ent, TransformComponent xform, TimeSpan now)
    {
        var here = _transform.GetWorldPosition(xform);
        EntityUid? best = null;
        var bestDistance = ent.Comp.SearchRange;
        var ships = ent.Comp.Anywhere ? CrewedShips() : CodaShips().Select(s => s.Grid).ToHashSet();
        // A summoned Pyramid keeps after the grid it was sent to for as long as it stands, crewed or not, ship or not.
        if (ent.Comp.Anywhere && ent.Comp.Target is { } current && !TerminatingOrDeleted(current) && HasComp<MapGridComponent>(current))
            ships.Add(current);

        foreach (var grid in ships)
        {
            if (Transform(grid).MapID != xform.MapID)
                continue;

            var distance = Vector2.Distance(_transform.GetWorldPosition(grid), here);
            if (distance > ent.Comp.SearchRange)
                continue;

            // It prefers to keep after the ship it already has.
            if (grid == ent.Comp.Target)
                distance *= 0.6f;

            if (distance < bestDistance)
            {
                best = grid;
                bestDistance = distance;
            }
        }

        if (best != ent.Comp.Target)
            ClearMarks(ent.Comp);

        ent.Comp.Target = best;
        if (best != null)
            ent.Comp.LastTarget = now;
    }

    /// <summary>Circles its target at its stand-off distance like a warship, or slows to a hover with none.</summary>
    private void Fly(Entity<WGPyramidComponent> ent, TransformComponent xform, PhysicsComponent body, float frameTime)
    {
        var desired = Vector2.Zero;
        if (ent.Comp.Target is { } target && !TerminatingOrDeleted(target))
        {
            var offset = _transform.GetWorldPosition(xform) - _transform.GetWorldPosition(target);
            var distance = offset.Length();
            var radial = distance > 0.01f ? offset / distance : Vector2.UnitX;
            var tangent = new Vector2(-radial.Y, radial.X) * ent.Comp.Orbit;
            var close = Math.Clamp((ent.Comp.StandOff - distance) / 25f, -1f, 1f);
            desired = tangent * 0.5f + radial * close;
            if (desired.LengthSquared() > 1f)
                desired = Vector2.Normalize(desired);
            desired *= ent.Comp.Speed;
        }

        var velocity = body.LinearVelocity;
        var change = desired - velocity;
        var step = ent.Comp.Acceleration * frameTime;
        if (change.Length() > step)
            change = Vector2.Normalize(change) * step;

        _physics.SetLinearVelocity(ent, velocity + change, body: body);
        _physics.SetAngularVelocity(ent, 0f, body: body);
    }

    /// <summary>Shoves away any ship that comes within its bubble.</summary>
    private void Bubble(Entity<WGPyramidComponent> ent, TransformComponent xform, float frameTime)
    {
        var here = _transform.GetWorldPosition(xform);
        var radius = ent.Comp.BubbleRadius;
        var grids = new List<Entity<MapGridComponent>>();
        _map.FindGridsIntersecting(xform.MapID, new Box2(here - new Vector2(radius), here + new Vector2(radius)), ref grids);
        foreach (var grid in grids)
        {
            if (!TryComp<PhysicsComponent>(grid, out var body) || body.BodyType != BodyType.Dynamic)
                continue;

            // The square found it; push only if its hull actually reaches into the bubble.
            var bounds = _transform.GetWorldMatrix(grid).TransformBox(grid.Comp.LocalAABB);
            if (Vector2.Distance(bounds.ClosestPoint(here), here) > radius)
                continue;

            var away = _transform.GetWorldPosition(grid) - here;
            var direction = away.LengthSquared() > 0.01f ? Vector2.Normalize(away) : Vector2.UnitY;
            _physics.ApplyLinearImpulse(grid, direction * ent.Comp.BubblePush * body.Mass * frameTime, body: body);
        }
    }

    #endregion

    #region Barrages

    /// <summary>Paints patches of the target's hull, one on a crewmember, and fires on them once the charge is done.</summary>
    public void MarkBarrage(Entity<WGPyramidComponent> ent, EntityUid target, TimeSpan now)
    {
        if (!TryComp<MapGridComponent>(target, out var grid))
            return;

        ent.Comp.NextBarrage = now + ent.Comp.BarrageEvery + ent.Comp.BarrageDelay;
        ent.Comp.FireAt = now + ent.Comp.BarrageDelay;
        var tiles = _map.GetAllTiles(target, grid).Select(t => t.GridIndices).ToList();
        if (tiles.Count == 0)
            return;

        var aboard = Aboard(target).ToList();
        var centres = new List<Vector2i>();
        if (aboard.Count > 0)
            centres.Add(_map.TileIndicesFor(target, grid, Transform(_random.Pick(aboard).Mob).Coordinates));

        for (var i = centres.Count; i < ent.Comp.Patches; i++)
            centres.Add(_random.Pick(tiles));

        foreach (var centre in centres)
            SpawnMark(ent, _map.GridTileToLocal(target, grid, centre));

        // One brought in by hand attacks everything round it: every other grid in range takes patches too.
        if (ent.Comp.Anywhere)
            MarkBystanders(ent, target);

        var line = Loc.GetString($"wg-pyramid-line-{_random.Next(1, Lines + 1)}");
        foreach (var (_, mob) in aboard)
            _popup.PopupEntity(line, mob, mob, PopupType.LargeCaution);

        PlayNear(Transform(target), ent.Comp.Charge, 250f, 3f);
    }

    /// <summary>Paints a few patches on every grid in range besides the target, one under someone aboard where it can.</summary>
    private void MarkBystanders(Entity<WGPyramidComponent> ent, EntityUid target)
    {
        var xform = Transform(ent);
        var here = _transform.GetWorldPosition(xform);
        var range = ent.Comp.FireRange;
        var grids = new List<Entity<MapGridComponent>>();
        _map.FindGridsIntersecting(xform.MapID, new Box2(here - new Vector2(range), here + new Vector2(range)), ref grids);
        foreach (var other in grids)
        {
            if (other.Owner == target || ent.Comp.Marks.Count >= 24 || !InRange(other, xform, range))
                continue;

            var tiles = _map.GetAllTiles(other, other.Comp).Select(t => t.GridIndices).ToList();
            if (tiles.Count == 0)
                continue;

            var centres = new List<Vector2i>();
            var aboard = Aboard(other).ToList();
            if (aboard.Count > 0)
                centres.Add(_map.TileIndicesFor(other, other.Comp, Transform(_random.Pick(aboard).Mob).Coordinates));

            while (centres.Count < 3)
                centres.Add(_random.Pick(tiles));

            foreach (var centre in centres)
                SpawnMark(ent, _map.GridTileToLocal(other, other.Comp, centre));
        }
    }

    /// <summary>Places a mark for the coming barrage; it lasts until a moment after the barrage fires.</summary>
    private void SpawnMark(Entity<WGPyramidComponent> ent, EntityCoordinates at)
    {
        var uid = Spawn(MarkProto, at);
        var mark = EnsureComp<WGPyramidMarkComponent>(uid);
        mark.Pyramid = ent;
        mark.FireAt = ent.Comp.FireAt ?? _timing.CurTime + ent.Comp.BarrageDelay;
        mark.ChargeSeconds = (float) ent.Comp.BarrageDelay.TotalSeconds;
        Dirty(uid, mark);
        EnsureComp<TimedDespawnComponent>(uid).Lifetime = mark.ChargeSeconds + 2f;
        ent.Comp.Marks.Add(uid);
    }

    /// <summary>Whether a grid is on the Pyramid's map and within a range of it.</summary>
    private bool InRange(EntityUid grid, TransformComponent xform, float range)
    {
        var gridXform = Transform(grid);
        return gridXform.MapID == xform.MapID
            && Vector2.Distance(_transform.GetWorldPosition(gridXform), _transform.GetWorldPosition(xform)) <= range;
    }

    /// <summary>
    /// Fires on every mark still standing: one laser after another, a moment apart, so the volley ripples across the
    /// hull and each blast is through the explosion queue before the next lands.
    /// </summary>
    public void FireBarrage(Entity<WGPyramidComponent> ent, TransformComponent xform)
    {
        ent.Comp.FireAt = null;
        var now = _timing.CurTime;
        EntityUid? struck = null;
        foreach (var mark in ent.Comp.Marks)
        {
            if (TerminatingOrDeleted(mark) || Transform(mark).MapID != xform.MapID)
                continue;

            ent.Comp.Volley.Add((Transform(mark).Coordinates, now + TimeSpan.FromSeconds(ent.Comp.Volley.Count * ent.Comp.VolleyStep)));
            struck ??= Transform(mark).GridUid;
        }

        ClearMarks(ent.Comp);
        if (struck is { } hit)
            PlayNear(Transform(hit), ent.Comp.Fire, 250f, 3f);
    }

    /// <summary>Fires each laser of the volley as its moment comes.</summary>
    private void UpdateVolley(Entity<WGPyramidComponent> ent, TransformComponent xform, TimeSpan now)
    {
        if (ent.Comp.Volley.Count == 0)
            return;

        // Shots are queued in order; the oldest due one fires, one a tick at most, so overdue shots keep their spacing.
        var (at, when) = ent.Comp.Volley[0];
        if (now < when)
            return;

        ent.Comp.Volley.RemoveAt(0);
        if (!at.IsValid(EntityManager))
            return;

        // A grid that has since got out of range is spared.
        if (Transform(at.EntityId).GridUid is { } struck && !InRange(struck, xform, ent.Comp.FireRange))
            return;

        var from = _transform.GetMapCoordinates(xform);
        var map = _transform.ToMapCoordinates(at);
        if (map.MapId != from.MapId)
            return;

        _explosion.QueueExplosion(map, ent.Comp.ExplosionType, ent.Comp.ExplosionIntensity, ent.Comp.ExplosionSlope,
            ent.Comp.ExplosionMaxTile, ent);
        RaiseNetworkEvent(new WGPyramidBeamEvent(from.MapId, from.Position, new List<Vector2> { map.Position }),
            Filter.BroadcastMap(from.MapId));
    }

    private void ClearMarks(WGPyramidComponent pyramid)
    {
        foreach (var mark in pyramid.Marks)
            QueueDel(mark);

        pyramid.Marks.Clear();
        pyramid.FireAt = null;
    }

    #endregion

    #region Waves

    /// <summary>Sends a wave of warped space out to its reach, felt by everyone it passes through.</summary>
    public void SendWave(Entity<WGPyramidComponent> ent, TransformComponent xform, float strength)
    {
        var at = _transform.GetMapCoordinates(xform);
        _waves.Add(new Wave(at.MapId, at.Position, _timing.CurTime, ent.Comp.WaveSpeed, ent.Comp.WaveReach,
            ent.Comp.WaveDrift, strength, strength > 1f ? ent.Comp.Collapse : ent.Comp.Ambience));
        RaiseNetworkEvent(new WGPyramidWaveEvent(GetNetEntity(ent), at.MapId, at.Position, ent.Comp.WaveSpeed,
            ent.Comp.WaveReach, strength), Filter.BroadcastMap(at.MapId));
    }

    /// <summary>Touches everyone each wave's front has reached since last tick.</summary>
    private void UpdateWaves(TimeSpan now)
    {
        for (var i = _waves.Count - 1; i >= 0; i--)
        {
            var wave = _waves[i];
            var front = (float) (now - wave.Left).TotalSeconds * wave.Speed;
            foreach (var session in _player.Sessions)
            {
                if (session.AttachedEntity is not { } mob)
                    continue;

                var mobXform = Transform(mob);
                if (mobXform.MapID != wave.Map || wave.Reached.Contains(mob))
                    continue;

                var distance = Vector2.Distance(_transform.GetWorldPosition(mobXform), wave.Origin);
                if (distance > front || distance > wave.Reach)
                    continue;

                wave.Reached.Add(mob);
                if (wave.Strength <= 1f)
                    _audio.PlayGlobal(wave.Sound, session, AudioParams.Default.WithVolume(4f - 16f * distance / wave.Reach));

                if (!HasComp<MobStateComponent>(mob) || _mobState.IsDead(mob))
                    continue;

                _drift.Add(mob, wave.Drift * wave.Strength, WGDriftFlavour.Unmooring);
                _recoil.KickCamera(mob, _random.NextAngle().ToVec() * 0.8f * wave.Strength);
            }

            if (front > wave.Reach)
                _waves.RemoveAt(i);
        }
    }

    #endregion

    #region Health and death

    private void Regenerate(Entity<WGPyramidComponent> ent, TimeSpan now, float frameTime)
    {
        if (now < ent.Comp.NextMend || now - ent.Comp.LastHit < ent.Comp.RegenerationAfter
            || !TryComp<DamageableComponent>(ent, out var damageable) || damageable.TotalDamage <= 0)
            return;

        ent.Comp.NextMend = now + TimeSpan.FromSeconds(1);
        var fraction = MathF.Min(1f, ent.Comp.Regeneration / damageable.TotalDamage.Float());
        _damageable.TryChangeDamage(ent, damageable.Damage * -fraction, true, false, damageable);
    }

    /// <summary>Brought down: it stops, shudders and groans for a while, then folds away.</summary>
    public void StartCollapse(Entity<WGPyramidComponent> ent)
    {
        if (ent.Comp.Collapsing != null)
            return;

        ent.Comp.Collapsing = _timing.CurTime;
        ClearMarks(ent.Comp);
        ent.Comp.Volley.Clear();
        var xform = Transform(ent);
        var at = _transform.GetMapCoordinates(xform);
        RaiseNetworkEvent(new WGPyramidCollapseEvent(GetNetEntity(ent), at.MapId, at.Position,
            (float) ent.Comp.CollapseTime.TotalSeconds), Filter.BroadcastMap(at.MapId));
        PlayNear(xform, ent.Comp.Collapse, ent.Comp.WaveReach, 6f);
        foreach (var (_, mob) in Near(xform, ent.Comp.WaveReach))
            _popup.PopupEntity(Loc.GetString("wg-pyramid-collapsing"), mob, mob, PopupType.LargeCaution);
    }

    private void FoldAway(Entity<WGPyramidComponent> ent, TransformComponent xform)
    {
        var at = _transform.GetMapCoordinates(xform);
        for (var i = 0; i < ent.Comp.LootCount; i++)
            Spawn(ent.Comp.Loot, at.Offset(_random.NextVector2(3f)));

        SendWave(ent, xform, 3f);
        foreach (var (_, mob) in Near(xform, ent.Comp.WaveReach))
            _popup.PopupEntity(Loc.GetString("wg-pyramid-folds"), mob, mob, PopupType.Large);

        QueueDel(ent);
    }

    /// <summary>With nothing left to hunt it sinks back out of the world.</summary>
    private void SinkAway(Entity<WGPyramidComponent> ent, TransformComponent xform)
    {
        SendWave(ent, xform, 1f);
        Log.Info($"The Pyramid found nothing to hunt and sank away at {_transform.GetWorldPosition(xform)}.");
        QueueDel(ent);
    }

    #endregion

    #region Helpers

    /// <summary>Every living, embodied player.</summary>
    private IEnumerable<EntityUid> Crew()
    {
        foreach (var session in _player.Sessions)
        {
            if (session.AttachedEntity is { } mob && HasComp<MobStateComponent>(mob) && !_mobState.IsDead(mob))
                yield return mob;
        }
    }

    /// <summary>Every living player aboard a grid.</summary>
    private IEnumerable<(ICommonSession Session, EntityUid Mob)> Aboard(EntityUid grid)
    {
        foreach (var session in _player.Sessions)
        {
            if (session.AttachedEntity is { } mob && HasComp<MobStateComponent>(mob) && !_mobState.IsDead(mob)
                && Transform(mob).GridUid == grid)
                yield return (session, mob);
        }
    }

    /// <summary>Every player, living or not, within range of a point.</summary>
    private IEnumerable<(ICommonSession Session, EntityUid Mob)> Near(TransformComponent xform, float range)
    {
        var here = _transform.GetWorldPosition(xform);
        foreach (var session in _player.Sessions)
        {
            if (session.AttachedEntity is not { } mob)
                continue;

            var mobXform = Transform(mob);
            if (mobXform.MapID == xform.MapID && Vector2.Distance(_transform.GetWorldPosition(mobXform), here) <= range)
                yield return (session, mob);
        }
    }

    /// <summary>Plays a sound to everyone in range, quieter the farther they are.</summary>
    private void PlayNear(TransformComponent xform, SoundSpecifier sound, float range, float loudest)
    {
        var here = _transform.GetWorldPosition(xform);
        foreach (var (session, mob) in Near(xform, range))
        {
            var distance = Vector2.Distance(_transform.GetWorldPosition(mob), here);
            var volume = loudest - 18f * distance / range;
            _audio.PlayGlobal(sound, session, AudioParams.Default.WithVolume(volume));
        }
    }

    private void ShakeNear(TransformComponent xform, float range, float strength)
    {
        foreach (var (_, mob) in Near(xform, range))
            _recoil.KickCamera(mob, _random.NextAngle().ToVec() * strength);
    }

    #endregion
}
