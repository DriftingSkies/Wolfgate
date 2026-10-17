using Robust.Server.Physics;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.EngineCompat;

/// <summary>Records how a split-off grid's tile indices map back to the grid it came from.</summary>
public sealed class WFGridSplitOriginSystem : EntitySystem
{
    [Dependency] private SharedTransformSystem _transform = default!;

    /// <summary>The most splits followed back from a grid before giving up.</summary>
    private const int MaxDepth = 64;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PostGridSplitEvent>(OnPostSplit);
    }

    private void OnPostSplit(ref PostGridSplitEvent args)
    {
        var origin = EnsureComp<WFGridSplitOriginComponent>(args.Grid);
        origin.Source = args.OldGrid;
        origin.Offset = SplitOffset(args.OldGrid, args.Grid);
    }

    /// <summary>
    /// Added to a tile index on <paramref name="split"/>, gives its index on <paramref name="source"/>. Only valid
    /// during the split, while both grids still share a rotation and the engine's placement.
    /// </summary>
    public Vector2i SplitOffset(EntityUid source, EntityUid split)
    {
        var (origin, rotation) = _transform.GetWorldPositionRotation(source);
        var tileSize = CompOrNull<MapGridComponent>(source)?.TileSize ?? 1;
        var local = (-rotation).RotateVec(_transform.GetWorldPosition(split) - origin) / tileSize;
        return new Vector2i((int) MathF.Round(local.X), (int) MathF.Round(local.Y));
    }

    /// <summary>The offset from a grid's tile indices to an earlier grid it split off from, through any splits between.</summary>
    public bool TryGetOffset(EntityUid grid, EntityUid ancestor, out Vector2i offset)
    {
        offset = Vector2i.Zero;
        var current = grid;
        for (var depth = 0; current != ancestor; depth++)
        {
            if (depth >= MaxDepth || !TryComp<WFGridSplitOriginComponent>(current, out var origin))
                return false;

            offset += origin.Offset;
            current = origin.Source;
        }

        return true;
    }
}
