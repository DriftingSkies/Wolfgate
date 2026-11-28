using System.Numerics;
using Content.Server._Crescent.ShipShields;
using Content.Server._Mono.NPC.HTN;
using Content.Server._Mono.Projectiles.TargetSeeking;
using Content.Server._WG.NpcCrew.Components;
using Content.Server._WG.ShipShields;
using Content.Shared._WG.ShipModules;
using Content.Shared.Explosion.Components;
using Content.Shared.Projectiles;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WG.ShipModules;

/// <summary>
/// Fires the modules of ships flown by NPCs (Mono ship AI or NPC crews) with no player at a helm: jets out of the path
/// of incoming rounds, the skimmer away from a missile lock or a big round, the fortress while its field takes hits and
/// the heat allows, and the feeder while the guns are engaged. Passive modules need no one.
/// </summary>
/// <remarks>
/// Runs every <see cref="SweepInterval"/> over the few ships with modules and an NPC aboard. The one costly step, the
/// lookup for incoming rounds, only runs for a ship whose jets or skimmer could fire right now.
/// </remarks>
public sealed partial class WGShipModuleNpcSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ShipShieldsSystem _shields = default!;
    [Dependency] private WGShipModuleSystem _modules = default!;

    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(0.5);

    /// <summary>How far out incoming rounds are looked for, in metres.</summary>
    private const float ScanRange = 300f;

    /// <summary>Seconds before impact under which the jets dodge a round.</summary>
    private const float DodgeTime = 1.2f;

    /// <summary>Seconds before impact under which the skimmer escapes a big round.</summary>
    private const float SkimTime = 1.5f;

    /// <summary>Rounds further out than this, in seconds, are ignored; the longer of the two above.</summary>
    private const float WarnTime = SkimTime;

    /// <summary>Metres beyond the hull's bounds a round still counts as on course.</summary>
    private const float HullMargin = 4f;

    /// <summary>A missile locked on the ship within this many metres sends it skimming.</summary>
    private const float SkimRange = 250f;

    /// <summary>Damage from which a round is big enough to skim away from.</summary>
    private const float BigDamage = 1000f;

    /// <summary>Explosion intensity from which a round is big enough to skim away from.</summary>
    private const float BigBlast = 300f;

    /// <summary>How long after its field's last hit a ship counts as under fire.</summary>
    private static readonly TimeSpan UnderFireTime = TimeSpan.FromSeconds(4);

    /// <summary>Share of heat capacity from which the fortress goes up under fire.</summary>
    private const float FortressOnHeat = 0.5f;

    /// <summary>Share of heat capacity at which the fortress comes down before it overloads the field.</summary>
    private const float FortressOffHeat = 0.85f;

    private readonly HashSet<EntityUid> _playerGrids = new();
    private readonly HashSet<EntityUid> _aiGrids = new();
    private readonly HashSet<EntityUid> _engaged = new();
    private readonly HashSet<EntityUid> _skimReady = new();
    private readonly Dictionary<EntityUid, Vector2> _locks = new();
    private readonly HashSet<Entity<ProjectileComponent>> _rounds = new();
    private TimeSpan _nextSweep;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        if (now < _nextSweep)
            return;

        _nextSweep = now + SweepInterval;
        if (Count<WGShipModuleBayComponent>() == 0)
            return;

        FindAiGrids(now);
        if (_aiGrids.Count == 0)
            return;

        _skimReady.Clear();
        foreach (var grid in _aiGrids)
        {
            if (TryComp<WGShipModuleBayComponent>(grid, out var bay) && SlotOf<WGPhaseSkimmerEffect>(bay) is { } skim
                && Ready(grid, skim, now))
                _skimReady.Add(grid);
        }

        FindMissileLocks();
        foreach (var grid in _aiGrids)
        {
            if (TryComp<WGShipModuleBayComponent>(grid, out var bay))
                Decide(grid, bay, now);
        }
    }

    /// <summary>Collects ships with modules flown by NPCs and no player at a helm, and which of them are firing.</summary>
    private void FindAiGrids(TimeSpan now)
    {
        _playerGrids.Clear();
        _aiGrids.Clear();
        _engaged.Clear();

        var pilots = EntityQueryEnumerator<PilotComponent, ActorComponent>();
        while (pilots.MoveNext(out _, out var pilot, out _))
        {
            if (pilot.Console is { } console && Transform(console).GridUid is { } grid)
                _playerGrids.Add(grid);
        }

        var targeting = EntityQueryEnumerator<ShipTargetingComponent, TransformComponent>();
        while (targeting.MoveNext(out _, out _, out var xform))
        {
            if (AddAiGrid(xform.GridUid) is { } grid)
                _engaged.Add(grid);
        }

        var steerers = EntityQueryEnumerator<ShipSteererComponent, TransformComponent>();
        while (steerers.MoveNext(out _, out _, out var xform))
            AddAiGrid(xform.GridUid);

        var crewPilots = EntityQueryEnumerator<WGPilotDutyComponent, TransformComponent>();
        while (crewPilots.MoveNext(out _, out _, out var xform))
            AddAiGrid(xform.GridUid);

        var gunners = EntityQueryEnumerator<WGGunnerDutyComponent, TransformComponent>();
        while (gunners.MoveNext(out _, out _, out var xform))
            AddAiGrid(xform.GridUid);

        if (_aiGrids.Count == 0)
            return;

        // Cannons an NPC crew fired lately.
        var fire = EntityQueryEnumerator<WGCrewShipFireComponent, TransformComponent>();
        while (fire.MoveNext(out _, out var shot, out var xform))
        {
            if (!shot.Launched && now < shot.Until && xform.GridUid is { } grid && _aiGrids.Contains(grid))
                _engaged.Add(grid);
        }
    }

    private EntityUid? AddAiGrid(EntityUid? grid)
    {
        if (grid is not { } uid || _playerGrids.Contains(uid) || !HasComp<WGShipModuleBayComponent>(uid))
            return null;

        _aiGrids.Add(uid);
        return uid;
    }

    /// <summary>For each ship whose skimmer could fire, the bearing of the nearest missile locked on it within range.</summary>
    private void FindMissileLocks()
    {
        _locks.Clear();
        if (_skimReady.Count == 0)
            return;

        var seekers = EntityQueryEnumerator<TargetSeekingComponent, TransformComponent>();
        while (seekers.MoveNext(out _, out var seeker, out var xform))
        {
            if (seeker.CurrentTarget is not { } target || TerminatingOrDeleted(target))
                continue;

            var grid = HasComp<MapGridComponent>(target) ? target : Transform(target).GridUid;
            if (grid is not { } uid || !_skimReady.Contains(uid))
                continue;

            var bearing = _transform.GetWorldPosition(xform) - _transform.GetWorldPosition(uid);
            if (bearing.Length() <= SkimRange
                && (!_locks.TryGetValue(uid, out var nearest) || bearing.LengthSquared() < nearest.LengthSquared()))
                _locks[uid] = bearing;
        }
    }

    private void Decide(EntityUid grid, WGShipModuleBayComponent bay, TimeSpan now)
    {
        var npc = EnsureComp<WGShipModuleNpcComponent>(grid);
        var fortressOn = Fortress(grid, bay, now);

        if (_engaged.Contains(grid) && !fortressOn && SlotOf<WGAmmoFeederEffect>(bay) is { } feeder
            && Ready(grid, feeder, now))
            _modules.TryActivateAboard(grid, feeder.Slot, null);

        var skim = _skimReady.Contains(grid) ? SlotOf<WGPhaseSkimmerEffect>(bay) : null;
        var jets = SlotOf<WGManeuveringJetsEffect>(bay) is { } fitted && Ready(grid, fitted, now) ? fitted : null;
        if (skim == null && jets == null)
            return;

        if (skim is { } locked && _locks.TryGetValue(grid, out var bearing))
        {
            Fire(grid, npc, locked, Sideways(grid, bearing));
            return;
        }

        if (Incoming(grid, skim != null, out var dodge) is not { } threat)
            return;

        if (skim is { } skimmer && threat.Big <= SkimTime)
            Fire(grid, npc, skimmer, dodge);
        else if (jets is { } jetting && threat.Any <= DodgeTime)
            Fire(grid, npc, jetting, dodge);
    }

    /// <summary>Raises or drops the fortress by fire and heat; whether it is up afterwards.</summary>
    private bool Fortress(EntityUid grid, WGShipModuleBayComponent bay, TimeSpan now)
    {
        if (SlotOf<WGFortressShieldEffect>(bay) is not { } fortress)
            return false;

        var underFire = TryComp<WGShipHeatComponent>(grid, out var hull) && hull.LastHit != TimeSpan.Zero
            && now - hull.LastHit < UnderFireTime;
        var (heat, capacity) = _shields.GetWolfgateShipHeat(grid);
        var share = capacity > 0f ? heat / capacity : 0f;
        var want = underFire && share < FortressOffHeat && (fortress.Active || share >= FortressOnHeat);
        if (want == fortress.Active || now < fortress.ReadyAt)
            return fortress.Active;

        if (want && !_shields.IsWolfgateShipFieldRaised(grid))
            return false;

        return _modules.TryActivateAboard(grid, fortress.Slot, null) ? want : fortress.Active;
    }

    private void Fire(EntityUid grid, WGShipModuleNpcComponent npc, WGFittedModule fitted, Vector2 steer)
    {
        npc.Steer = steer;
        _modules.TryActivateAboard(grid, fitted.Slot, null);
        npc.Steer = Vector2.Zero;
    }

    /// <summary>The first fitted module with an effect of this kind.</summary>
    private WGFittedModule? SlotOf<T>(WGShipModuleBayComponent bay) where T : WGShipModuleEffect
    {
        foreach (var fitted in bay.Fitted)
        {
            if (_prototype.TryIndex(fitted.Module, out var module) && module.Effect is T)
                return fitted;
        }

        return null;
    }

    /// <summary>Whether a fired module could go off now: idle, charged, off cooldown and unlocked at the last sweep.</summary>
    private bool Ready(EntityUid grid, WGFittedModule fitted, TimeSpan now)
    {
        if (fitted.Active || fitted.Charges == 0 || now < fitted.ReadyAt || !TryComp<WGShipModulesComponent>(grid, out var strip))
            return false;

        foreach (var slot in strip.Slots)
        {
            if (slot.Slot == fitted.Slot)
                return slot.Lock == WGShipModuleLock.None;
        }

        return false;
    }

    /// <summary>The ship-relative way across a world bearing, on the side the ship is already drifting.</summary>
    private Vector2 Sideways(EntityUid grid, Vector2 bearing)
    {
        var across = new Vector2(-bearing.Y, bearing.X);
        if (TryComp<PhysicsComponent>(grid, out var body) && Vector2.Dot(across, body.LinearVelocity) < 0f)
            across = -across;

        return across == Vector2.Zero ? Vector2.UnitX : (-_transform.GetWorldRotation(grid)).RotateVec(Vector2.Normalize(across));
    }

    /// <summary>
    /// Seconds until the soonest round on course to hit the ship arrives (<c>Any</c>) and the soonest big one
    /// (<c>Big</c>, only looked at when <paramref name="weighBig"/>), or null when nothing is coming;
    /// <paramref name="dodge"/> is the ship-relative way out of the path that matters most.
    /// </summary>
    private (float Any, float Big)? Incoming(EntityUid grid, bool weighBig, out Vector2 dodge)
    {
        dodge = Vector2.Zero;
        if (!TryComp<PhysicsComponent>(grid, out var body) || !TryComp<MapGridComponent>(grid, out var map))
            return null;

        var xform = Transform(grid);
        var (position, rotation) = _transform.GetWorldPositionRotation(xform);
        var center = position + rotation.RotateVec(body.LocalCenter);
        var radius = MathF.Max(map.LocalAABB.Width, map.LocalAABB.Height) / 2f + HullMargin;
        var any = float.MaxValue;
        var big = float.MaxValue;
        Vector2 anyAway = default, bigAway = default;

        _rounds.Clear();
        _lookup.GetEntitiesInRange(new MapCoordinates(center, xform.MapID), ScanRange, _rounds, LookupFlags.Dynamic);
        foreach (var round in _rounds)
        {
            if (!TryComp<PhysicsComponent>(round, out var roundBody))
                continue;

            var offset = _transform.GetWorldPosition(round) - center;
            var closing = roundBody.LinearVelocity - body.LinearVelocity;
            var speed = closing.LengthSquared();
            if (speed < 1f)
                continue;

            var time = -Vector2.Dot(offset, closing) / speed;
            if (time < 0f || time > WarnTime || time >= any && (!weighBig || time >= big))
                continue;

            var miss = offset + closing * time;
            if (miss.Length() > radius || FromGrid(round.Comp.Weapon, grid) || FromGrid(round.Comp.Shooter, grid))
                continue;

            // Away from where it passes; straight across its path when it comes dead centre.
            var away = miss.LengthSquared() > 0.25f ? -miss : new Vector2(-closing.Y, closing.X);
            if (time < any)
                (any, anyAway) = (time, away);
            if (weighBig && time < big && Big(round))
                (big, bigAway) = (time, away);
        }

        _rounds.Clear();
        if (any == float.MaxValue)
            return null;

        var path = big <= SkimTime ? bigAway : anyAway;
        dodge = (-rotation).RotateVec(Vector2.Normalize(path));
        return (any, big);
    }

    /// <summary>Whether a round hits hard enough to skim away from rather than ride out.</summary>
    private bool Big(Entity<ProjectileComponent> round)
    {
        return round.Comp.Damage.GetTotal() >= BigDamage
               || TryComp<ExplosiveComponent>(round, out var explosive) && explosive.TotalIntensity >= BigBlast;
    }

    private bool FromGrid(EntityUid? source, EntityUid grid)
    {
        return source is { } uid && !TerminatingOrDeleted(uid) && Transform(uid).GridUid == grid;
    }
}

/// <summary>Module state of a ship flown by NPCs.</summary>
[RegisterComponent, UnsavedComponent]
public sealed partial class WGShipModuleNpcComponent : Component
{
    /// <summary>Ship-relative direction the jets or skimmer go in for the use being fired; zero otherwise.</summary>
    [ViewVariables]
    public Vector2 Steer;
}
