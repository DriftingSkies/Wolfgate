using Content.Shared._Scp.ScpCCVars;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;

namespace Content.Client._WG.DisplayOptions;

/// <summary>
/// Keeps <see cref="WGBloomOverlay"/> up while light bloom is on, in place of the SCP layer's per-lamp bloom overlays.
/// </summary>
public sealed partial class WGBloomSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IOverlayManager _overlayMan = default!;

    private WGBloomOverlay _overlay = default!;

    public override void Initialize()
    {
        base.Initialize();

        _overlay = new WGBloomOverlay();

        Subs.CVar(_cfg, ScpCCVars.LightBloomStrength, value => _overlay.Strength = Math.Clamp(value, 0f, 1f), true);
        Subs.CVar(_cfg, ScpCCVars.LightBloomEnable, OnEnabledChanged, true);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _overlayMan.RemoveOverlay(_overlay);
        _overlay.Dispose();
    }

    private void OnEnabledChanged(bool enabled)
    {
        if (enabled)
            _overlayMan.AddOverlay(_overlay);
        else
            _overlayMan.RemoveOverlay(_overlay);
    }
}
