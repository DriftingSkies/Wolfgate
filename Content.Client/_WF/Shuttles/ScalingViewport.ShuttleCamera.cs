using Content.Client._WF.Shuttles.Systems;
using Content.Client.UserInterface.Controls;
using Robust.Client.UserInterface;

namespace Content.Client.Viewport;

public sealed partial class ScalingViewport
{
    /// <summary>Zooms a pilot's external view with the mouse wheel, over the game's own viewport.</summary>
    protected override void MouseWheel(GUIMouseWheelEventArgs args)
    {
        base.MouseWheel(args);

        // The admin camera, tabletop and camera monitors share this class and want the wheel to bubble.
        if (args.Handled || args.Delta.Y == 0f || Parent is not MainViewport)
            return;

        if (_entityManager.TrySystem<ShuttleExternalCameraSystem>(out var camera) && camera.TryWheelZoom(args.Delta.Y))
            args.Handle();
    }
}
