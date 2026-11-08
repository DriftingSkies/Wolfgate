using Content.Shared._WF.Outposts;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Client._WF.Outposts;

/// <summary>Draws the outline of a saved outpost on its way down, tile by tile, pulsing faster as it nears landing.</summary>
public sealed class WFOutpostPendingLoadOverlay : Overlay
{
    private static readonly Color Fill = Color.FromHex("#EFB341");

    private readonly IEntityManager _entities;
    private readonly SharedTransformSystem _transform;
    private readonly IGameTiming _timing;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

    public WFOutpostPendingLoadOverlay(IEntityManager entities, IGameTiming timing)
    {
        _entities = entities;
        _transform = entities.System<SharedTransformSystem>();
        _timing = timing;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (args.MapId == MapId.Nullspace)
            return;

        var handle = args.WorldHandle;
        var query = _entities.EntityQueryEnumerator<WFOutpostPendingLoadComponent, TransformComponent>();
        while (query.MoveNext(out var pending, out var xform))
        {
            if (xform.MapID != args.MapId)
                continue;

            var left = (float) Math.Max(0, (pending.Lands - _timing.CurTime).TotalSeconds);
            var rate = left < 5f ? 6f : 2f;
            var pulse = 0.5f + 0.5f * MathF.Sin((float) _timing.RealTime.TotalSeconds * rate);
            var fill = Fill.WithAlpha(0.12f + 0.12f * pulse);
            var edge = Fill.WithAlpha(0.6f);

            handle.SetTransform(_transform.GetWorldMatrix(xform));
            foreach (var tile in pending.Tiles)
            {
                var box = Box2.FromDimensions(tile, Vector2i.One);
                handle.DrawRect(box, fill);
                handle.DrawRect(box, edge, filled: false);
            }
        }

        handle.SetTransform(System.Numerics.Matrix3x2.Identity);
    }
}
