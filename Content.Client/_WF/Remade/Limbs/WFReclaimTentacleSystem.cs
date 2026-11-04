using Content.Shared.CCVar;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Client._WF.Remade.Limbs;

/// <summary>Adds the mender's tentacle overlay and holds it still with reduced motion.</summary>
public sealed class WFReclaimTentacleSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IOverlayManager _overlays = default!;

    private WFReclaimTentacleOverlay _overlay = default!;

    public override void Initialize()
    {
        base.Initialize();
        _overlay = new WFReclaimTentacleOverlay(EntityManager, _timing);
        Subs.CVar(_config, CCVars.ReducedMotion, motion => _overlay.Still = motion, true);
        _overlays.AddOverlay(_overlay);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlays.RemoveOverlay(_overlay);
    }
}
