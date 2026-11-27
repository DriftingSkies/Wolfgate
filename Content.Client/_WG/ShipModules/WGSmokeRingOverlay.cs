using System.Numerics;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Graphics.RSI;
using Robust.Shared.Utility;

namespace Content.Client._WG.ShipModules;

/// <summary>Draws the jets' smoke rings, each turned to stand across its thruster's exhaust, growing and fading.</summary>
public sealed class WGSmokeRingOverlay : Overlay
{
    private static readonly SpriteSpecifier.Rsi RingSprite =
        new(new ResPath("/Textures/_WG/ShipModules/smoke_ring.rsi"), "ring");

    private static readonly Color Tint = Color.FromHex("#E6EBF0");

    private readonly WGManeuveringJetsVisualsSystem _jets;
    private readonly Texture[] _frames;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

    public WGSmokeRingOverlay(WGManeuveringJetsVisualsSystem jets, SpriteSystem sprite)
    {
        _jets = jets;
        _frames = sprite.GetState(RingSprite).GetFrames(RsiDirection.South);
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        return _jets.Rings.Count > 0;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;
        foreach (var ring in _jets.Rings)
        {
            if (ring.Map != args.MapId)
                continue;

            var t = ring.Age / WGSmokeRing.Life;
            var size = ring.Size * (0.9f + 1.1f * t);
            var position = ring.Position;
            var box = Box2.CenteredAround(position, new Vector2(size, size));
            if (!args.WorldAABB.Intersects(box))
                continue;

            // The texture is drawn with its exhaust pointing south, which ToWorldAngle puts at zero.
            var frame = _frames[Math.Min(_frames.Length - 1, (int) (t * _frames.Length))];
            var alpha = ring.Opacity * (1f - t * t);
            handle.DrawTextureRect(frame, new Box2Rotated(box, ring.Exhaust.ToWorldAngle(), position), Tint.WithAlpha(alpha));
        }
    }
}
