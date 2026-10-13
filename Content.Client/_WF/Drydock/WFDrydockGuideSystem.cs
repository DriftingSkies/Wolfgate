using Robust.Client.Graphics;

namespace Content.Client._WF.Drydock;

/// <summary>
/// Adds the booked-berth guide overlay for the client's lifetime.
/// </summary>
public sealed partial class WFDrydockGuideSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlay = default!;

    public override void Initialize()
    {
        base.Initialize();

        _overlay.AddOverlay(new WFDrydockGuideOverlay());
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _overlay.RemoveOverlay<WFDrydockGuideOverlay>();
    }
}
