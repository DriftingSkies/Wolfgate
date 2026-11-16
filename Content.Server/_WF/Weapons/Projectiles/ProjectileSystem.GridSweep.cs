using System.Numerics;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;

namespace Content.Server.Projectiles;

public sealed partial class ProjectileSystem
{
    [Dependency] private IConfigurationManager _wfCfg = default!;
    [Dependency] private SharedMapSystem _wfMap = default!;

    /// <summary>Below this speed relative to the sweep's frame, a grid is left to the frame's own ray.</summary>
    private const float WfMovingGridSpeed = 0.1f;

    private List<Entity<MapGridComponent>> _wfSweepGrids = new();
    private readonly HashSet<EntityUid> _wfMovingGrids = new();
    private readonly List<WfSweepHit> _wfSweepHits = new();

    /// <summary>A sweep hit, with the direction it was swept along and the fraction of a second until impact.</summary>
    private readonly record struct WfSweepHit(EntityUid Entity, float Distance, Vector2 Direction, float Time);

    /// <summary>
    /// Sweeps one tick of travel, casting each grid that moves in this frame with the projectile's velocity relative to it.
    /// A single ray in the parent's frame misses a hull that closes the gap by more than its own thickness in a tick.
    /// </summary>
    private List<WfSweepHit> WfSweep(EntityUid uid, MapCoordinates from, Vector2 velocity, Vector2 frameVelocity,
        float frameTime, int mask)
    {
        _wfSweepHits.Clear();
        _wfMovingGrids.Clear();
        _wfSweepGrids.Clear();

        // A grid can reach the path from as far as it moves in a tick at the physics speed limit.
        var world = velocity + frameVelocity;
        var bounds = Box2.FromTwoPoints(from.Position, from.Position + world * frameTime)
            .Enlarged(_wfCfg.GetCVar(CVars.MaxLinVelocity) * frameTime);
        _wfMap.FindGridsIntersecting(from.MapId, bounds, ref _wfSweepGrids);

        foreach (var grid in _wfSweepGrids)
        {
            if (!_physQuery.TryComp(grid, out var body))
                continue;

            // Translational only: a spinning grid's speed at the round is not the speed of the hull it will meet.
            var gridVelocity = _physics.GetMapLinearVelocity(grid, body);
            if ((gridVelocity - frameVelocity).Length() < WfMovingGridSpeed)
                continue;

            // A spinning grid keeps its hits on the frame's ray too, so its turn cannot hide a wall from both rays.
            var spin = MathF.Abs(body.AngularVelocity) * (from.Position - _transformSystem.GetWorldPosition(grid)).Length();
            if (spin < WfMovingGridSpeed)
                _wfMovingGrids.Add(grid.Owner);

            WfCast(uid, from, world - gridVelocity, frameTime, mask, grid.Owner);
        }

        WfCast(uid, from, velocity, frameTime, mask, null);
        return _wfSweepHits;
    }

    /// <summary>Casts one ray, keeping hits on <paramref name="grid"/>, or with no grid, hits off every grid swept on its own.</summary>
    private void WfCast(EntityUid uid, MapCoordinates from, Vector2 velocity, float frameTime, int mask, EntityUid? grid)
    {
        var speed = velocity.Length();
        if (speed <= 0f)
            return;

        var direction = velocity / speed;
        var ray = new CollisionRay(from.Position, direction, mask);
        foreach (var hit in _physics.IntersectRay(from.MapId, ray, speed * frameTime, uid, false))
        {
            var hitGrid = Transform(hit.HitEntity).GridUid;
            if (grid != null ? hitGrid != grid : hitGrid is { } moving && _wfMovingGrids.Contains(moving))
                continue;

            _wfSweepHits.Add(new WfSweepHit(hit.HitEntity, hit.Distance, direction, hit.Distance / speed));
        }
    }
}
