using System.Numerics;
using Content.Shared.Examine;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Utility;

namespace Content.Client._WF.ExtraInteractions;

/// <summary>
/// Draws each shown item inside a thought bubble up and to the right of whoever shows it, as SS13 did.
/// </summary>
public sealed class WFShowItemOverlay : Overlay
{
    // Above lighting, like the speech bubbles, so it reads in the dark.
    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    private const float Alpha = 200f / 255f;
    private const float ItemScale = 0.7f;

    /// <summary>Bubble centre from the mob's, in screen-aligned tiles.</summary>
    private static readonly Vector2 BubbleOffset = new(0.5f, 1f);

    /// <summary>Item centre from the bubble's: the cloud's trail takes its bottom rows.</summary>
    private static readonly Vector2 ItemOffset = new(0f, 2f / 32f);

    private static readonly SpriteSpecifier BubbleSprite =
        new SpriteSpecifier.Rsi(new ResPath("/Textures/_WF/ExtraInteractions/thought_bubble.rsi"), "bubble");

    private readonly IEntityManager _entMan;
    private readonly IPlayerManager _player;
    private readonly SpriteSystem _sprite;
    private readonly SharedTransformSystem _transform;
    private readonly ExamineSystemShared _examine;
    private readonly WFShowItemSystem _show;

    public WFShowItemOverlay(IEntityManager entMan)
    {
        _entMan = entMan;
        _player = IoCManager.Resolve<IPlayerManager>();
        _sprite = entMan.System<SpriteSystem>();
        _transform = entMan.System<SharedTransformSystem>();
        _examine = entMan.System<ExamineSystemShared>();
        _show = entMan.System<WFShowItemSystem>();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (_show.Bubbles.Count == 0)
            return;

        var eye = args.Viewport.Eye;
        var eyeRot = eye?.Rotation ?? Angle.Zero;
        var screenRot = -eyeRot;
        var viewer = _player.LocalEntity;
        var bubbleTexture = _sprite.Frame0(BubbleSprite);
        var color = Color.White.WithAlpha(Alpha);

        foreach (var (user, bubble) in _show.Bubbles)
        {
            if (!_entMan.TryGetComponent(user, out TransformComponent? xform) || xform.MapID != args.MapId)
                continue;

            if (eye is { DrawFov: true } && viewer != null && viewer != user
                && !_examine.InRangeUnOccluded(viewer.Value, user, ExamineSystemShared.MaxRaycastRange))
                continue;

            var center = _transform.GetWorldPosition(xform) + screenRot.RotateVec(BubbleOffset);
            if (!args.WorldAABB.Enlarged(1f).Contains(center))
                continue;

            args.WorldHandle.DrawTextureRect(bubbleTexture,
                new Box2Rotated(Box2.CenteredAround(center, Vector2.One), screenRot, center),
                color);

            var itemCenter = center + screenRot.RotateVec(ItemOffset);
            if (_entMan.TryGetComponent(bubble.Item, out SpriteComponent? sprite))
            {
                var item = bubble.Item.Value;
                var (scale, spriteColor) = (sprite.Scale, sprite.Color);
                _sprite.SetScale((item, sprite), scale * ItemScale);
                _sprite.SetColor((item, sprite), spriteColor.WithAlpha(spriteColor.A * Alpha));
                _sprite.RenderSprite((item, sprite), args.WorldHandle, eyeRot, screenRot, itemCenter);
                _sprite.SetScale((item, sprite), scale);
                _sprite.SetColor((item, sprite), spriteColor);
            }
            else if (bubble.Prototype is { } prototype)
            {
                var icon = _sprite.GetPrototypeIcon(prototype).Default;
                var size = (Vector2) icon.Size / EyeManager.PixelsPerMeter * ItemScale;
                args.WorldHandle.DrawTextureRect(icon,
                    new Box2Rotated(Box2.CenteredAround(itemCenter, size), screenRot, itemCenter),
                    color);
            }
        }
    }
}
