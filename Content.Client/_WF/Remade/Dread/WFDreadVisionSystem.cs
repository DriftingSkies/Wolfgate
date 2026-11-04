using Content.Shared._WF.Remade.Dread;
using Content.Shared.CCVar;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Client._WF.Remade.Dread;

/// <summary>
/// Drives the dread overlay from what the server sends. The level holds while it keeps being renewed and fades when it isn't;
/// a spike throbs and dies at once. With reduced motion on the screen only darkens at the edges and never moves.
/// </summary>
public sealed partial class WFDreadVisionSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IOverlayManager _overlays = default!;

    private const float EaseRate = 1.5f;
    private const float FadeSeconds = 4f;
    private const float SpikeSeconds = 1.4f;

    private WFDreadOverlay _overlay = default!;
    private float _target;
    private float _level;
    private float _spike;
    private TimeSpan _holdUntil;

    /// <summary>The warp being drawn now. Tests read it.</summary>
    public float Level => _level;

    public float Spike => _spike;

    public WFDreadOverlay Overlay => _overlay;

    public override void Initialize()
    {
        base.Initialize();
        _overlay = new WFDreadOverlay();
        SubscribeNetworkEvent<WFDreadVisionEvent>(OnVision);
        SubscribeLocalEvent<LocalPlayerDetachedEvent>(_ => Reset());
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlays.RemoveOverlay(_overlay);
    }

    private void Reset()
    {
        _target = 0f;
        _level = 0f;
        _spike = 0f;
        Apply();
    }

    private void OnVision(WFDreadVisionEvent ev)
    {
        _target = Math.Clamp(ev.Level, 0f, 1f);
        _holdUntil = _timing.CurTime + TimeSpan.FromSeconds(Math.Clamp(ev.Seconds, 0.5f, 30f));
        _spike = Math.Max(_spike, Math.Clamp(ev.Spike, 0f, 1f));
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (_target <= 0f && _level <= 0.001f && _spike <= 0.001f)
        {
            if (_overlays.HasOverlay<WFDreadOverlay>())
                _overlays.RemoveOverlay(_overlay);

            return;
        }

        // Nothing renewing the level lets it fade out.
        if (_timing.CurTime > _holdUntil)
            _target = Math.Max(0f, _target - frameTime / FadeSeconds);

        _level += (_target - _level) * Math.Min(1f, frameTime * EaseRate);
        _spike = Math.Max(0f, _spike - frameTime / SpikeSeconds);
        Apply();
    }

    private void Apply()
    {
        var scale = Math.Max(0f, _config.GetCVar(WFDreadCVars.Visuals));
        var level = _level * scale;
        var spike = _spike * scale;
        if (scale <= 0f || level + spike <= 0.001f)
        {
            _overlay.Wobble = _overlay.Split = _overlay.Vignette = _overlay.Drain = 0f;
            if (_overlays.HasOverlay<WFDreadOverlay>())
                _overlays.RemoveOverlay(_overlay);

            return;
        }

        if (_config.GetCVar(CCVars.ReducedMotion))
        {
            // Still, and gentler: no wobble, no split, no throbbing and no flash of colour.
            _overlay.Wobble = 0f;
            _overlay.Split = 0f;
            _overlay.Drain = 0f;
            _overlay.Vignette = WFDreadMath.Look(level * 0.6f, 0f).Vignette;
        }
        else
        {
            var look = WFDreadMath.Look(level, spike);
            _overlay.Wobble = look.Wobble;
            _overlay.Split = look.Split;
            _overlay.Vignette = look.Vignette;
            _overlay.Drain = look.Drain;
        }

        if (!_overlays.HasOverlay<WFDreadOverlay>())
            _overlays.AddOverlay(_overlay);
    }
}
