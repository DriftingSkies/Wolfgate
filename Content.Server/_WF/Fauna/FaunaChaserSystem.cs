using System.Numerics;
using Content.Shared._WF.Fauna;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Robust.Shared.Containers;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Launches and steps the chasers of <see cref="FaunaChaserComponent"/> mobs.
/// </summary>
public sealed partial class FaunaChaserSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private TurfSystem _turf = default!;
    [Dependency] private SharedContainerSystem _container = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FaunaChaserComponent, FaunaChaserActionEvent>(OnChaserAction);
    }

    private void OnChaserAction(Entity<FaunaChaserComponent> ent, ref FaunaChaserActionEvent args)
    {
        if (args.Handled || TerminatingOrDeleted(args.Target) || _container.IsEntityInContainer(args.Target)
            || Transform(args.Target).MapID != Transform(ent).MapID)
            return;

        args.Handled = true;
        var chaser = Spawn(ent.Comp.Chaser, _transform.GetMapCoordinates(ent));
        if (!TryComp<FaunaChaserMoverComponent>(chaser, out var mover))
            return;

        var now = _timing.CurTime;
        mover.Target = args.Target;
        mover.NextStep = now + mover.StepDelay;
        mover.EndAt = now + mover.Lifetime;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<FaunaChaserMoverComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var comp, out var xform))
        {
            if (now >= comp.EndAt || comp.Target is not { } target || TerminatingOrDeleted(target)
                || _container.IsEntityInContainer(target) || Transform(target).MapID != xform.MapID
                || xform.GridUid is not { } grid || !TryComp<MapGridComponent>(grid, out var gridComp))
            {
                QueueDel(uid);
                continue;
            }

            if (now < comp.NextStep)
                continue;

            comp.NextStep = now + comp.StepDelay;
            var tile = _map.TileIndicesFor(grid, gridComp, xform.Coordinates);
            var targetTile = _map.TileIndicesFor(grid, gridComp, Transform(target).Coordinates);
            var diff = targetTile - tile;
            if (comp.Steps % comp.RepickEvery == 0 || comp.Direction == Vector2i.Zero)
                comp.Direction = Pick(diff);

            comp.Steps++;
            var next = tile + comp.Direction;
            if (_turf.IsTileBlocked(grid, next, CollisionGroup.Impassable, gridComp))
            {
                comp.Direction = Vector2i.Zero;
                continue;
            }

            var coords = _map.GridTileToLocal(grid, gridComp, next);
            _transform.SetCoordinates(uid, coords);
            Spawn(comp.Effect, coords);
        }
    }

    private static Vector2i Pick(Vector2i diff)
    {
        if (diff == Vector2i.Zero)
            return Vector2i.Zero;

        return Math.Abs(diff.X) >= Math.Abs(diff.Y)
            ? new Vector2i(Math.Sign(diff.X), 0)
            : new Vector2i(0, Math.Sign(diff.Y));
    }
}
