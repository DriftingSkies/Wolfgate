using Robust.Client.Graphics;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WG.AsteroidMining;

/// <summary>Registers the overlay that draws ore magnets pulling ore.</summary>
public sealed partial class WGOreMagnetVisualsSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();
        _overlays.AddOverlay(new WGOreMagnetOverlay(EntityManager, _prototypes, _timing));
    }

    public override void Shutdown()
    {
        _overlays.RemoveOverlay<WGOreMagnetOverlay>();
        base.Shutdown();
    }
}
