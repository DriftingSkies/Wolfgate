using Content.Shared._WG.TurnInPlace;
using Content.Shared.Movement.Components;

namespace Content.Shared.Movement.Systems;

public abstract partial class SharedMoverController
{
    /// <summary>
    /// Turns the mob instead of moving it while its turn-in-place key is held. True when the input was used up.
    /// Key releases always pass through, so a key held before the turn key still clears.
    /// </summary>
    private bool WgTryTurnInPlace(EntityUid entity, Direction dir, bool state)
    {
        if (!state || !HasComp<WGTurnInPlaceComponent>(entity))
            return false;

        if (WgMoverTarget(entity) is not { } target)
            return true;

        // Facing the cursor in combat holds the facing, so the key is inert and the press moves as usual.
        if (NoRotateQuery.HasComp(target))
            return false;

        if (!MoverQuery.TryComp(target, out var mover)
            || !mover.CanMove
            || _container.IsEntityInContainer(target)
            || !XformQuery.TryComp(target, out var xform))
        {
            return true;
        }

        var vec = dir.ToVec();
        var wish = _relativeMovement ? GetParentGridAngle(mover).RotateVec(vec) : vec;
        var worldRot = _transform.GetWorldRotation(xform);
        _transform.SetLocalRotation(target, xform.LocalRotation + wish.ToWorldAngle() - worldRot, xform);
        return true;
    }

    /// <summary>Whether the mob an entity steers can't turn by moving, as while combat mode faces the cursor.</summary>
    public bool WgFacingLocked(EntityUid entity)
    {
        return WgMoverTarget(entity) is { } target && NoRotateQuery.HasComp(target);
    }

    /// <summary>Releases the direction keys of the mob an entity steers, so a held turn or shift key stops it.</summary>
    public void WgReleaseDirections(EntityUid entity, ushort subTick)
    {
        if (WgMoverTarget(entity) is { } target && MoverQuery.TryComp(target, out var mover))
            SetMoveInput((target, mover), subTick, false, MoveButtons.AnyDirection);
    }

    /// <summary>
    /// The mob an entity's movement keys steer, following movement relays. Null when a relaying mob is incapacitated,
    /// since <see cref="HandleDirChange"/> doesn't pass its keys on either.
    /// </summary>
    private EntityUid? WgMoverTarget(EntityUid entity)
    {
        for (var i = 0; i < 4 && RelayQuery.TryComp(entity, out var relay); i++)
        {
            if (_mobState.IsIncapacitated(entity))
                return null;

            entity = relay.RelayEntity;
        }

        return entity;
    }
}
