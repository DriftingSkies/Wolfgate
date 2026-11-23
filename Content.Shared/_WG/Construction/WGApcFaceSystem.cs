using Content.Shared.Tag;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Shared._WG.Construction;

/// <summary>Keeps APCs to one per wall face, for the APC recipe and the RCD.</summary>
public sealed class WGApcFaceSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private TagSystem _tag = default!;

    private static readonly ProtoId<TagPrototype> ApcTag = "WGApc";

    /// <summary>Whether an entity prototype is an APC or APC frame.</summary>
    public bool IsApc(string? prototype)
    {
        return prototype != null
            && _prototype.TryIndex<EntityPrototype>(prototype, out var proto)
            && proto.TryComp<TagComponent>(out var tags, Factory)
            && _tag.HasTag(tags, ApcTag);
    }

    /// <summary>Whether an APC or APC frame on the tile already faces this way.</summary>
    public bool IsTaken(EntityCoordinates location, Direction direction)
    {
        return _transform.GetGrid(location) is { } gridUid
            && TryComp<MapGridComponent>(gridUid, out var grid)
            && IsTaken((gridUid, grid), _map.TileIndicesFor(gridUid, grid, location), direction);
    }

    /// <inheritdoc cref="IsTaken(EntityCoordinates, Direction)"/>
    public bool IsTaken(Entity<MapGridComponent> grid, Vector2i tile, Direction direction)
    {
        var anchored = _map.GetAnchoredEntities(grid.Owner, grid.Comp, tile);
        while (anchored.MoveNext(out var uid))
        {
            if (_tag.HasTag(uid.Value, ApcTag) && Transform(uid.Value).LocalRotation.GetCardinalDir() == direction)
                return true;
        }

        return false;
    }
}
