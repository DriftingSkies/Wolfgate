using Content.Shared._WG.Xenonids.Pheromones;
using Robust.Client.Graphics;

namespace Content.Client._WG.Xenonids.Pheromones;

public sealed partial class XenoPheromonesSystem : SharedXenoPheromonesSystem
{
    [Dependency] private IOverlayManager _overlays = default!;

    public override void Initialize()
    {
        base.Initialize();

        _overlays.AddOverlay(new XenoPheromonesOverlay());
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _overlays.RemoveOverlay<XenoPheromonesOverlay>();
    }
}
