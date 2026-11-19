using System.Numerics;
using Content.Server._WG.Planets.Bounds;
using Content.Server.Parallax;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WG.Farming;

/// <summary>
/// Holds a homestead test ground until its world has finished generating (every site, road and creature down), then
/// wipes the patch to bare biome ground, so nothing the world placed late lands on the test ground, and lays it out.
/// </summary>
public sealed class WGHomesteadTestSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private BiomeSystem _biomes = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private SharedRoofSystem _roofs = default!;

    /// <summary>The longest it waits for a world before building anyway.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(2);

    private readonly List<(EntityUid Ground, Box2 Patch, TimeSpan Deadline, Action Build)> _pending = new();

    /// <summary>Builds the test ground with <paramref name="build"/> once <paramref name="ground"/> has finished generating.</summary>
    public void Queue(EntityUid ground, Box2 patch, Action build)
    {
        _pending.Add((ground, patch, _timing.CurTime + Patience, build));
    }

    public override void Update(float frameTime)
    {
        for (var i = _pending.Count - 1; i >= 0; i--)
        {
            var (ground, patch, deadline, build) = _pending[i];
            if (TerminatingOrDeleted(ground))
            {
                _pending.RemoveAt(i);
                continue;
            }

            if (!HasComp<WGPlanetPreloadedComponent>(ground) && _timing.CurTime < deadline)
                continue;

            _pending.RemoveAt(i);
            Clear(ground, patch);
            build();
        }
    }

    /// <summary>
    /// Wipes the patch to bare ground: deletes whatever stands on it (keeping players), any other grid crossing it, and
    /// roofs, resets its tiles to the biome's own, and pins them so the biome grows nothing back.
    /// </summary>
    public void Clear(EntityUid ground, Box2 patch)
    {
        if (!TryComp<MapGridComponent>(ground, out var grid) || !TryComp<TransformComponent>(ground, out var xform))
            return;

        var mapId = xform.MapID;
        var world = patch.Enlarged(1f);

        // Not what is inside things: that goes with its holder, and a player's own belongings must stay.
        foreach (var uid in _lookup.GetEntitiesIntersecting(mapId, world, LookupFlags.Uncontained))
        {
            if (uid == ground || HasComp<ActorComponent>(uid) || HasComp<MapGridComponent>(uid) || HasComp<MapComponent>(uid))
                continue;
            QueueDel(uid);
        }

        var grids = new List<Entity<MapGridComponent>>();
        _maps.FindGridsIntersecting(mapId, world, ref grids);
        foreach (var other in grids)
        {
            if (other.Owner != ground)
                QueueDel(other);
        }

        var tiles = new List<(Vector2i, Tile)>();
        var roof = CompOrNull<RoofComponent>(ground);
        for (var x = (int) MathF.Floor(patch.Left); x < (int) MathF.Ceiling(patch.Right); x++)
        {
            for (var y = (int) MathF.Floor(patch.Bottom); y < (int) MathF.Ceiling(patch.Top); y++)
            {
                var index = new Vector2i(x, y);
                if (_biomes.TryGetBiomeTile(ground, grid, index, out var tile))
                    tiles.Add((index, tile.Value));
                if (roof != null)
                    _roofs.SetRoof((ground, grid, roof), index, false);
            }
        }

        _maps.SetTiles(ground, grid, tiles);
        _biomes.ReserveTiles(ground, patch, new List<(Vector2i Index, Tile Tile)>());
    }
}
