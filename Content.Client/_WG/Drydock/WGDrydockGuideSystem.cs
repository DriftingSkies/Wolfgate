using Robust.Client.Graphics;

namespace Content.Client._WG.Drydock;

/// <summary>
/// Adds the booked-berth guide overlay for the client's lifetime.
/// </summary>
public sealed partial class WGDrydockGuideSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlay = default!;

    public override void Initialize()
    {
        base.Initialize();

        _overlay.AddOverlay(new WGDrydockGuideOverlay());
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _overlay.RemoveOverlay<WGDrydockGuideOverlay>();
    }
}
