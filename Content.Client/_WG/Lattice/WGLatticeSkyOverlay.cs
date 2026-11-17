using System.Numerics;
using Content.Client.Parallax;
using Content.Shared._WG.Lattice;
using Content.Shared.CCVar;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WG.Lattice;

/// <summary>
/// Regrades the space background by how far out of the Lattice's hold the view is: the Slack drains it, Adrift turns
/// it sick and swimming, Untethered red and dark, and the Deep nearly black, with veins, a heartbeat and things looking back.
/// </summary>
public sealed class WGLatticeSkyOverlay : Overlay
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IEntityManager _ent = default!;
    [Dependency] private IEyeManager _eye = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    private static readonly ProtoId<ShaderPrototype> Shader = "WGLatticeSky";

    /// <summary>How fast the shown sky follows the view's place, per second.</summary>
    private const float Follow = 0.8f;

    private readonly ShaderInstance _shader;
    private readonly List<(Vector2 Position, float Strength)> _anchors = new();
    private float _shown;

    /// <summary>After Kyphrus bursts, 0 to 1: the sky goes out but for a few stars.</summary>
    private float _dark;
    private TimeSpan _last;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowWorld;

    public override bool RequestScreenTexture => true;

    public WGLatticeSkyOverlay()
    {
        IoCManager.InjectDependencies(this);
        ZIndex = ParallaxSystem.ParallaxZIndex + 2;
        _shader = _proto.Index(Shader).InstanceUnique();
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (!_cfg.GetCVar(CCVars.ParallaxEnabled))
            return false;

        Advance();
        return _shown > 0.01f && _ent.HasComponent<WGLatticeFieldComponent>(args.MapUid);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null)
            return;

        var eye = args.Viewport.Eye?.Position.Position ?? args.WorldAABB.Center;
        _shader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        _shader.SetParameter("viewportMin", args.WorldAABB.BottomLeft);
        _shader.SetParameter("viewportSize", args.WorldAABB.Size);
        _shader.SetParameter("eyePos", eye);
        _shader.SetParameter("reach", _shown);
        _shader.SetParameter("dark", _dark);

        args.WorldHandle.UseShader(_shader);
        args.WorldHandle.DrawRect(args.WorldAABB, Color.White);
        args.WorldHandle.UseShader(null);
    }

    /// <summary>Eases the shown sky towards the main eye's place; off the sector map it holds where it was.</summary>
    private void Advance()
    {
        var now = _timing.RealTime;
        var dt = (float) Math.Clamp((now - _last).TotalSeconds, 0, 1);
        if (dt <= 0f)
            return;

        _last = now;
        if (Reach() is not { } target)
            return;

        // After the burst it is the Coda everywhere, and darker than ever.
        var seconds = _ent.System<WGLatticeViewSystem>().ImplosionSeconds(
            _ent.System<SharedMapSystem>().GetMapOrInvalid(_eye.CurrentEye.Position.MapId), out _);
        _dark = seconds is { } s ? Math.Clamp((s - WGLatticeImplosion.Burst) / 8f, 0f, 1f) : 0f;
        // The burst snaps the sky straight to the Coda's, rather than easing through the bands on the way.
        if (seconds >= WGLatticeImplosion.Burst)
        {
            _shown = MathF.Max(_shown, 4.8f);
            return;
        }

        _shown += (target - _shown) * (1f - MathF.Exp(-dt * Follow));
    }

    /// <summary>
    /// How far out the main eye is, in bands: 0 inside the hold, 1 at the Slack's edge, 4 at the Deep's, towards 5 far
    /// out in it. Null off a map with a Lattice.
    /// </summary>
    private float? Reach()
    {
        var position = _eye.CurrentEye.Position;
        if (!_ent.System<SharedMapSystem>().TryGetMap(position.MapId, out var map)
            || !_ent.TryGetComponent<WGLatticeFieldComponent>(map, out var field)
            || !_proto.TryIndex(field.Profile, out var profile))
            return null;

        _anchors.Clear();
        for (var i = 0; i < field.Anchors.Count && i < field.Strengths.Count; i++)
        {
            _anchors.Add((field.Anchors[i], field.Strengths[i]));
        }

        var thresholds = field.Thresholds;
        var strength = WGLatticeMath.Field(position.Position, _anchors, profile.Reach);
        var band = WGLatticeMath.Band(strength, thresholds);
        if (band == 0)
            return 0f;

        float through;
        if (band < thresholds.Count)
        {
            var inner = thresholds[band - 1];
            var outer = thresholds[band];
            through = inner - outer <= 0f ? 0f : Math.Clamp((inner - strength) / (inner - outer), 0f, 1f);
        }
        else
        {
            through = WGLatticeMath.Depth(strength, band, thresholds);
        }

        return band + through;
    }
}
