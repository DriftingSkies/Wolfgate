using System.Numerics;
using Content.Shared._WF.Warheads;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WF.Warheads;

/// <summary>
/// Draws each detonation's shockwave, which bends the screen as it passes, and its procedural fireball or, on a
/// planet, mushroom cloud. Also draws the haze over fallout.
/// </summary>
public sealed partial class WFNuclearBlastOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> BlastShader = "WFNuclearBlast";
    private static readonly ProtoId<ShaderPrototype> FalloutShader = "WFFalloutHaze";

    [Dependency] private IEntityManager _entMan = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;
    public override bool RequestScreenTexture => true;

    private readonly WFWarheadEffectsSystem _effects;
    private readonly SharedTransformSystem _transform;
    private readonly ShaderInstance _blast;
    private readonly ShaderInstance _fallout;

    /// <summary>How far above ground zero the mushroom cloud reaches, in fireball radii.</summary>
    private const float CloudHeight = 7f;

    public WFNuclearBlastOverlay()
    {
        IoCManager.InjectDependencies(this);
        _effects = _entMan.System<WFWarheadEffectsSystem>();
        _transform = _entMan.System<SharedTransformSystem>();
        _blast = _proto.Index(BlastShader).InstanceUnique();
        _fallout = _proto.Index(FalloutShader).InstanceUnique();
        ZIndex = 200;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;
        var viewport = args.Viewport;

        DrawFallout(args);

        if (ScreenTexture == null)
            return;

        foreach (var det in _effects.Active)
        {
            if (det.MapId != args.MapId)
                continue;

            var ev = det.Event;
            var age = _effects.Age(det);
            var reach = Math.Max(ev.ShockwaveRange, ev.FireballRadius * (CloudHeight + 2f));
            var bounds = Box2.CenteredAround(ev.Position, new Vector2(reach * 2f));

            if (!bounds.Intersects(args.WorldAABB))
                continue;

            var center = viewport.WorldToLocal(ev.Position);
            var pixelsPerTile = (viewport.WorldToLocal(ev.Position + Vector2.UnitX) - center).Length();
            center.Y = viewport.Size.Y - center.Y;

            _blast.SetParameter("SCREEN_TEXTURE", ScreenTexture);
            _blast.SetParameter("center", center);
            _blast.SetParameter("pxPerTile", pixelsPerTile);
            _blast.SetParameter("age", age);
            _blast.SetParameter("fireball", Math.Max(ev.FireballRadius, 0.1f));
            _blast.SetParameter("shockRange", ev.ShockwaveRange);
            _blast.SetParameter("shockSpeed", ev.ShockwaveSpeed);
            _blast.SetParameter("duration", Math.Max(ev.Duration, 0.1f));
            _blast.SetParameter("mushroom", ev.Mushroom ? 1f : 0f);
            _blast.SetParameter("seed", det.Seed * 100f);

            handle.UseShader(_blast);
            handle.DrawRect(bounds.Intersect(args.WorldAABB), Color.White);
        }

        handle.UseShader(null);
    }

    private void DrawFallout(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;
        var now = _timing.CurTime;
        var drew = false;

        var query = _entMan.EntityQueryEnumerator<WFFalloutComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var fallout, out var xform))
        {
            if (xform.MapID != args.MapId)
                continue;

            var pos = _transform.GetWorldPosition(xform);
            var bounds = Box2.CenteredAround(pos, new Vector2(fallout.Radius * 2f));
            if (!bounds.Intersects(args.WorldAABB))
                continue;

            if (!drew)
            {
                handle.UseShader(_fallout);
                drew = true;
            }

            // Strength rides in the colour's alpha; the shader shapes it into a drifting haze.
            var strength = Math.Clamp(fallout.Fraction(now), 0f, 1f);
            handle.DrawTextureRect(Texture.White, bounds, new Color(0.45f, 1f, 0.2f, strength));
        }

        if (drew)
            handle.UseShader(null);
    }
}
