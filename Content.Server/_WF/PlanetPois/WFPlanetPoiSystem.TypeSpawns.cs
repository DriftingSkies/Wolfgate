using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Shared._WF.PlanetPois;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.PlanetPois;

public sealed partial class WFPlanetPoiSystem
{
    /// <summary>Puts each typed site's <see cref="WFPoiTypePrototype.Spawns"/> down, a site a tick.</summary>
    private async Task SpawnTypeExtrasAsync(EntityUid ground, MapGridComponent grid, WFPlanetPoiComponent record)
    {
        foreach (var site in record.Sites.ToList())
        {
            if (site.Type is not { } typeId || !_proto.TryIndex(typeId, out var type) || type.Spawns.Count == 0)
                continue;

            await _preload.Breathe();
            if (TerminatingOrDeleted(ground))
                return;
            SpawnTypeExtras((ground, grid), site, type);
        }
    }

    /// <summary>
    /// Puts a site's type spawns, such as a ruin's clues or a tradepost's safe zone, on its free floor nearest its middle,
    /// one a tile. Returns what was put down.
    /// </summary>
    public List<EntityUid> SpawnTypeExtras(Entity<MapGridComponent> ground, WFPlanetPoiSite site, WFPoiTypePrototype type)
    {
        var spawned = new List<EntityUid>();
        var middle = new Vector2((site.Bounds.Left + site.Bounds.Right) / 2f, (site.Bounds.Bottom + site.Bounds.Top) / 2f);
        var free = FreeSiteFloor(ground, site)
            .OrderBy(index => Vector2.DistanceSquared(index + new Vector2(0.5f), middle))
            .ToList();
        for (var i = 0; i < type.Spawns.Count && i < free.Count; i++)
        {
            spawned.Add(Spawn(type.Spawns[i], _maps.GridTileToLocal(ground, ground.Comp, free[i])));
        }

        return spawned;
    }

    /// <summary>The site's laid floor, not the world's own ground or a road through it, with nothing anchored on it.</summary>
    private IEnumerable<Vector2i> FreeSiteFloor(Entity<MapGridComponent> ground, WFPlanetPoiSite site)
    {
        var record = CompOrNull<WFPlanetPoiComponent>(ground);
        var biome = CompOrNull<Content.Shared.Parallax.Biomes.BiomeComponent>(ground);
        for (var x = site.Bounds.Left; x < site.Bounds.Right; x++)
        {
            for (var y = site.Bounds.Bottom; y < site.Bounds.Top; y++)
            {
                var index = new Vector2i(x, y);
                if (record != null && (record.RoadTiles.Contains(index) || record.ShoulderTiles.Contains(index) || record.TrailTiles.Contains(index)))
                    continue;
                if (!_maps.TryGetTileRef(ground, ground.Comp, index, out var tile) || tile.Tile.IsEmpty)
                    continue;
                if (biome != null && _biome.TryGetTile(index, biome.Layers, biome.Seed, null, out var natural)
                    && natural.Value.TypeId == tile.Tile.TypeId)
                    continue;

                var anchored = _maps.GetAnchoredEntitiesEnumerator(ground, ground.Comp, index);
                if (anchored.MoveNext(out _))
                    continue;

                yield return index;
            }
        }
    }
}
