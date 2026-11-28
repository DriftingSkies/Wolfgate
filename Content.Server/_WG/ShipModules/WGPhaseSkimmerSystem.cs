using System.Numerics;
using Content.Server._Crescent.ShipShields;
using Content.Server._Mono.Projectiles.TargetSeeking;
using Content.Shared._WG.ShipModules;
using Content.Shared._WG.ShipShields;
using Robust.Server.Audio;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Player;

namespace Content.Server._WG.ShipModules;

/// <summary>
/// Runs the phase skimmer: drops the hull back further along the way it is travelling, short of whatever grid is in
/// the way, and breaks the locks of missiles chasing it.
/// </summary>
public sealed partial class WGPhaseSkimmerSystem : EntitySystem
{
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ShipShieldsSystem _shields = default!;
    [Dependency] private TargetSeekingSystem _seeking = default!;
    [Dependency] private WGManeuveringJetsSystem _jets = default!;
    [Dependency] private WGShipModuleSystem _modules = default!;

    /// <summary>Clearance kept round the hull at its arrival, in metres.</summary>
    private const float Clearance = 2f;

    /// <summary>Distances tried, as shares of the full range, from the farthest.</summary>
    private const int Steps = 12;

    /// <summary>Where each ship's skim lands, between the attempt and the activation.</summary>
    private readonly Dictionary<EntityUid, Vector2> _pending = new();

    private List<Entity<MapGridComponent>> _grids = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGShipModuleLockEvent>(OnLock);
        SubscribeLocalEvent<WGShipModuleActivateAttemptEvent>(OnAttempt);
        SubscribeLocalEvent<WGShipModuleActivatedEvent>(OnActivated);
    }

    private void OnLock(ref WGShipModuleLockEvent args)
    {
        if (args.Lock == WGShipModuleLock.None && args.Module.Effect is WGPhaseSkimmerEffect)
            args.Lock = _modules.MotionLock(args.Grid, true);
    }

    private void OnAttempt(ref WGShipModuleActivateAttemptEvent args)
    {
        if (args.Cancelled || args.Module.Effect is not WGPhaseSkimmerEffect skimmer)
            return;

        if (FindLanding(args.Grid, skimmer, args.User) is not { } landing)
        {
            args.Cancelled = true;
            args.Reason = "wg-ship-module-skimmer-blocked";
            return;
        }

        _pending[args.Grid] = landing;
    }

    private void OnActivated(ref WGShipModuleActivatedEvent args)
    {
        if (args.Module.Effect is not WGPhaseSkimmerEffect skimmer || !_pending.Remove(args.Grid, out var landing))
            return;

        var xform = Transform(args.Grid);
        var (from, rotation) = _transform.GetWorldPositionRotation(xform);
        var origin = new MapCoordinates(from, xform.MapID);
        RaiseNetworkEvent(new WGPhaseSkimEvent(GetNetEntity(args.Grid), from, rotation, landing),
            Filter.BroadcastMap(xform.MapID));
        if (skimmer.DepartSound != null)
            _audio.PlayStatic(skimmer.DepartSound, Filter.Pvs(origin), _transform.ToCoordinates(origin), true);

        _transform.SetWorldPosition(args.Grid, landing);
        if (skimmer.ArriveSound != null)
            _audio.PlayPvs(skimmer.ArriveSound, args.Grid);

        var (_, capacity) = _shields.GetWolfgateShipHeat(args.Grid);
        _shields.AddWolfgateShipHeat(args.Grid, capacity * skimmer.Heat, WGShipShieldEffects.GunHoldHeat);
        BreakLocks(args.Grid);
    }

    /// <summary>
    /// The farthest clear spot along the skim, from the full range down to <see cref="WGPhaseSkimmerEffect.MinShare"/>
    /// of it; null when every spot overlaps another grid.
    /// </summary>
    public Vector2? FindLanding(EntityUid grid, WGPhaseSkimmerEffect skimmer, EntityUid? user)
    {
        if (!TryComp<PhysicsComponent>(grid, out var body) || !TryComp<MapGridComponent>(grid, out var gridComp))
            return null;

        var xform = Transform(grid);
        var (position, rotation) = _transform.GetWorldPositionRotation(xform);
        var direction = Direction(grid, skimmer, body, user, rotation);
        var range = skimmer.Range * MathF.Max(MathF.Min(1f, skimmer.ReferenceMass / MathF.Max(body.Mass, 0.01f)), skimmer.MinScale);
        var local = gridComp.LocalAABB.Enlarged(Clearance);
        for (var i = 0; i <= Steps; i++)
        {
            var share = 1f - (1f - skimmer.MinShare) * i / Steps;
            var landing = position + direction * range * share;
            var bounds = new Box2Rotated(local.Translated(landing), rotation, landing).CalcBoundingBox();
            _grids.Clear();
            _map.FindGridsIntersecting(xform.MapID, bounds, ref _grids, approx: false, includeMap: false);
            if (_grids.TrueForAll(g => g.Owner == grid))
                return landing;
        }

        return null;
    }

    /// <summary>
    /// The way an NPC steering the ship's modules asks for, else along the hull's velocity while it is moving, else the
    /// held direction, else its nose.
    /// </summary>
    private Vector2 Direction(EntityUid grid, WGPhaseSkimmerEffect skimmer, PhysicsComponent body, EntityUid? user, Angle rotation)
    {
        if (TryComp<WGShipModuleNpcComponent>(grid, out var npc) && npc.Steer != Vector2.Zero)
            return rotation.RotateVec(Vector2.Normalize(npc.Steer));

        var velocity = body.LinearVelocity;
        if (velocity.Length() > skimmer.DriftSpeed)
            return Vector2.Normalize(velocity);

        var held = _jets.HeldDirection(grid, user);
        return rotation.RotateVec(held == Vector2.Zero ? Vector2.UnitY : held);
    }

    /// <summary>Missiles chasing the hull or anything aboard lose them and look for another target.</summary>
    private void BreakLocks(EntityUid grid)
    {
        var seekers = EntityQueryEnumerator<TargetSeekingComponent, TransformComponent>();
        while (seekers.MoveNext(out var uid, out var seeker, out var xform))
        {
            if (seeker.CurrentTarget is not { } target || TerminatingOrDeleted(target))
                continue;

            if (target == grid || Transform(target).GridUid == grid)
                _seeking.SetSeekerTarget((uid, seeker), null, xform);
        }
    }
}
