using System.Numerics;
using Content.Client.Graphics;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Graphics;
using Robust.Shared.Prototypes;

namespace Content.Client._WG.DisplayOptions;

/// <summary>
/// Light bloom: a soft glow around the brightest parts of the lit scene, such as lamps and glowing sprites.
/// </summary>
/// <remarks>
/// Per viewport, the lit screen is box-filtered to half size keeping only what is near full brightness, blurred across
/// and then down, and screen-blended back over the world. Every pass samples by fragment position, so no texture is
/// ever drawn as a quad and none of them can come out flipped.
/// </remarks>
public sealed partial class WGBloomOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> ExtractShader = "WGBloomExtract";
    private static readonly ProtoId<ShaderPrototype> BlurShader = "WGBloomBlur";
    private static readonly ProtoId<ShaderPrototype> CompositeShader = "WGBloomComposite";

    /// <summary>Spacing between blur taps, in half size texels. Wider spreads the glow further at the same cost.</summary>
    private const float BlurSpread = 2f;

    /// <summary>Glow gain at full strength; the default strength of 0.25 gives a quarter of it.</summary>
    private const float Gain = 6f;

    [Dependency] private IClyde _clyde = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;
    public override bool RequestScreenTexture => true;

    /// <summary>Bloom strength from 0 to 1, from <c>scp.light_bloom_strength</c>.</summary>
    public float Strength = 0.25f;

    private readonly ShaderInstance _extract;
    private readonly ShaderInstance _blurAcross;
    private readonly ShaderInstance _blurDown;
    private readonly ShaderInstance _composite;
    private readonly OverlayResourceCache<BloomTargets> _targets = new();

    public WGBloomOverlay()
    {
        IoCManager.InjectDependencies(this);

        _extract = _proto.Index(ExtractShader).InstanceUnique();
        _blurAcross = _proto.Index(BlurShader).InstanceUnique();
        _blurDown = _proto.Index(BlurShader).InstanceUnique();
        _composite = _proto.Index(CompositeShader).InstanceUnique();

        _blurAcross.SetParameter("direction", new Vector2(BlurSpread, 0f));
        _blurDown.SetParameter("direction", new Vector2(0f, BlurSpread));

        // Ahead of the other world-space overlays, so health bars, icons and labels drawn there don't glow.
        ZIndex = -100;
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        return Strength > 0f && args.Viewport.Size.X > 1 && args.Viewport.Size.Y > 1;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null)
            return;

        var size = args.Viewport.Size;
        var half = new Vector2i((size.X + 1) / 2, (size.Y + 1) / 2);
        var targets = _targets.GetForViewport(args.Viewport, static _ => new BloomTargets());

        if (targets.A is not { } a || targets.B is not { } b || a.Size != half)
        {
            targets.Dispose();
            a = CreateTarget(half, "wg-bloom-a");
            b = CreateTarget(half, "wg-bloom-b");
            targets.A = a;
            targets.B = b;
        }

        var handle = args.WorldHandle;
        var halfRect = new Box2(0f, 0f, half.X, half.Y);

        _extract.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        _extract.SetParameter("source_pixel_size", new Vector2(1f / size.X, 1f / size.Y));
        DrawPass(handle, a, _extract, halfRect);

        _blurAcross.SetParameter("bloom_texture", a.Texture);
        DrawPass(handle, b, _blurAcross, halfRect);

        _blurDown.SetParameter("bloom_texture", b.Texture);
        DrawPass(handle, a, _blurDown, halfRect);

        _composite.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        _composite.SetParameter("bloom_texture", a.Texture);
        _composite.SetParameter("bloom_pixel_size", new Vector2(1f / half.X, 1f / half.Y));
        _composite.SetParameter("strength", Strength * Gain);

        handle.SetTransform(Matrix3x2.Identity);
        handle.UseShader(_composite);
        handle.DrawRect(args.WorldAABB, Color.White);
        handle.UseShader(null);
    }

    /// <summary>
    /// Covers the whole of <paramref name="target"/> with <paramref name="shader"/>.
    /// </summary>
    private static void DrawPass(DrawingHandleWorld handle, IRenderTexture target, ShaderInstance shader, Box2 rect)
    {
        handle.RenderInRenderTarget(target,
            () =>
            {
                handle.SetTransform(Matrix3x2.Identity);
                handle.UseShader(shader);
                handle.DrawRect(rect, Color.White);
                handle.UseShader(null);
            },
            Color.Black);
    }

    private IRenderTexture CreateTarget(Vector2i size, string name)
    {
        return _clyde.CreateRenderTarget(size,
            new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb),
            new TextureSampleParameters { Filter = true },
            name);
    }

    protected override void DisposeBehavior()
    {
        _targets.Dispose();
        base.DisposeBehavior();
    }

    /// <summary>The two half size textures the passes bounce between, for one viewport.</summary>
    private sealed class BloomTargets : IDisposable
    {
        public IRenderTexture? A;
        public IRenderTexture? B;

        public void Dispose()
        {
            A?.Dispose();
            B?.Dispose();
            A = null;
            B = null;
        }
    }
}
