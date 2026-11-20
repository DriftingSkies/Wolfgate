using Content.Client._WG.Augments.UI;
using Robust.Client.Graphics;

namespace Content.Client._WG.Augments;

/// <summary>Keeps the smartlink's sight on screen; it draws only while the player holds a gun in a linked hand.</summary>
public sealed partial class WGSmartlinkOverlaySystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlays = default!;

    public override void Initialize()
    {
        base.Initialize();
        _overlays.AddOverlay(new WGSmartlinkOverlay());
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlays.RemoveOverlay<WGSmartlinkOverlay>();
    }
}
