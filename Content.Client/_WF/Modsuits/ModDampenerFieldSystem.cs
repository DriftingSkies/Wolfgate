using Robust.Client.Graphics;

namespace Content.Client._WF.Modsuits;

public sealed partial class ModDampenerFieldSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlay = default!;

    public override void Initialize()
    {
        base.Initialize();

        _overlay.AddOverlay(new ModDampenerFieldOverlay());
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _overlay.RemoveOverlay<ModDampenerFieldOverlay>();
    }
}
