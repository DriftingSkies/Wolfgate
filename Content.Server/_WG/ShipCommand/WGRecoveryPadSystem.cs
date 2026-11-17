using System.Linq;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Shared.Damage;
using Content.Shared._WG.ShipCommand;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.Database;
using Content.Shared.Ghost;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Popups;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Timing;

namespace Content.Server._WG.ShipCommand;

/// <summary>
/// Recovery pads (<see cref="WGRecoveryPadComponent"/>) and their consoles. The console lists everyone with a mind on
/// the pad's map, the dead and the worst hurt first; recalling one charges the pad, counts the target down if they are alive and awake, then brings them
/// onto the pad out of whatever held them. One recall at a time, then a cooldown. Who may use it is the pad's
/// <see cref="WGRecoveryPadComponent.Access"/>, measured against the ship's standings (<see cref="WGShipCommandSystem"/>);
/// on a grid with no command lists only admins count. Every recall goes in the admin log and the ship's service log.
/// </summary>
public sealed class WGRecoveryPadSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IAdminManager _admin = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private PullingSystem _pulling = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedBuckleSystem _buckle = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private WGShipCommandSystem _command = default!;
    [Dependency] private WGShipServiceSystem _service = default!;

    private static readonly TimeSpan RefreshGap = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RefreshMessageGap = TimeSpan.FromSeconds(0.5);
    private TimeSpan _nextRefresh;

    /// <summary>When each viewer's refresh button may next rebuild their state.</summary>
    private readonly Dictionary<EntityUid, TimeSpan> _refreshAt = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGRecoveryPadConsoleComponent, BoundUIOpenedEvent>(OnOpened);
        SubscribeLocalEvent<WGRecoveryPadConsoleComponent, WGRecoveryPadRefreshMessage>(OnRefresh);
        SubscribeLocalEvent<WGRecoveryPadConsoleComponent, WGRecoveryPadRecallMessage>(OnRecall);
        SubscribeLocalEvent<WGRecoveryPadConsoleComponent, WGRecoveryPadAccessMessage>(OnAccess);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        var pads = EntityQueryEnumerator<WGRecoveryPadComponent>();
        while (pads.MoveNext(out var uid, out var pad))
        {
            if (pad.Target is not { } target)
                continue;

            var ent = (uid, pad);
            if (!Reaches(ent, target))
            {
                if (pad.Operator is { } op && Exists(op))
                {
                    var line = Exists(target) ? Loc.GetString("wg-recovery-pad-lost", ("target", target)) : Loc.GetString("wg-recovery-pad-lost-gone");
                    _popup.PopupEntity(line, uid, op, PopupType.MediumCaution);
                }

                Stop(ent, cooldown: false);
                continue;
            }

            var left = pad.ChargeEnds - now;
            if (pad.Warn && left > TimeSpan.Zero)
            {
                var seconds = (int) Math.Ceiling(left.TotalSeconds);
                if (seconds != pad.Warned)
                {
                    pad.Warned = seconds;
                    _popup.PopupEntity(Loc.GetString("wg-recovery-pad-countdown", ("seconds", seconds)), target, target, PopupType.LargeCaution);
                }
            }

            if (now >= pad.ChargeEnds)
                Recall(ent, target);
        }

        if (now < _nextRefresh)
            return;

        _nextRefresh = now + RefreshGap;
        var consoles = EntityQueryEnumerator<WGRecoveryPadConsoleComponent>();
        while (consoles.MoveNext(out var uid, out _))
        {
            foreach (var actor in _ui.GetActors(uid, WGRecoveryPadUiKey.Key).ToList())
                SendState(uid, actor);
        }
    }

    #region Who and where

    /// <summary>The nearest recovery pad on the console's grid within its reach; null when there is none.</summary>
    public Entity<WGRecoveryPadComponent>? PadFor(EntityUid console)
    {
        if (!TryComp<WGRecoveryPadConsoleComponent>(console, out var comp) || Transform(console).GridUid is not { } grid)
            return null;

        var from = _transform.GetMapCoordinates(console);
        Entity<WGRecoveryPadComponent>? best = null;
        var bestDistance = comp.PadRange;
        var pads = EntityQueryEnumerator<WGRecoveryPadComponent, TransformComponent>();
        while (pads.MoveNext(out var uid, out var pad, out var xform))
        {
            if (xform.GridUid != grid)
                continue;

            var distance = (_transform.GetMapCoordinates(uid, xform).Position - from.Position).Length();
            if (distance > bestDistance)
                continue;

            best = (uid, pad);
            bestDistance = distance;
        }

        return best;
    }

    /// <summary>
    /// Where someone stands with the pad's ship. Off a commanded grid that is admin or guest: a plain ship's deed
    /// holder doesn't count as its owner here.
    /// </summary>
    public WGShipAuthority Authority(EntityUid user, EntityUid pad)
    {
        WGShipCommandComponent? command = null;
        var authority = Transform(pad).GridUid is { } grid && TryComp<WGShipCommandComponent>(grid, out command)
            ? _command.GetAuthority(user, grid)
            : WGShipAuthority.Guest;
        if (authority >= WGShipAuthority.Admin)
            return authority;

        var eventStaff = command?.EventStaffAreAdmins ?? true;
        return _player.TryGetSessionByEntity(user, out var session)
               && WGShipCommandSystem.CountsAsAdmin(_admin.GetAdminData(session), eventStaff)
            ? WGShipAuthority.Admin
            : authority;
    }

    /// <summary>Whether someone may recall with this pad: their standing is at least its access.</summary>
    public bool CanUse(EntityUid user, Entity<WGRecoveryPadComponent> pad)
    {
        var authority = Authority(user, pad);
        return authority != WGShipAuthority.Barred && authority >= pad.Comp.Access;
    }

    /// <summary>Whether someone may change who uses the pad: whoever configures the ship; off a commanded grid, admins.</summary>
    public bool CanConfigure(EntityUid user, EntityUid pad)
    {
        if (Transform(pad).GridUid is { } grid && HasComp<WGShipCommandComponent>(grid))
            return _command.CanConfigure(user, grid);

        return Authority(user, pad) >= WGShipAuthority.Admin;
    }

    /// <summary>Alive, critical or dead; null for anything that isn't a mob.</summary>
    public WGRecoveryPadCondition? Condition(EntityUid target)
    {
        if (!TryComp<MobStateComponent>(target, out var state))
            return null;

        if (_mobState.IsDead(target, state))
            return WGRecoveryPadCondition.Dead;

        return _mobState.IsCritical(target, state) ? WGRecoveryPadCondition.Critical : WGRecoveryPadCondition.Alive;
    }

    /// <summary>A person the pad recalls: a mob with a mind (a player's character, or one that was), not a ghost.</summary>
    public bool IsPerson(EntityUid target)
    {
        return HasComp<MobStateComponent>(target) && !HasComp<GhostComponent>(target) && _mind.TryGetMind(target, out _, out _);
    }

    /// <summary>True when the target is a person on the pad's map, within its range if it has one.</summary>
    public bool Reaches(Entity<WGRecoveryPadComponent> pad, EntityUid target)
    {
        if (TerminatingOrDeleted(target) || !IsPerson(target))
            return false;

        var from = _transform.GetMapCoordinates(pad);
        var to = _transform.GetMapCoordinates(target);
        return from.MapId == to.MapId
               && (pad.Comp.Range is not { } range || (to.Position - from.Position).Length() <= range);
    }

    /// <summary>
    /// Everyone the pad can reach: the dead first, then the critical, then the living, the worst hurt first within each
    /// (the v0.9 review), and the nearest first among the equally hurt.
    /// </summary>
    public List<WGRecoveryPadEntry> People(Entity<WGRecoveryPadComponent> pad)
    {
        var result = new List<WGRecoveryPadEntry>();
        var from = _transform.GetMapCoordinates(pad);
        var minds = EntityQueryEnumerator<MindContainerComponent, MobStateComponent, TransformComponent>();
        while (minds.MoveNext(out var uid, out _, out _, out var xform))
        {
            if (xform.MapID != from.MapId || !Reaches(pad, uid) || Condition(uid) is not { } condition)
                continue;

            var distance = (_transform.GetMapCoordinates(uid, xform).Position - from.Position).Length();
            var where = xform.GridUid is { } grid ? Name(grid) : Loc.GetString("wg-recovery-pad-in-space");
            var damage = TryComp<DamageableComponent>(uid, out var damageable) ? damageable.TotalDamage.Int() : 0;
            result.Add(new WGRecoveryPadEntry(GetNetEntity(uid), Name(uid), condition, where, distance, damage));
        }

        result.Sort((a, b) =>
        {
            var byCondition = b.Condition.CompareTo(a.Condition);
            if (byCondition != 0)
                return byCondition;

            var byDamage = b.Damage.CompareTo(a.Damage);
            return byDamage != 0 ? byDamage : a.Distance.CompareTo(b.Distance);
        });
        return result;
    }

    #endregion

    #region Recall

    /// <summary>
    /// Starts a recall. Someone alive and awake needs <paramref name="confirmed"/>; without it the operator is warned
    /// and nothing happens. <see cref="WGRecoveryPadRefusal.None"/> when the pad is charging.
    /// </summary>
    public WGRecoveryPadRefusal TryRecall(Entity<WGRecoveryPadComponent> pad, EntityUid user, EntityUid target, bool confirmed)
    {
        if (!CanUse(user, pad))
            return WGRecoveryPadRefusal.NotAllowed;
        if (pad.Comp.Target != null)
            return WGRecoveryPadRefusal.Busy;
        if (_timing.CurTime < pad.Comp.ReadyAt)
            return WGRecoveryPadRefusal.Cooldown;
        if (!Reaches(pad, target))
            return WGRecoveryPadRefusal.OutOfReach;

        var alive = Condition(target) == WGRecoveryPadCondition.Alive;
        if (alive && !confirmed)
            return WGRecoveryPadRefusal.NeedsConfirmation;

        pad.Comp.Target = target;
        pad.Comp.Operator = user;
        pad.Comp.ChargeEnds = _timing.CurTime + pad.Comp.ChargeTime;
        pad.Comp.Warn = alive;
        pad.Comp.Warned = -1;
        _appearance.SetData(pad, WGRecoveryPadVisuals.Charging, true);
        _adminLog.Add(LogType.Action, alive ? LogImpact.High : LogImpact.Medium,
            $"{ToPrettyString(user):actor} started a recovery pad recall of {ToPrettyString(target):subject} ({Condition(target)}) with {ToPrettyString(pad.Owner):pad}");
        return WGRecoveryPadRefusal.None;
    }

    /// <summary>The charge is done: the target leaves whatever holds them and arrives on the pad.</summary>
    private void Recall(Entity<WGRecoveryPadComponent> pad, EntityUid target)
    {
        var user = pad.Comp.Operator;
        var arrival = Transform(pad).Coordinates;
        _audio.PlayPvs(pad.Comp.DepartSound, Transform(target).Coordinates);

        // Straight onto the pad: a removal without a destination would put them in the next container up, as with a
        // body bag in a morgue tray.
        if (_container.TryGetContainingContainer(target, out var container))
            _container.Remove(target, container, force: true, destination: arrival);

        if (TryComp<BuckleComponent>(target, out var buckle) && buckle.Buckled)
            _buckle.Unbuckle((target, buckle), null);

        if (TryComp<PullableComponent>(target, out var pullable) && pullable.BeingPulled)
            _pulling.TryStopPull(target, pullable);

        if (TryComp<PullerComponent>(target, out var puller) && puller.Pulling is { } pulled
            && TryComp<PullableComponent>(pulled, out var pulledComp))
            _pulling.TryStopPull(pulled, pulledComp);

        _transform.SetCoordinates(target, arrival);
        _audio.PlayPvs(pad.Comp.ArriveSound, pad);
        _popup.PopupEntity(Loc.GetString("wg-recovery-pad-arrived", ("target", target)), pad, PopupType.Medium);

        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(user):actor} recalled {ToPrettyString(target):subject} onto {ToPrettyString(pad.Owner):pad}");
        var who = user is { } u && Exists(u) ? Name(u) : Loc.GetString("wg-ship-service-nobody");
        _service.Record(pad, target, WGShipServiceKind.Medical, Loc.GetString("wg-ship-service-recall", ("who", who)), 0, 0, null);
        Stop(pad, cooldown: true);
    }

    private void Stop(Entity<WGRecoveryPadComponent> pad, bool cooldown)
    {
        pad.Comp.Target = null;
        pad.Comp.Operator = null;
        pad.Comp.Warn = false;
        pad.Comp.Warned = -1;
        if (cooldown)
            pad.Comp.ReadyAt = _timing.CurTime + pad.Comp.Cooldown;
        _appearance.SetData(pad, WGRecoveryPadVisuals.Charging, false);
    }

    /// <summary>Changes who may use the pad: staff, officers or admins. False when the user may not, or nothing changed.</summary>
    public bool SetAccess(Entity<WGRecoveryPadComponent> pad, EntityUid user, WGShipAuthority access)
    {
        if (access is not (WGShipAuthority.Staff or WGShipAuthority.Officer or WGShipAuthority.Admin)
            || pad.Comp.Access == access || !CanConfigure(user, pad))
            return false;

        pad.Comp.Access = access;
        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(user):actor} opened recovery pad {ToPrettyString(pad.Owner):pad} to {access}");
        return true;
    }

    #endregion

    #region Console

    private void OnOpened(Entity<WGRecoveryPadConsoleComponent> console, ref BoundUIOpenedEvent args)
    {
        SendState(console, args.Actor);
    }

    private void OnRefresh(Entity<WGRecoveryPadConsoleComponent> console, ref WGRecoveryPadRefreshMessage args)
    {
        // A held-down refresh shouldn't rebuild the list every tick.
        var now = _timing.CurTime;
        if (now < _refreshAt.GetValueOrDefault(args.Actor))
            return;

        if (_refreshAt.Count > 64)
            _refreshAt.Clear();
        _refreshAt[args.Actor] = now + RefreshMessageGap;
        SendState(console, args.Actor);
    }

    private void OnRecall(Entity<WGRecoveryPadConsoleComponent> console, ref WGRecoveryPadRecallMessage args)
    {
        var refusal = PadFor(console) is { } pad && GetEntity(args.Target) is { Valid: true } target
            ? TryRecall(pad, args.Actor, target, args.Confirmed)
            : WGRecoveryPadRefusal.NoPad;

        var line = refusal switch
        {
            WGRecoveryPadRefusal.None => Loc.GetString("wg-recovery-pad-charging"),
            WGRecoveryPadRefusal.NeedsConfirmation => Loc.GetString("wg-recovery-pad-confirm-alive"),
            _ => Loc.GetString($"wg-recovery-pad-refused-{refusal.ToString().ToLowerInvariant()}"),
        };
        _popup.PopupEntity(line, console, args.Actor,
            refusal == WGRecoveryPadRefusal.None ? PopupType.Medium : PopupType.MediumCaution);
        SendState(console, args.Actor);
    }

    private void OnAccess(Entity<WGRecoveryPadConsoleComponent> console, ref WGRecoveryPadAccessMessage args)
    {
        if (PadFor(console) is { } pad)
            SetAccess(pad, args.Actor, args.Access);
        SendState(console, args.Actor);
    }

    /// <summary>The console's state for one viewer.</summary>
    public WGRecoveryPadState State(EntityUid console, EntityUid viewer)
    {
        var state = new WGRecoveryPadState();
        if (PadFor(console) is not { } pad)
            return state;

        state.HasPad = true;
        state.Allowed = CanUse(viewer, pad);
        state.CanConfigure = CanConfigure(viewer, pad);
        state.Access = pad.Comp.Access;
        state.Charging = pad.Comp.Target is { } target && Exists(target) ? Name(target) : null;
        state.ChargeEnds = pad.Comp.ChargeEnds;
        state.ReadyAt = pad.Comp.ReadyAt;
        if (state.Allowed)
            state.People = People(pad);
        return state;
    }

    private void SendState(EntityUid console, EntityUid viewer)
    {
        _ui.ServerSendUiMessage(console, WGRecoveryPadUiKey.Key, new WGRecoveryPadStateMessage(State(console, viewer)), viewer);
    }

    #endregion
}
