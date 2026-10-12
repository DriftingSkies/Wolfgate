using System.Numerics;
using Content.Client.Eye;
using Content.Client.Movement.Systems;
using Robust.Client.Player;

namespace Content.Client._WF.Shuttles.Systems;

/// <summary>
/// Zooms the external view as the wheel turns rather than once the server has answered. Each tick
/// starts from the server's zoom again, so the wheel's is put back before the eye eases towards it.
/// </summary>
public sealed partial class ShuttleCameraZoomPredictionSystem : EntitySystem
{
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private ContentEyeSystem _contentEye = default!;
    [Dependency] private ShuttleExternalCameraSystem _external = default!;

    public override void Initialize()
    {
        base.Initialize();

        UpdatesBefore.Add(typeof(EyeLerpingSystem));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_external.PendingZoom is { } zoom && _player.LocalEntity is { } player)
            _contentEye.SetZoom(player, new Vector2(zoom), ignoreLimits: true);
    }
}
