using System.Numerics;
using Content.Shared._WF.MappingMode;
using Robust.Client.Graphics;
using Robust.Shared.Enums;

namespace Content.Client._WF.MappingMode;

/// <summary>
/// Draws the reference grid being placed: each grid's tile rectangles and the whole footprint's bounding box at the
/// cursor, red when it would cross the map limit.
/// </summary>
public sealed class WFMappingReferenceOverlay : Overlay
{
    private static readonly Color GhostColor = Color.FromHex("#4ad9ff");
    private static readonly Color BoxColor = Color.FromHex("#ffd24a");
    private static readonly Color RefusedColor = Color.FromHex("#ff4a4a");

    private readonly WFMappingReferenceSystem _system;
    private readonly WFMappingClientSystem _mapping;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public WFMappingReferenceOverlay(WFMappingReferenceSystem system)
    {
        _system = system;
        _mapping = IoCManager.Resolve<IEntityManager>().System<WFMappingClientSystem>();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (_system.Footprint is not { } footprint ||
            !_system.TryGetAnchor(out var anchor) ||
            anchor.MapId != args.MapId)
        {
            return;
        }

        var turns = _system.Turns;
        var bounds = WFMappingFootprint.Bounds(footprint.Grids, turns).Translated(anchor.Position);
        var half = _mapping.MapSize / 2f;
        var inside = bounds.Left >= -half && bounds.Bottom >= -half && bounds.Right <= half && bounds.Top <= half;
        var ghost = inside ? GhostColor : RefusedColor;

        var handle = args.WorldHandle;
        foreach (var grid in footprint.Grids)
        {
            var (position, rotation) = WFMappingFootprint.Place(grid, anchor.Position, turns);
            handle.SetTransform(Matrix3Helpers.CreateTransform(position, rotation));

            foreach (var rect in grid.Rects)
            {
                handle.DrawRect(new Box2(rect.BottomLeft, rect.TopRight), ghost.WithAlpha(0.3f));
            }
        }

        handle.SetTransform(Matrix3x2.Identity);
        handle.DrawRect(bounds, (inside ? BoxColor : RefusedColor).WithAlpha(0.08f));
        handle.DrawRect(bounds, inside ? BoxColor : RefusedColor, filled: false);
    }
}
