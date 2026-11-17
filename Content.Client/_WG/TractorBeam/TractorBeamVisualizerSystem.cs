using Robust.Client.Graphics;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WG.TractorBeam;

/// <summary>Registers the overlay that draws tractor beams.</summary>
public sealed partial class TractorBeamVisualizerSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();
        _overlays.AddOverlay(new TractorBeamOverlay(EntityManager, _prototypes, _timing));
    }

    public override void Shutdown()
    {
        _overlays.RemoveOverlay<TractorBeamOverlay>();
        base.Shutdown();
    }
}
