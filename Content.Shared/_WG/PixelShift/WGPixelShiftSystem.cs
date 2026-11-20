using Content.Shared.ActionBlocker;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Input;
using Robust.Shared.Input.Binding;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Shared._WG.PixelShift;

/// <summary>
/// Shifts and tilts a mob's own sprite while a pixel shift key is held. <see cref="SharedMoverController"/> hands it
/// the movement keys pressed meanwhile; a movement key pressed without one clears the shift.
/// </summary>
public sealed class WGPixelShiftSystem : EntitySystem
{
    [Dependency] private ActionBlockerSystem _blocker = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private SharedMoverController _mover = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private INetConfigurationManager _netConfig = default!;
    [Dependency] private ISharedPlayerManager _player = default!;

    /// <summary>Degrees one tap of the tilt key turns the sprite.</summary>
    public const int TiltStep = 5;

    /// <summary>Slowest a held movement key shifts the sprite, in pixels per second.</summary>
    public const int MinShiftSpeed = 1;

    /// <summary>Fastest a held movement key shifts the sprite, in pixels per second.</summary>
    public const int MaxShiftSpeed = 100;

    /// <summary>Slowest a held movement key tilts the sprite, in degrees per second.</summary>
    public const int MinTiltSpeed = 5;

    /// <summary>Fastest a held movement key tilts the sprite, in degrees per second.</summary>
    public const int MaxTiltSpeed = 360;

    private static readonly Direction[] Cardinals = { Direction.North, Direction.South, Direction.East, Direction.West };

    private int _maxOffset;
    private int _maxTilt;

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_cfg, WGPixelShiftCVars.MaxOffset, v =>
        {
            _maxOffset = Math.Max(0, v);
            ClampAll();
        }, true);
        Subs.CVar(_cfg, WGPixelShiftCVars.MaxTilt, v =>
        {
            _maxTilt = Math.Clamp(v, 0, 180);
            ClampAll();
        }, true);

        CommandBinds.Builder
            .Bind(WGPixelShiftKeys.WGPixelShift, new HoldInputCmdHandler(this, WGPixelShiftMode.Shift))
            .Bind(WGPixelShiftKeys.WGPixelTilt, new HoldInputCmdHandler(this, WGPixelShiftMode.Tilt))
            .Bind(WGPixelShiftKeys.WGPixelShiftReset, InputCmdHandler.FromDelegate(session =>
            {
                if (session?.AttachedEntity is { } uid)
                    Reset(uid);
            }, handle: false, outsidePrediction: false))
            .Register<WGPixelShiftSystem>();

        SubscribeLocalEvent<WGPixelShiftHeldComponent, PlayerDetachedEvent>((uid, _, _) => RemComp<WGPixelShiftHeldComponent>(uid));
        SubscribeLocalEvent<WGPixelShiftHeldComponent, LocalPlayerDetachedEvent>((uid, _, _) => RemComp<WGPixelShiftHeldComponent>(uid));
    }

    public override void Shutdown()
    {
        base.Shutdown();
        CommandBinds.Unregister<WGPixelShiftSystem>();
    }

    /// <summary>
    /// Holds or releases a pixel shift key. Holding one stops the mob where it stands. Ghosts and other entities without
    /// a mob state never shift, so the key leaves them alone.
    /// </summary>
    public void SetHeld(EntityUid uid, WGPixelShiftMode mode, bool held, ushort subTick = 0)
    {
        if (held && !HasComp<MobStateComponent>(uid))
            return;

        if (!held)
        {
            if (!TryComp<WGPixelShiftHeldComponent>(uid, out var current))
                return;

            SetKey(current, mode, false);
            if (!current.Shift && !current.Tilt)
                RemComp<WGPixelShiftHeldComponent>(uid);
            else
                current.Mode = current.Shift ? WGPixelShiftMode.Shift : WGPixelShiftMode.Tilt; // The key still down.
            return;
        }

        var wasHeld = HasComp<WGPixelShiftHeldComponent>(uid);
        var keys = EnsureComp<WGPixelShiftHeldComponent>(uid);
        SetKey(keys, mode, true);
        keys.Mode = mode;
        if (!wasHeld)
            _mover.WgReleaseDirections(uid, subTick);
    }

    private static void SetKey(WGPixelShiftHeldComponent keys, WGPixelShiftMode mode, bool down)
    {
        if (mode == WGPixelShiftMode.Shift)
            keys.Shift = down;
        else
            keys.Tilt = down;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // The server moves held movement keys on; clients get the result with the networked shift.
        if (!_net.IsServer)
            return;

        var query = EntityQueryEnumerator<WGPixelShiftHeldComponent>();
        while (query.MoveNext(out var uid, out var held))
        {
            if (held.Directions == DirectionFlag.None)
                continue;

            held.Progress += HeldSpeed(uid, held.Mode) * frameTime;
            var amount = (int) held.Progress;
            if (amount <= 0)
                continue;

            held.Progress -= amount;
            foreach (var dir in Cardinals)
            {
                if ((held.Directions & dir.AsFlag()) != 0)
                    Nudge(uid, held, dir, amount);
            }
        }
    }

    /// <summary>
    /// Nudges the sprite for a movement key pressed while a pixel shift key is held, and keeps nudging at the player's
    /// speed while the key stays down. False when no key is held, so the key moves the mob as usual.
    /// </summary>
    public bool TryNudge(EntityUid uid, Direction dir)
    {
        if (!TryComp<WGPixelShiftHeldComponent>(uid, out var held))
            return false;

        // The press is the first step; the next follows one step's time later, with no pause between.
        if (held.Directions == DirectionFlag.None)
            held.Progress = 0f;

        held.Directions |= dir.AsFlag();
        Nudge(uid, held, dir, held.Mode == WGPixelShiftMode.Shift ? 1 : TiltStep);
        return true;
    }

    /// <summary>How fast the entity's player moves the sprite with a held key, in pixels or degrees per second.</summary>
    private float HeldSpeed(EntityUid uid, WGPixelShiftMode mode)
    {
        var cvar = mode == WGPixelShiftMode.Shift ? WGPixelShiftCVars.ShiftSpeed : WGPixelShiftCVars.TiltSpeed;
        var speed = _player.TryGetSessionByEntity(uid, out var session)
            ? _netConfig.GetClientCVar(session.Channel, cvar)
            : cvar.DefaultValue;

        return mode == WGPixelShiftMode.Shift ? ClampShiftSpeed(speed) : ClampTiltSpeed(speed);
    }

    /// <summary>A shift speed pulled inside <see cref="MinShiftSpeed"/> and <see cref="MaxShiftSpeed"/>.</summary>
    public static int ClampShiftSpeed(int speed)
    {
        return Math.Clamp(speed, MinShiftSpeed, MaxShiftSpeed);
    }

    /// <summary>A tilt speed pulled inside <see cref="MinTiltSpeed"/> and <see cref="MaxTiltSpeed"/>.</summary>
    public static int ClampTiltSpeed(int speed)
    {
        return Math.Clamp(speed, MinTiltSpeed, MaxTiltSpeed);
    }

    /// <summary>Stops a let-go movement key moving the sprite on.</summary>
    public void ReleaseDirection(EntityUid uid, Direction dir)
    {
        if (TryComp<WGPixelShiftHeldComponent>(uid, out var held))
            held.Directions &= ~dir.AsFlag();
    }

    /// <summary>Moves the shift <paramref name="amount"/> pixels, or the tilt that many degrees, toward a direction.</summary>
    private void Nudge(EntityUid uid, WGPixelShiftHeldComponent held, Direction dir, int amount)
    {
        if (!_blocker.CanInteract(uid, null))
            return;

        WGPixelShiftComponent? shift;
        if (_net.IsServer)
            shift = EnsureComp<WGPixelShiftComponent>(uid);
        else if (!TryComp(uid, out shift))
            return; // The server adds it; prediction starts from the next tap.

        var offset = shift.Offset;
        var tilt = shift.Tilt;
        if (held.Mode == WGPixelShiftMode.Shift)
        {
            var step = dir.ToIntVec() * amount;
            offset = new Vector2i(
                Math.Clamp(offset.X + step.X, -_maxOffset, _maxOffset),
                Math.Clamp(offset.Y + step.Y, -_maxOffset, _maxOffset));
        }
        else
        {
            var step = dir switch
            {
                Direction.West => amount,
                Direction.East => -amount,
                _ => 0,
            };
            tilt = Math.Clamp(tilt + step, -_maxTilt, _maxTilt);
        }

        if (offset == shift.Offset && tilt == shift.Tilt)
            return;

        shift.Offset = offset;
        shift.Tilt = tilt;
        Dirty(uid, shift);
    }

    /// <summary>Clears the shift and tilt when the mob steps off, as long as it can walk.</summary>
    public void ResetOnWalk(EntityUid uid)
    {
        if (HasComp<WGPixelShiftComponent>(uid) && _blocker.CanMove(uid))
            Reset(uid);
    }

    /// <summary>Pulls every shift and tilt back inside the limits, after a limit is lowered.</summary>
    private void ClampAll()
    {
        if (!_net.IsServer)
            return;

        var query = EntityQueryEnumerator<WGPixelShiftComponent>();
        while (query.MoveNext(out var uid, out var shift))
        {
            var offset = new Vector2i(
                Math.Clamp(shift.Offset.X, -_maxOffset, _maxOffset),
                Math.Clamp(shift.Offset.Y, -_maxOffset, _maxOffset));
            var tilt = Math.Clamp(shift.Tilt, -_maxTilt, _maxTilt);
            if (offset == shift.Offset && tilt == shift.Tilt)
                continue;

            shift.Offset = offset;
            shift.Tilt = tilt;
            Dirty(uid, shift);
        }
    }

    /// <summary>Clears the shift and tilt.</summary>
    public void Reset(EntityUid uid)
    {
        if (!TryComp<WGPixelShiftComponent>(uid, out var shift) || shift.Offset == Vector2i.Zero && shift.Tilt == 0)
            return;

        shift.Offset = Vector2i.Zero;
        shift.Tilt = 0;
        Dirty(uid, shift);
    }

    private sealed class HoldInputCmdHandler(WGPixelShiftSystem system, WGPixelShiftMode mode) : InputCmdHandler
    {
        public override bool HandleCmdMessage(IEntityManager entManager, ICommonSession? session, IFullInputCmdMessage message)
        {
            if (session?.AttachedEntity is not { } uid)
                return false;

            system.SetHeld(uid, mode, message.State == BoundKeyState.Down, message.SubTick);
            return false;
        }
    }
}
