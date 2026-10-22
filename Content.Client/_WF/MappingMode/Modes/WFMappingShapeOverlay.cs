using System.Numerics;
using Robust.Client.Graphics;
using Robust.Shared.Enums;

namespace Content.Client._WF.MappingMode.Modes;

/// <summary>Shades the cells the current build mode is about to change: a drag's rectangle or shape, or the hovered cell.</summary>
public sealed partial class WFMappingShapeOverlay : Overlay
{
    [Dependency] private IEntityManager _entities = default!;

    private readonly WFMappingBuildModeSystem _modes;
    private readonly SharedTransformSystem _transform;
    private readonly List<Vector2i> _cells = new();

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public WFMappingShapeOverlay(WFMappingBuildModeSystem modes)
    {
        IoCManager.InjectDependencies(this);
        _modes = modes;
        _transform = _entities.System<SharedTransformSystem>();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (!_modes.Active ||
            _modes.Handler is not IWFMappingPreviewSource source ||
            !source.TryGetPreview(_cells, out var grid, out var box, out var color) ||
            !_entities.TryGetComponent(grid, out TransformComponent? xform) ||
            xform.MapID != args.MapId)
        {
            return;
        }

        var handle = args.WorldHandle;
        handle.SetTransform(_transform.GetWorldMatrix(grid));

        if (box is { } shaded)
        {
            var rect = new Box2(shaded.Left, shaded.Bottom, shaded.Right, shaded.Top);
            handle.DrawRect(rect, color.WithAlpha(0.25f));
            handle.DrawRect(rect, color, filled: false);
        }

        foreach (var cell in _cells)
        {
            var rect = new Box2(cell.X, cell.Y, cell.X + 1, cell.Y + 1);
            handle.DrawRect(rect, color.WithAlpha(0.35f));
            handle.DrawRect(rect, color.WithAlpha(0.8f), filled: false);
        }

        handle.SetTransform(Matrix3x2.Identity);
    }
}
