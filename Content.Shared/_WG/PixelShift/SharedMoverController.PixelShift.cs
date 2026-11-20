using Content.Shared._WG.PixelShift;

namespace Content.Shared.Movement.Systems;

public abstract partial class SharedMoverController
{
    [Dependency] private WGPixelShiftSystem _wgPixelShift = default!;

    /// <summary>
    /// Hands a movement key press to the pixel shift while one of its keys is held. Otherwise the press is a step,
    /// which clears the shift. Releases always pass through, after stopping the key's repeat.
    /// </summary>
    private bool WgTryPixelShift(EntityUid entity, Direction dir, bool state)
    {
        if (!state)
        {
            _wgPixelShift.ReleaseDirection(entity, dir);
            return false;
        }

        if (_wgPixelShift.TryNudge(entity, dir))
            return true;

        _wgPixelShift.ResetOnWalk(entity);
        return false;
    }
}
