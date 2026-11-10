using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Shared._WF.PlanetPois;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.PlanetPois;

public sealed partial class WFPlanetPoiSystem
{
    /// <summary>Tiles a lot keeps from the sites and other lots, wide enough for a road to pass between.</summary>
    private const int LotGap = 12;

    /// <summary>Tiles a lot keeps from the world's boundary ring.</summary>
    private const int LotEdge = 8;

    private const int LotAttempts = 128;

    /// <summary>
    /// Keeps squares of open ground for the outposts landing this round, after the sites and before the roads: laid with
    /// the world's own ground and pinned, so no road, wreck, camp or growth takes them.
    /// </summary>
    private async Task PlaceOutpostLotsAsync(Entity<BiomeComponent, MapGridComponent> ground, Vector2 centre, float radius,
        WFPlanetPoiTablePrototype table, WFPlanetPoiComponent record, List<Box2i> reserved, List<Box2i> approaches,
        HashSet<Vector2i> changed)
    {
        var size = table.OutpostLotSize;
        if (table.OutpostLots <= 0 || size <= 0)
            return;

        var half = size / 2;
        var footprint = new Box2i(-half, -half, size - half, size - half);
        var reach = radius - LotEdge - half * MathF.Sqrt(2);
        if (reach <= table.CentreClear)
            return;

        for (var i = 0; i < table.OutpostLots; i++)
        {
            await _preload.Breathe();
            if (TerminatingOrDeleted(ground))
                return;

            if (!TryLotOrigin(ground, centre, radius, reach, table, footprint, reserved, approaches, out var lot))
            {
                Log.Warning($"Room for only {i} of {table.OutpostLots} outpost lots on {ToPrettyString(ground)}.");
                return;
            }

            var tiles = new HashSet<Vector2i>();
            var open = new List<(Vector2i, Tile)>();
            for (var x = lot.Left; x < lot.Right; x++)
            for (var y = lot.Bottom; y < lot.Top; y++)
            {
                var tile = new Vector2i(x, y);
                tiles.Add(tile);
                if (_maps.GetTileRef(ground, ground.Comp2, tile).Tile.IsEmpty
                    && _biome.TryGetBiomeTile(ground, ground.Comp2, tile, out var natural))
                    open.Add((tile, natural.Value));
            }

            changed.UnionWith(tiles);
            _maps.SetTiles(ground, ground.Comp2, open);
            _biome.WfPinTiles((ground, ground.Comp1), tiles);
            reserved.Add(lot);
            record.OutpostLots.Add(lot);
        }
    }

    /// <summary>
    /// Lays the world's outpost plots in the band past its points of interest: as many lots as fit, evenly round the
    /// ring, each laid with the world's own ground and pinned so no growth takes it. Nothing else of the world's is placed
    /// there, so the plots never take from the sites' pool.
    /// </summary>
    private async Task PlaceOutpostBandAsync(Entity<BiomeComponent, MapGridComponent> ground, Vector2 centre, float inner, int band,
        WFPlanetPoiTablePrototype table, WFPlanetPoiComponent record, HashSet<Vector2i> changed)
    {
        var size = Math.Min(table.OutpostLotSize, band - LotEdge * 2);
        if (size <= 8)
            return;

        var half = size / 2;
        var ring = inner + band / 2f;
        var spacing = size * MathF.Sqrt(2) + LotGap;
        var count = (int) MathF.Floor(2 * MathF.PI * ring / spacing);
        for (var i = 0; i < count; i++)
        {
            await _preload.Breathe();
            if (TerminatingOrDeleted(ground))
                return;

            var at = centre + new Angle(2 * Math.PI * i / count).ToVec() * ring;
            var origin = new Vector2i((int) MathF.Floor(at.X) - half, (int) MathF.Floor(at.Y) - half);
            var lot = new Box2i(origin, origin + new Vector2i(size, size));
            var tiles = new HashSet<Vector2i>();
            var open = new List<(Vector2i, Tile)>();
            for (var x = lot.Left; x < lot.Right; x++)
            for (var y = lot.Bottom; y < lot.Top; y++)
            {
                var tile = new Vector2i(x, y);
                tiles.Add(tile);
                if (_maps.GetTileRef(ground, ground.Comp2, tile).Tile.IsEmpty
                    && _biome.TryGetBiomeTile(ground, ground.Comp2, tile, out var natural))
                    open.Add((tile, natural.Value));
            }

            changed.UnionWith(tiles);
            _maps.SetTiles(ground, ground.Comp2, open);
            _biome.WfPinTiles((ground, ground.Comp1), tiles);
            record.OutpostLots.Add(lot);
        }
    }

    private bool TryLotOrigin(Entity<BiomeComponent, MapGridComponent> ground, Vector2 centre, float radius, float reach,
        WFPlanetPoiTablePrototype table, Box2i footprint, List<Box2i> reserved, List<Box2i> approaches, out Box2i lot)
    {
        for (var attempt = 0; attempt < LotAttempts; attempt++)
        {
            var candidate = centre + _random.NextAngle().ToVec()
                * MathF.Sqrt(_random.NextFloat(table.CentreClear * table.CentreClear, reach * reach));
            lot = Translate(footprint, new Vector2i((int) MathF.Floor(candidate.X), (int) MathF.Floor(candidate.Y)));
            var spaced = new Box2i(lot.Left - LotGap, lot.Bottom - LotGap, lot.Right + LotGap, lot.Top + LotGap);
            if (!Fits(lot, centre, radius - LotEdge, table.CentreClear)
                || reserved.Any(other => Overlaps(other, spaced))
                || approaches.Any(approach => Overlaps(approach, spaced))
                || Flooded(ground, lot))
                continue;

            var box = spaced;
            if (ground.Comp1.ModifiedTiles.Values.Any(pinned => pinned.Any(tile => Contains(box, tile))))
                continue;

            return true;
        }

        lot = default;
        return false;
    }
}
