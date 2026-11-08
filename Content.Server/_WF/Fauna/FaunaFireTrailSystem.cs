using Content.Shared.Maps;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Physics;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Spawns the fires of <see cref="FaunaFireTrailComponent"/> mobs.
/// </summary>
public sealed partial class FaunaFireTrailSystem : EntitySystem
{
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private TurfSystem _turf = default!;
    [Dependency] private SharedMapSystem _map = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FaunaFireTrailComponent, MoveEvent>(OnMove);
        SubscribeLocalEvent<FaunaFireTrailComponent, MobStateChangedEvent>(OnMobStateChanged);
    }

    private void OnMove(Entity<FaunaFireTrailComponent> ent, ref MoveEvent args)
    {
        var parent = args.OldPosition.EntityId;
        if (parent != args.NewPosition.EntityId || !TryComp<MapGridComponent>(parent, out var grid))
            return;

        var old = _map.LocalToTile(parent, grid, args.OldPosition);
        if (old == _map.LocalToTile(parent, grid, args.NewPosition) || _mobState.IsDead(ent))
            return;

        TrySpawnFire(ent, (parent, grid), old);
    }

    private void OnMobStateChanged(Entity<FaunaFireTrailComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead)
            return;

        var xform = Transform(ent);
        if (!TryComp<MapGridComponent>(xform.ParentUid, out var grid))
            return;

        var centre = _map.LocalToTile(xform.ParentUid, grid, xform.Coordinates);
        var radius = ent.Comp.DeathRadius;
        for (var x = -radius; x <= radius; x++)
        {
            for (var y = -radius; y <= radius; y++)
            {
                if (x * x + y * y <= radius * radius)
                    TrySpawnFire(ent, (xform.ParentUid, grid), centre + new Vector2i(x, y));
            }
        }
    }

    private void TrySpawnFire(Entity<FaunaFireTrailComponent> ent, Entity<MapGridComponent> grid, Vector2i tile)
    {
        if (_turf.IsTileBlocked(grid, tile, CollisionGroup.Impassable, grid.Comp))
            return;

        Spawn(ent.Comp.Fire, _map.GridTileToLocal(grid, grid.Comp, tile));
    }
}
