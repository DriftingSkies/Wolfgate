using Content.Shared.Movement.Systems;
using Robust.Shared.Input;
using Robust.Shared.Input.Binding;
using Robust.Shared.Player;

namespace Content.Shared._WG.TurnInPlace;

/// <summary>Tracks the held turn-in-place key; <see cref="SharedMoverController"/> reads it on movement input.</summary>
public sealed class WGTurnInPlaceSystem : EntitySystem
{
    [Dependency] private SharedMoverController _mover = default!;

    public override void Initialize()
    {
        base.Initialize();

        CommandBinds.Builder
            .Bind(WGTurnInPlaceKeys.WGTurnInPlace, new TurnInPlaceInputCmdHandler(this))
            .Register<WGTurnInPlaceSystem>();

        // The key's release goes to whatever the player controls by then, so a body left behind keeps no hold.
        SubscribeLocalEvent<WGTurnInPlaceComponent, PlayerDetachedEvent>((uid, _, _) => RemComp<WGTurnInPlaceComponent>(uid));
        SubscribeLocalEvent<WGTurnInPlaceComponent, LocalPlayerDetachedEvent>((uid, _, _) => RemComp<WGTurnInPlaceComponent>(uid));
    }

    public override void Shutdown()
    {
        base.Shutdown();
        CommandBinds.Unregister<WGTurnInPlaceSystem>();
    }

    /// <summary>
    /// Holds or releases the turn key for an entity. Holding it stops the mob where it stands, unless the mob can't
    /// turn by moving anyway.
    /// </summary>
    public void SetHeld(EntityUid uid, bool held, ushort subTick = 0)
    {
        if (!held)
        {
            RemComp<WGTurnInPlaceComponent>(uid);
            return;
        }

        if (HasComp<WGTurnInPlaceComponent>(uid))
            return;

        AddComp<WGTurnInPlaceComponent>(uid);
        if (!_mover.WgFacingLocked(uid))
            _mover.WgReleaseDirections(uid, subTick);
    }

    private sealed class TurnInPlaceInputCmdHandler(WGTurnInPlaceSystem system) : InputCmdHandler
    {
        public override bool HandleCmdMessage(IEntityManager entManager, ICommonSession? session, IFullInputCmdMessage message)
        {
            if (session?.AttachedEntity is not { } uid)
                return false;

            system.SetHeld(uid, message.State == BoundKeyState.Down, message.SubTick);
            return false;
        }
    }
}
