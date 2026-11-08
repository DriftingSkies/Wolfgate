using Content.Shared.Maps;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Spawns the tentacles of <see cref="FaunaTentacleFieldComponent"/> mobs.
/// </summary>
public sealed partial class FaunaTentacleFieldSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private ITileDefinitionManager _tileDefs = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private FaunaHelperSystem _helper = default!;

    private readonly List<Vector2i> _tiles = new();

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<FaunaTentacleFieldComponent, ActiveNPCComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var comp, out _, out var xform))
        {
            if (now < comp.NextSpawn)
                continue;

            comp.NextSpawn = now + comp.Interval;
            if (_mobState.IsDead(uid) || !TryComp<MapGridComponent>(xform.ParentUid, out var grid))
                continue;

            var centre = _map.LocalToTile(xform.ParentUid, grid, xform.Coordinates);
            var gridUid = xform.ParentUid;
            _helper.GetFreeTiles(gridUid, grid, centre, comp.Radius, FaunaTileShape.SquareNoCentre, _tiles,
                comp.Tiles.Count == 0 ? null : tile => comp.Tiles.Contains(
                    _tileDefs[_map.GetTileRef(gridUid, grid, tile).Tile.TypeId].ID));
            foreach (var tile in _tiles)
            {
                if (_random.Prob(comp.Chance))
                    Spawn(comp.Tentacle, _map.GridTileToLocal(xform.ParentUid, grid, tile));
            }
        }
    }
}
