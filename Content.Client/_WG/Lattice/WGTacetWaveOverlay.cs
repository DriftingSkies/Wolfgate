using System.Numerics;
using Content.Shared._WG.Lattice;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;

namespace Content.Client._WG.Lattice;

/// <summary>Draws Kyphrus's shockwaves running out across the sector after it bursts, bending space as they pass.</summary>
public sealed partial class WGTacetWaveOverlay : Overlay
{
    [Dependency] private IEntityManager _ent = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    private static readonly ProtoId<ShaderPrototype> Shader = "WGTacetShockwave";

    private readonly ShaderInstance _shader;
    private Vector4 _radii;
    private float _reach;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public override bool RequestScreenTexture => true;

    public WGTacetWaveOverlay()
    {
        IoCManager.InjectDependencies(this);
        ZIndex = 190;
        _shader = _proto.Index(Shader).InstanceUnique();
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (_ent.System<WGLatticeViewSystem>().ImplosionSeconds(args.MapUid, out var field) is not { } seconds
            || field == null || seconds < WGLatticeImplosion.Burst)
            return false;

        _reach = field.WaveReach;
        // Only when a front is somewhere in the view.
        var near = Vector2.Distance(Vector2.Zero, args.WorldAABB.ClosestPoint(Vector2.Zero));
        var far = 0f;
        foreach (var corner in new[] { args.WorldAABB.BottomLeft, args.WorldAABB.BottomRight, args.WorldAABB.TopLeft, args.WorldAABB.TopRight })
        {
            far = MathF.Max(far, corner.Length());
        }

        _radii = new Vector4(WGLatticeImplosion.WaveRadius(seconds, 0), WGLatticeImplosion.WaveRadius(seconds, 1),
            WGLatticeImplosion.WaveRadius(seconds, 2), WGLatticeImplosion.WaveRadius(seconds, 3));
        return InView(_radii.X, near, far) || InView(_radii.Y, near, far) || InView(_radii.Z, near, far) || InView(_radii.W, near, far);
    }

    /// <summary>Whether a front at this radius, with its wake, reaches between the view's nearest and farthest points.</summary>
    private bool InView(float radius, float near, float far)
    {
        var pad = WGLatticeImplosion.WaveWidth * 3f;
        return radius > 0f && radius <= _reach && radius + pad >= near && radius - pad * 4f <= far;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null)
            return;

        var origin = args.Viewport.WorldToLocal(Vector2.Zero);
        var right = args.Viewport.WorldToLocal(Vector2.UnitX) - origin;
        var up = args.Viewport.WorldToLocal(Vector2.UnitY) - origin;
        right.Y = -right.Y;
        up.Y = -up.Y;

        _shader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        _shader.SetParameter("viewportMin", args.WorldAABB.BottomLeft);
        _shader.SetParameter("viewportSize", args.WorldAABB.Size);
        _shader.SetParameter("worldToScreenX", right);
        _shader.SetParameter("worldToScreenY", up);
        _shader.SetParameter("radii", _radii);
        _shader.SetParameter("width", WGLatticeImplosion.WaveWidth);
        _shader.SetParameter("reach", _reach);

        args.WorldHandle.UseShader(_shader);
        args.WorldHandle.DrawRect(args.WorldAABB, Color.White);
        args.WorldHandle.UseShader(null);
    }
}
