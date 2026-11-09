using Robust.Client.Graphics;

namespace Content.Client._WF.Photography;

/// <summary>Keeps the camera preview overlay registered.</summary>
public sealed partial class PhotoPreviewSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlays = default!;

    public override void Initialize()
    {
        base.Initialize();

        _overlays.AddOverlay(new PhotoPreviewOverlay());
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _overlays.RemoveOverlay<PhotoPreviewOverlay>();
    }
}
