using System.Numerics;
using Content.Server._WG.Remade.Corruption;
using Content.Server._WG.Remade.Gifts;
using Content.Server._WG.Remade.Monolith;
using Content.Server.Shuttles.Systems;
using Content.Shared._WG.Remade.Signals;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Server._WG.Remade.Signals;

/// <summary>Keeps a Signal within reach of its stone: the stone's ship, what is docked to it and any corruption or rooted shard of it.</summary>
public sealed partial class WGSignalSystem
{
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private ShuttleSystem _shuttle = default!;

    private static readonly TimeSpan PullMessageGap = TimeSpan.FromSeconds(3);

    private readonly HashSet<EntityUid> _docked = new();
    private readonly HashSet<Entity<WGCorruptionComponent>> _corruptionNear = new();
    private readonly HashSet<Entity<WGMonolithShardComponent>> _shardsNear = new();

    private void InitializeLeash()
    {
    }

    private void Leash(TimeSpan now)
    {
        var query = EntityQueryEnumerator<WGSignalComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var signal, out var xform))
        {
            if (signal.Possessing != null || !Bound((uid, signal), out var stone))
                continue;

            var here = _transform.GetMapCoordinates(uid, xform);
            if (!_corruption.IsSanctuaryAt(uid) && InReach(stone, here))
            {
                signal.LastGood = here;
                continue;
            }

            PullBack((uid, signal), stone, here, now);
        }
    }

    /// <summary>Whether a place is somewhere the stone's Signals may be: near its ship or a ship docked to it, or near its corruption or a rooted shard of it.</summary>
    public bool InReach(Entity<WGMonolithComponent> stone, MapCoordinates at)
    {
        var stoneAt = _transform.GetMapCoordinates(stone);
        if (stoneAt.MapId != at.MapId)
            return false;

        if (Transform(stone).GridUid is { } grid)
        {
            _docked.Clear();
            _shuttle.GetAllDockedShuttles(grid, _docked);
            _docked.Add(grid);
            foreach (var hull in _docked)
            {
                if (NearHull(hull, at.Position))
                    return true;
            }
        }
        else if ((stoneAt.Position - at.Position).Length() <= WGSignalMath.FreeRadius)
        {
            return true;
        }

        _corruptionNear.Clear();
        _lookup.GetEntitiesInRange(at, WGSignalMath.CorruptionReach, _corruptionNear);
        foreach (var tile in _corruptionNear)
        {
            if (tile.Comp.Source == stone.Owner)
                return true;
        }

        _shardsNear.Clear();
        _lookup.GetEntitiesInRange(at, WGSignalMath.ShardReach, _shardsNear);
        foreach (var shard in _shardsNear)
        {
            if (shard.Comp.Deployed && shard.Comp.Parent == stone.Owner)
                return true;
        }

        return false;
    }

    private bool NearHull(EntityUid hull, Vector2 world)
    {
        if (!TryComp<MapGridComponent>(hull, out var grid))
            return false;

        var local = Vector2.Transform(world, _transform.GetInvWorldMatrix(hull));
        return grid.LocalAABB.Enlarged(WGSignalMath.ShipMargin).Contains(local);
    }

    /// <summary>Sends a Signal that has strayed back to where it last was allowed, or to the stone if that's no longer so.</summary>
    private void PullBack(Entity<WGSignalComponent> ent, Entity<WGMonolithComponent> stone, MapCoordinates here, TimeSpan now)
    {
        var back = _transform.GetMapCoordinates(stone);
        if (ent.Comp.LastGood is { } last && last.MapId == here.MapId && InReach(stone, last))
            back = last;

        _transform.SetMapCoordinates(ent, back);
        _physics.SetLinearVelocity(ent, Vector2.Zero);
        ent.Comp.Pulls++;

        if (now >= ent.Comp.NextPullMessage)
        {
            ent.Comp.NextPullMessage = now + PullMessageGap;
            Refuse(ent, "wg-signal-pulled");
        }
    }
}
