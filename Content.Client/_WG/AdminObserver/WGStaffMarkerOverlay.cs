using System.Numerics;
using Content.Shared._WG.AdminObserver;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Graphics.RSI;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WG.AdminObserver;

/// <summary>
/// Draws the staff marker's ring under each marked entity, and its shield and glyph over it. Everything is unlit
/// and upright on the screen, in the marker's colour.
/// </summary>
public sealed class WGStaffMarkerOverlay : Overlay
{
    private readonly IEntityManager _entities;
    private readonly IPrototypeManager _prototypes;
    private readonly IGameTiming _timing;
    private readonly SpriteSystem _sprite;
    private readonly SharedTransformSystem _transform;
    private readonly ShaderInstance _tint;
    private readonly ShaderInstance _tintSolid;

    private static readonly ProtoId<ShaderPrototype> TintShader = "WGStaffMarkerTint";
    private static readonly ProtoId<ShaderPrototype> TintSolidShader = "WGStaffMarkerTintSolid";

    /// <summary>How far from the entity's middle a glyph circles, in tiles, and how flat its path looks.</summary>
    private const float OrbitRadius = 0.65f;
    private const float OrbitFlatness = 0.45f;

    /// <summary>Seconds a glyph takes to go round once.</summary>
    private const float OrbitPeriod = 4f;

    private const float ShieldAlpha = 0.6f;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowEntities | OverlaySpace.WorldSpaceBelowFOV;

    public WGStaffMarkerOverlay(
        IEntityManager entities,
        IPrototypeManager prototypes,
        IGameTiming timing,
        SpriteSystem sprite,
        SharedTransformSystem transform)
    {
        _entities = entities;
        _prototypes = prototypes;
        _timing = timing;
        _sprite = sprite;
        _transform = transform;
        _tint = prototypes.Index(TintShader).Instance();
        _tintSolid = prototypes.Index(TintSolidShader).Instance();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (args.MapId == MapId.Nullspace)
            return;

        var handle = args.WorldHandle;
        var below = args.Space == OverlaySpace.WorldSpaceBelowEntities;
        var time = _timing.RealTime;
        var upright = Matrix3Helpers.CreateRotation(-(args.Viewport.Eye?.Rotation ?? Angle.Zero));

        var query = _entities.EntityQueryEnumerator<WGStaffMarkerComponent, SpriteComponent, TransformComponent, MetaDataComponent>();
        while (query.MoveNext(out var uid, out var marker, out var sprite, out var xform, out var meta))
        {
            if (xform.MapID != args.MapId || !sprite.Visible || (meta.Flags & MetaDataFlags.InContainer) != 0)
                continue;

            var position = _transform.GetWorldPosition(xform);
            if (!args.WorldAABB.Enlarged(2f).Contains(position))
                continue;

            handle.SetTransform(Matrix3x2.Multiply(upright, Matrix3Helpers.CreateTranslation(position)));

            if (below && Variant(marker, WGMarkerVisuals.Ring, marker.Ring) is { } ring)
                DrawSprite(handle, ring, time, Vector2.Zero, marker.Color);

            if (!below && Variant(marker, WGMarkerVisuals.Shield, marker.Shield) is { } shield)
            {
                if (shield.Kind == WGMarkerVariantKind.Sprite)
                {
                    DrawSprite(handle, shield, time, Vector2.Zero, marker.Color.WithAlpha(ShieldAlpha));
                }
                else if (_entities.TryGetComponent(uid, out WGStaffMarkerVisualsComponent? visuals) && visuals.Skin != null)
                {
                    handle.UseShader(visuals.Skin);
                    handle.DrawTextureRect(Texture.White, Box2.CenteredAround(Vector2.Zero, new Vector2(shield.Size, shield.Size)));
                }
            }

            if (Variant(marker, WGMarkerVisuals.Glyph, marker.Glyph) is { } glyph)
            {
                // Each glyph starts its circle somewhere of its own, and passes behind the avatar on the far half.
                var turn = (time.TotalSeconds / OrbitPeriod + uid.Id * 0.37) % 1d * Math.Tau;
                var behind = glyph.Orbit && Math.Sin(turn) > 0d;
                if (behind == below)
                {
                    var offset = glyph.Orbit
                        ? new Vector2((float) Math.Cos(turn), (float) Math.Sin(turn) * OrbitFlatness) * OrbitRadius
                        : Vector2.Zero;
                    DrawSprite(handle, glyph, time, offset, marker.Color);
                }
            }
        }

        handle.SetTransform(Matrix3x2.Identity);
        handle.UseShader(null);
    }

    private WGStaffMarkerVariantPrototype? Variant(WGStaffMarkerComponent marker, WGMarkerVisuals visual, string id)
    {
        if ((marker.Visuals & visual) == 0)
            return null;

        return _prototypes.TryIndex<WGStaffMarkerVariantPrototype>(id, out var variant) ? variant : null;
    }

    private void DrawSprite(DrawingHandleWorld handle, WGStaffMarkerVariantPrototype variant, TimeSpan time, Vector2 offset, Color color)
    {
        if (variant.Sprite == null)
            return;

        var texture = Frame(variant, time);
        var size = new Vector2(variant.Size, variant.Size * texture.Height / Math.Max(texture.Width, 1));
        handle.UseShader(variant.Solid ? _tintSolid : _tint);
        handle.DrawTextureRect(texture, Box2.CenteredAround(offset, size), color);
    }

    /// <summary>The frame of a variant's sprite at a time, looping, played backwards for a reversed variant.</summary>
    private Texture Frame(WGStaffMarkerVariantPrototype variant, TimeSpan time)
    {
        var state = _sprite.RsiStateLike(variant.Sprite!);
        var count = state.AnimationFrameCount;
        if (count <= 1)
            return state.GetFrame(RsiDirection.South, 0);

        var total = 0f;
        for (var i = 0; i < count; i++)
        {
            total += state.GetDelay(i);
        }

        if (total <= 0f)
            return state.GetFrame(RsiDirection.South, 0);

        var at = (float) (time.TotalSeconds % total);
        var frame = count - 1;
        for (var i = 0; i < count; i++)
        {
            at -= state.GetDelay(i);
            if (at >= 0f)
                continue;

            frame = i;
            break;
        }

        return state.GetFrame(RsiDirection.South, variant.Reversed ? count - 1 - frame : frame);
    }
}
