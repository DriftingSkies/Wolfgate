using System.Numerics;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Map.Components;

namespace Content.Client._WF.MappingMode;

/// <summary>
/// Shows the grid waiting on a delete confirmation: its tiles in red, its bounds outlined, and a box on everything
/// standing on it.
/// </summary>
public sealed partial class WFMappingDeletePreviewOverlay : Overlay
{
    [Dependency] private IEntityManager _entities = default!;

    private static readonly Color TileColor = Color.FromHex("#ff3030").WithAlpha(0.35f);
    private static readonly Color EdgeColor = Color.FromHex("#ff5050");
    private static readonly Color EntityColor = Color.FromHex("#ffb0b0").WithAlpha(0.8f);

    private readonly WFMappingClientSystem _system;
    private readonly SharedMapSystem _map;
    private readonly SharedTransformSystem _transform;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public WFMappingDeletePreviewOverlay(WFMappingClientSystem system)
    {
        IoCManager.InjectDependencies(this);
        _system = system;
        _map = _entities.System<SharedMapSystem>();
        _transform = _entities.System<SharedTransformSystem>();
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        return _system.PendingDelete is { } pending &&
               _entities.TryGetEntity(pending.Grid, out var grid) &&
               _entities.TryGetComponent<TransformComponent>(grid, out var xform) &&
               xform.MapID == args.MapId;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (_system.PendingDelete is not { } pending ||
            !_entities.TryGetEntity(pending.Grid, out var uid) ||
            !_entities.TryGetComponent<MapGridComponent>(uid, out var grid))
        {
            return;
        }

        var handle = args.WorldHandle;
        handle.SetTransform(_transform.GetWorldMatrix(uid.Value));

        var tiles = _map.GetAllTilesEnumerator(uid.Value, grid);
        while (tiles.MoveNext(out var tile))
        {
            var cell = tile.Value.GridIndices;
            handle.DrawRect(new Box2(cell, cell + Vector2i.One), TileColor);
        }

        handle.DrawRect(grid.LocalAABB, EdgeColor, false);

        var children = _entities.GetComponent<TransformComponent>(uid.Value).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            var position = _entities.GetComponent<TransformComponent>(child).LocalPosition;
            handle.DrawRect(Box2.CenteredAround(position, new Vector2(0.8f, 0.8f)), EntityColor, false);
        }

        handle.SetTransform(Matrix3x2.Identity);
    }
}
