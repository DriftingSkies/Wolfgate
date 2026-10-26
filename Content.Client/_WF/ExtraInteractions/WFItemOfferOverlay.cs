using System.Numerics;
using Content.Shared._WF.ExtraInteractions;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Timing;

namespace Content.Client._WF.ExtraInteractions;

/// <summary>
/// A translucent, gently bobbing copy of each offered item, half a tile in front of whoever holds it out.
/// </summary>
public sealed class WFItemOfferOverlay : Overlay
{
    // Under FOV and lighting, like the item itself would be.
    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

    private const float Alpha = 0.6f;
    private const float BobHeight = 0.04f;
    private const float BobSpeed = 3f;

    private readonly IEntityManager _entMan;
    private readonly IGameTiming _timing;
    private readonly SpriteSystem _sprite;
    private readonly WFItemOfferSystem _offer;

    public WFItemOfferOverlay(IEntityManager entMan)
    {
        _entMan = entMan;
        _timing = IoCManager.Resolve<IGameTiming>();
        _sprite = entMan.System<SpriteSystem>();
        _offer = entMan.System<WFItemOfferSystem>();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var eyeRot = args.Viewport.Eye?.Rotation ?? Angle.Zero;
        var bob = (-eyeRot).RotateVec(new Vector2(0f, MathF.Sin((float) _timing.RealTime.TotalSeconds * BobSpeed) * BobHeight));
        var bounds = args.WorldAABB.Enlarged(1f);

        var query = _entMan.EntityQueryEnumerator<WFItemOfferComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var offer, out var xform))
        {
            if (xform.MapID != args.MapId || !_entMan.TryGetComponent(offer.Item, out SpriteComponent? sprite))
                continue;

            var position = _offer.GetIconPosition(uid) + bob;
            if (!bounds.Contains(position))
                continue;

            var item = offer.Item.Value;
            var color = sprite.Color;
            _sprite.SetColor((item, sprite), color.WithAlpha(color.A * Alpha));
            _sprite.RenderSprite((item, sprite), args.WorldHandle, eyeRot, -eyeRot, position);
            _sprite.SetColor((item, sprite), color);
        }
    }
}
