using System.Globalization;
using System.Numerics;
using Content.Shared._WG.MappingMode;
using Content.Shared._WG.MappingTools;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Sequence;
using Robust.Shared.Serialization.Markdown.Value;

namespace Content.Server._WG.MappingMode;

/// <summary>What a prototype adds to a floor plan.</summary>
public enum WGMappingPreviewCell : byte
{
    None,
    Wall,
    Opening,
}

/// <summary>
/// Reads a map file's floor plan straight from its yaml, without spawning anything: each grid's tiles and the walls,
/// windows and doors parented to it.
/// </summary>
public static class WGMappingPreviewParser
{
    private const string EmptyTile = "Space";
    private const int DefaultChunkSize = 16;

    /// <summary>Largest chunk edge accepted, so a bad file can't ask for a huge loop.</summary>
    private const int MaxChunkSize = 256;

    private sealed class GridCells(Vector2 position, Angle rotation)
    {
        public readonly Vector2 Position = position;
        public readonly Angle Rotation = rotation;
        public readonly List<Vector2i> Floor = new();
        public readonly List<Vector2i> Walls = new();
        public readonly List<Vector2i> Openings = new();
    }

    /// <summary>
    /// The floor plan of a map file's root node. <paramref name="classify"/> is asked once per prototype. A file with
    /// no grids gives a preview with none.
    /// </summary>
    public static WGMappingReferencePreview Parse(MappingDataNode root, long bytes,
        Func<string, WGMappingPreviewCell> classify)
    {
        var tileNames = ReadTileMap(root);
        var grids = new Dictionary<int, GridCells>();
        var entities = 0;
        var tiles = 0;

        if (!root.TryGet<SequenceDataNode>("entities", out var groups))
            return new WGMappingReferencePreview(new List<WGMappingPreviewGrid>(), 0, 0, bytes);

        foreach (var group in Entities(groups))
        {
            entities++;
            if (Component(group, "MapGrid") is not { } mapGrid ||
                !group.TryGet<ValueDataNode>("uid", out var uid) ||
                !int.TryParse(uid.Value, CultureInfo.InvariantCulture, out var id))
            {
                continue;
            }

            var transform = Component(group, "Transform");
            var cells = new GridCells(ReadPosition(transform), ReadRotation(transform));
            tiles += ReadChunks(mapGrid, tileNames, cells.Floor);
            grids[id] = cells;
        }

        if (grids.Count > 0)
            ReadBlockers(groups, grids, classify);

        var result = new List<WGMappingPreviewGrid>(grids.Count);
        foreach (var grid in grids.Values)
        {
            result.Add(new WGMappingPreviewGrid(grid.Position, grid.Rotation, WGMappingFootprint.Merge(grid.Floor),
                WGMappingFootprint.Merge(grid.Walls), WGMappingFootprint.Merge(grid.Openings)));
        }

        return new WGMappingReferencePreview(result, tiles, entities, bytes);
    }

    private static void ReadBlockers(SequenceDataNode groups, Dictionary<int, GridCells> grids,
        Func<string, WGMappingPreviewCell> classify)
    {
        foreach (var node in groups.Sequence)
        {
            if (node is not MappingDataNode group ||
                !group.TryGet<ValueDataNode>("proto", out var proto) ||
                string.IsNullOrEmpty(proto.Value) ||
                !group.TryGet<SequenceDataNode>("entities", out var list))
            {
                continue;
            }

            var kind = classify(proto.Value);
            if (kind == WGMappingPreviewCell.None)
                continue;

            foreach (var entityNode in list.Sequence)
            {
                if (entityNode is not MappingDataNode entity ||
                    Component(entity, "Transform") is not { } transform ||
                    !transform.TryGet<ValueDataNode>("parent", out var parent) ||
                    !int.TryParse(parent.Value, CultureInfo.InvariantCulture, out var parentId) ||
                    !grids.TryGetValue(parentId, out var grid))
                {
                    continue;
                }

                var cell = MappingToolsMath.CellOf(ReadPosition(transform));
                (kind == WGMappingPreviewCell.Wall ? grid.Walls : grid.Openings).Add(cell);
            }
        }
    }

    private static IEnumerable<MappingDataNode> Entities(SequenceDataNode groups)
    {
        foreach (var node in groups.Sequence)
        {
            if (node is not MappingDataNode group || !group.TryGet<SequenceDataNode>("entities", out var list))
                continue;

            foreach (var entity in list.Sequence)
            {
                if (entity is MappingDataNode mapping)
                    yield return mapping;
            }
        }
    }

    private static MappingDataNode? Component(MappingDataNode entity, string type)
    {
        if (!entity.TryGet<SequenceDataNode>("components", out var components))
            return null;

        foreach (var node in components.Sequence)
        {
            if (node is MappingDataNode component &&
                component.TryGet<ValueDataNode>("type", out var name) &&
                name.Value == type)
            {
                return component;
            }
        }

        return null;
    }

    private static Dictionary<int, string> ReadTileMap(MappingDataNode root)
    {
        var names = new Dictionary<int, string>();
        if (!root.TryGet<MappingDataNode>("tilemap", out var map))
            return names;

        foreach (var (key, value) in map.Children)
        {
            if (value is ValueDataNode name && int.TryParse(key, CultureInfo.InvariantCulture, out var id))
                names[id] = name.Value;
        }

        return names;
    }

    /// <summary>Adds the non-empty cells of a grid's chunks; the number added.</summary>
    private static int ReadChunks(MappingDataNode mapGrid, Dictionary<int, string> tileNames, List<Vector2i> floor)
    {
        if (!mapGrid.TryGet<MappingDataNode>("chunks", out var chunks))
            return 0;

        var added = 0;
        foreach (var (_, node) in chunks.Children)
        {
            if (node is not MappingDataNode chunk ||
                !chunk.TryGet<ValueDataNode>("ind", out var ind) ||
                !chunk.TryGet<ValueDataNode>("tiles", out var encoded))
            {
                continue;
            }

            var size = chunk.TryGet<ValueDataNode>("size", out var sizeNode) &&
                       int.TryParse(sizeNode.Value, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : DefaultChunkSize;
            var version = chunk.TryGet<ValueDataNode>("version", out var versionNode) &&
                          int.TryParse(versionNode.Value, CultureInfo.InvariantCulture, out var parsedVersion)
                ? parsedVersion
                : 1;
            var idBytes = version < 6 ? 2 : 4;
            var stride = idBytes + (version >= 7 ? 3 : 2);
            var data = Convert.FromBase64String(encoded.Value);
            if (size <= 0 || size > MaxChunkSize || data.Length < size * size * stride)
                continue;

            var origin = ReadPosition(ind.Value);
            var offset = 0;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++, offset += stride)
                {
                    var id = idBytes == 2 ? BitConverter.ToUInt16(data, offset) : BitConverter.ToInt32(data, offset);
                    if (!tileNames.TryGetValue(id, out var name) || name == EmptyTile)
                        continue;

                    floor.Add(new Vector2i((int) origin.X * size + x, (int) origin.Y * size + y));
                    added++;
                }
            }
        }

        return added;
    }

    private static Vector2 ReadPosition(MappingDataNode? transform)
    {
        return transform != null && transform.TryGet<ValueDataNode>("pos", out var pos)
            ? ReadPosition(pos.Value)
            : Vector2.Zero;
    }

    private static Vector2 ReadPosition(string text)
    {
        var comma = text.IndexOf(',');
        if (comma < 0 ||
            !float.TryParse(text[..comma], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
            !float.TryParse(text[(comma + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
        {
            return Vector2.Zero;
        }

        return new Vector2(x, y);
    }

    /// <summary>Reads a rotation saved as <c>1.57 rad</c>.</summary>
    private static Angle ReadRotation(MappingDataNode? transform)
    {
        if (transform == null || !transform.TryGet<ValueDataNode>("rot", out var rot))
            return Angle.Zero;

        var text = rot.Value;
        var space = text.IndexOf(' ');
        return double.TryParse(space < 0 ? text : text[..space], NumberStyles.Float, CultureInfo.InvariantCulture,
            out var radians)
            ? new Angle(radians)
            : Angle.Zero;
    }
}
