using Content.Client._WG.Shuttles.UI;
using Content.Shared._WG.Shuttles;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;

namespace Content.Client._WG.Shuttles.Systems;

/// <summary>
/// Keeps the local pilot's eye on their hull camera. The engine drops an eye target whose entity
/// hasn't arrived yet, which a freshly spawned camera usually hasn't. Also owns the low-light feed.
/// </summary>
public sealed partial class ShuttleCameraSystem : EntitySystem
{
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private EyeSystem _eye = default!;
    [Dependency] private IOverlayManager _overlay = default!;

    public override void Initialize()
    {
        base.Initialize();
        _overlay.AddOverlay(new ShuttleLowLightOverlay());
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlay.RemoveOverlay<ShuttleLowLightOverlay>();
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (_player.LocalEntity is not { } player ||
            !TryComp<ShuttleCameraComponent>(player, out var camera) ||
            camera.Camera is not { } target ||
            !TryComp<EyeComponent>(player, out var eye) ||
            eye.Target == target ||
            !HasComp<TransformComponent>(target))
        {
            return;
        }

        _eye.SetTarget(player, target, eye);
    }
}
