using Robust.Client.Graphics;

namespace Content.Client._WG.WolfChem.Narcotics;

/// <summary>Draws the exotic matter sense for the local player.</summary>
public sealed class WGExoticSenseSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlays = default!;

    private WGExoticSenseOverlay _overlay = default!;

    public override void Initialize()
    {
        base.Initialize();
        _overlay = new WGExoticSenseOverlay();
        _overlays.AddOverlay(_overlay);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlays.RemoveOverlay(_overlay);
    }
}
