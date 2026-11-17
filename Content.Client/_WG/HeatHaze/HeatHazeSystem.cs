using Content.Shared._WG.CCVar;
using Content.Shared.CCVar;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;

namespace Content.Client._WG.HeatHaze;

/// <summary>
/// Keeps <see cref="HeatHazeOverlay"/> up while heat haze is enabled and reduced motion is off.
/// </summary>
public sealed partial class HeatHazeSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IOverlayManager _overlayMan = default!;

    private HeatHazeOverlay _overlay = default!;

    public override void Initialize()
    {
        base.Initialize();

        _overlay = new HeatHazeOverlay();

        Subs.CVar(_cfg, HeatHazeCVars.Strength, value => _overlay.Strength = Math.Max(value, 0f), true);
        Subs.CVar(_cfg, HeatHazeCVars.Enabled, _ => UpdateOverlay());
        Subs.CVar(_cfg, CCVars.ReducedMotion, _ => UpdateOverlay(), true);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _overlayMan.RemoveOverlay(_overlay);
        _overlay.Dispose();
    }

    private void UpdateOverlay()
    {
        if (_cfg.GetCVar(HeatHazeCVars.Enabled) && !_cfg.GetCVar(CCVars.ReducedMotion))
            _overlayMan.AddOverlay(_overlay);
        else
            _overlayMan.RemoveOverlay(_overlay);
    }
}
