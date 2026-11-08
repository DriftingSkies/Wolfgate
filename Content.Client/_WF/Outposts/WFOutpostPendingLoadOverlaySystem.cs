using Robust.Client.Graphics;
using Robust.Shared.Timing;

namespace Content.Client._WF.Outposts;

/// <summary>Shows where a saved outpost will land while it is on its way down.</summary>
public sealed class WFOutpostPendingLoadOverlaySystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();
        _overlays.AddOverlay(new WFOutpostPendingLoadOverlay(EntityManager, _timing));
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlays.RemoveOverlay<WFOutpostPendingLoadOverlay>();
    }
}
