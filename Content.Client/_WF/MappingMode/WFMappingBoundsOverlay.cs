using System.Numerics;
using Content.Client.Resources;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Shared.Enums;

namespace Content.Client._WF.MappingMode;

/// <summary>
/// Draws the session's map limit while mapping: a dashed square of <c>map_size</c> tiles round the origin, a label
/// at each corner, and a tint over everything outside it.
/// </summary>
public sealed partial class WFMappingBoundsOverlay : Overlay
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IPlayerManager _player = default!;

    private static readonly Color BorderColor = Color.FromHex("#ffb347");
    private static readonly Color OutsideColor = Color.FromHex("#300000").WithAlpha(0.35f);

    private const float Dash = 1f;
    private const float DashWidth = 0.12f;

    private readonly WFMappingClientSystem _system;
    private readonly Font _font;
    private readonly Vector2[] _corners = new Vector2[4];

    public override OverlaySpace Space => OverlaySpace.WorldSpace | OverlaySpace.ScreenSpace;

    public WFMappingBoundsOverlay(WFMappingClientSystem system)
    {
        IoCManager.InjectDependencies(this);
        _system = system;
        _font = IoCManager.Resolve<IResourceCache>().GetFont("/Fonts/NotoSans/NotoSans-Regular.ttf", 12);
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        return _system.InSession &&
               _player.LocalEntity is { } local &&
               _entities.GetComponent<TransformComponent>(local).MapID == args.MapId;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var half = _system.MapSize / 2f;
        var bounds = new Box2(-half, -half, half, half);

        if (args.Space == OverlaySpace.ScreenSpace)
        {
            DrawLabels(args, bounds);
            return;
        }

        var handle = args.WorldHandle;
        DrawOutside(handle, args.WorldAABB, bounds);
        DrawDashes(handle, bounds.BottomLeft, bounds.BottomRight);
        DrawDashes(handle, bounds.BottomRight, bounds.TopRight);
        DrawDashes(handle, bounds.TopRight, bounds.TopLeft);
        DrawDashes(handle, bounds.TopLeft, bounds.BottomLeft);
    }

    /// <summary>Tints the visible area outside the bounds, as up to four rectangles.</summary>
    private static void DrawOutside(DrawingHandleWorld handle, Box2 view, Box2 bounds)
    {
        if (view.Bottom < bounds.Bottom)
            handle.DrawRect(new Box2(view.Left, view.Bottom, view.Right, MathF.Min(bounds.Bottom, view.Top)), OutsideColor);

        if (view.Top > bounds.Top)
            handle.DrawRect(new Box2(view.Left, MathF.Max(bounds.Top, view.Bottom), view.Right, view.Top), OutsideColor);

        var bottom = MathF.Max(view.Bottom, bounds.Bottom);
        var top = MathF.Min(view.Top, bounds.Top);
        if (top <= bottom)
            return;

        if (view.Left < bounds.Left)
            handle.DrawRect(new Box2(view.Left, bottom, MathF.Min(bounds.Left, view.Right), top), OutsideColor);

        if (view.Right > bounds.Right)
            handle.DrawRect(new Box2(MathF.Max(bounds.Right, view.Left), bottom, view.Right, top), OutsideColor);
    }

    private void DrawDashes(DrawingHandleWorld handle, Vector2 from, Vector2 to)
    {
        var length = (to - from).Length();
        var direction = (to - from) / length;
        var across = new Vector2(-direction.Y, direction.X) * (DashWidth / 2f);

        for (var at = 0f; at < length; at += Dash * 2f)
        {
            var start = from + direction * at;
            var end = from + direction * MathF.Min(at + Dash, length);
            handle.DrawLine(start, end, BorderColor);

            _corners[0] = start - across;
            _corners[1] = end - across;
            _corners[2] = end + across;
            _corners[3] = start + across;
            handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, _corners, BorderColor.WithAlpha(0.8f));
        }
    }

    private void DrawLabels(in OverlayDrawArgs args, Box2 bounds)
    {
        if (args.ViewportControl is not { } viewport)
            return;

        var text = Loc.GetString("wf-mapping-mode-bounds-label", ("size", _system.MapSize));
        var size = args.ScreenHandle.GetDimensions(_font, text, 1f);
        var matrix = viewport.GetWorldToScreenMatrix();

        foreach (var (corner, inward) in new[]
                 {
                     (bounds.TopLeft, new Vector2(4f, 4f)),
                     (bounds.TopRight, new Vector2(-4f - size.X, 4f)),
                     (bounds.BottomLeft, new Vector2(4f, -4f - size.Y)),
                     (bounds.BottomRight, new Vector2(-4f - size.X, -4f - size.Y)),
                 })
        {
            var screen = Vector2.Transform(corner, matrix);
            args.ScreenHandle.DrawString(_font, screen + inward, text, BorderColor);
        }
    }
}
