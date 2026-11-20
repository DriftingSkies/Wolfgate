using System.Numerics;
using Content.Shared.Construction;
using Content.Shared.Construction.Conditions;
using Content.Shared.Tag;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Shared._WG.Paintings;

/// <summary>
/// Construction condition: no other frame where this one would hang. A large frame spans its wall tile and the one
/// east of it, so it needs that second wall tile, on a wall facing north or south.
/// </summary>
[DataDefinition]
public sealed partial class WGPaintingFrameSpace : IConstructionCondition
{
    private static readonly ProtoId<TagPrototype> WallTag = "Wall";

    [DataField]
    public bool Large;

    public ConstructionGuideEntry GenerateGuideEntry()
    {
        return new ConstructionGuideEntry
        {
            Localization = Large ? "wg-painting-construction-large-frame-space" : "wg-painting-construction-frame-space",
        };
    }

    public bool Condition(EntityUid user, EntityCoordinates location, Direction direction)
    {
        var entMan = IoCManager.Resolve<IEntityManager>();
        if (entMan.System<SharedTransformSystem>().GetGrid(location) is not { } gridUid
            || !entMan.TryGetComponent<MapGridComponent>(gridUid, out var grid))
            return false;

        var maps = entMan.System<SharedMapSystem>();
        var tile = maps.TileIndicesFor(gridUid, grid, location);
        if (Large && (direction is not (Direction.North or Direction.South)
            || !HasWall(entMan, maps, (gridUid, grid), tile + new Vector2i(1, 0))))
            return false;

        var centre = new Vector2(tile.X + (Large ? 1f : 0.5f), tile.Y + 0.5f);
        var half = HalfWidth(Large);
        var lookup = entMan.System<EntityLookupSystem>();
        foreach (var frame in lookup.GetEntitiesInRange<WGPaintingFrameComponent>(new EntityCoordinates(gridUid, centre), 2.5f))
        {
            var xform = entMan.GetComponent<TransformComponent>(frame);
            if (xform.ParentUid != gridUid)
                continue;

            var offset = xform.LocalPosition - centre;
            if (MathF.Abs(offset.Y) < 0.5f && MathF.Abs(offset.X) < half + HalfWidth(frame.Comp.Large) - 0.01f)
                return false;
        }

        return true;
    }

    /// <summary>Half the width of wall a frame takes, in tiles.</summary>
    private static float HalfWidth(bool large)
    {
        return large ? 1f : 0.5f;
    }

    private static bool HasWall(IEntityManager entMan, SharedMapSystem maps, Entity<MapGridComponent> grid, Vector2i tile)
    {
        var tags = entMan.System<TagSystem>();
        var anchored = maps.GetAnchoredEntitiesEnumerator(grid.Owner, grid.Comp, tile);
        while (anchored.MoveNext(out var uid))
        {
            if (tags.HasTag(uid.Value, WallTag))
                return true;
        }

        return false;
    }
}
