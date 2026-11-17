using System.Linq;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Server._WG.ShipCommand;
using Content.Shared._WG.Drydock;
using Content.Shared._WG.ShipCommand;
using Content.Shared.Database;
using Content.Shared.DeviceLinking;
using Robust.Server.Player;

namespace Content.Server._WG.Drydock;

/// <summary>
/// The drydock terminal (<see cref="WGDrydockTerminalComponent"/>): one seat for every berth console multitool-linked
/// to it on its grid. It forwards the berth console's own messages (repair, refit, clean Drift, break, berth lights),
/// stamped with the berth, to that console's own handlers, so pricing, power, blockers and jobs are the console's: the
/// operator pays as at a walk-in console. A berth can be locked: it refuses walk-in work and is left out of bookings,
/// and only a terminal works it. On a commanded ship the ship's standings decide (officers operate and lock berths,
/// whoever configures the ship sets the price, which is the ship's <c>drydock</c> price rule); elsewhere anyone operates
/// and only admins lock.
/// </summary>
public sealed partial class WGDrydockSystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IAdminManager _admin = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private WGShipCommandSystem _shipCommand = default!;

    /// <summary>The name of the commanded ship's price rule the terminal sets: everyone, drydock work only.</summary>
    public const string PriceRule = "drydock";

    /// <summary>Highest drydock price multiplier the terminal sets.</summary>
    public const float MaxMultiplier = 10f;

    /// <summary>Set while a terminal forwards a message, so a locked berth takes it.</summary>
    private bool _fromTerminal;

    private TimeSpan _nextTerminalRefresh;

    private void InitializeTerminal()
    {
        SubscribeLocalEvent<WGDrydockTerminalComponent, WGDrydockTerminalRefreshMessage>(OnTerminalRefresh);
        SubscribeLocalEvent<WGDrydockTerminalComponent, WGDrydockTerminalLockMessage>(OnTerminalLock);
        SubscribeLocalEvent<WGDrydockTerminalComponent, WGDrydockTerminalPriceMessage>(OnTerminalPrice);
        SubscribeLocalEvent<WGDrydockTerminalComponent, WGDrydockRepairMessage>((uid, _, args) =>
            Forward(uid, args, (console, comp) => OnRepair(console, comp, args)));
        SubscribeLocalEvent<WGDrydockTerminalComponent, WGDrydockUpgradeMessage>((uid, _, args) =>
            Forward(uid, args, (console, comp) => OnUpgrade(console, comp, args)));
        SubscribeLocalEvent<WGDrydockTerminalComponent, WGDrydockCleanDriftMessage>((uid, _, args) =>
            Forward(uid, args, (console, comp) => OnCleanDrift(console, comp, args)));
        SubscribeLocalEvent<WGDrydockTerminalComponent, WGDrydockBreakMessage>((uid, _, args) =>
            Forward(uid, args, (console, comp) => OnBreak(console, comp, args)));
        SubscribeLocalEvent<WGDrydockTerminalComponent, WGDrydockToggleBerthLightsMessage>((uid, _, args) =>
            Forward(uid, args, (console, comp) => OnToggleBerthLights(console, comp, args)));
    }

    #region Berths and standing

    /// <summary>The berth consoles linked to a terminal on its own grid, by berth name.</summary>
    public List<EntityUid> TerminalBerths(EntityUid terminal)
    {
        var berths = new List<EntityUid>();
        if (!TryComp<DeviceLinkSourceComponent>(terminal, out var source))
            return berths;

        var grid = Transform(terminal).GridUid;
        foreach (var sink in _deviceLink.GetLinkedSinks((terminal, source), WGDrydockTerminalComponent.BerthPort))
        {
            if (HasComp<WGDrydockConsoleComponent>(sink) && Transform(sink).GridUid == grid)
                berths.Add(sink);
        }

        berths.Sort((a, b) => string.Compare(BerthLabel(a, Comp<WGDrydockConsoleComponent>(a)),
            BerthLabel(b, Comp<WGDrydockConsoleComponent>(b)), StringComparison.OrdinalIgnoreCase));
        return berths;
    }

    /// <summary>Whether someone may start and stop work from the terminal: officers and up aboard a commanded ship, anyone elsewhere.</summary>
    public bool CanOperateTerminal(EntityUid terminal, EntityUid user)
    {
        return Transform(terminal).GridUid is not { } grid || !HasComp<WGShipCommandComponent>(grid)
               || _shipCommand.CanOperate(user, grid);
    }

    /// <summary>Whether someone may lock and unlock berths: officers and up aboard a commanded ship, active admins elsewhere.</summary>
    public bool CanLockBerths(EntityUid terminal, EntityUid user)
    {
        return Transform(terminal).GridUid is { } grid && HasComp<WGShipCommandComponent>(grid)
            ? _shipCommand.CanOperate(user, grid)
            : CanConfigureTerminal(terminal, user);
    }

    /// <summary>
    /// Whether someone may set the price: whoever configures a commanded ship, an active admin (not event staff)
    /// elsewhere.
    /// </summary>
    public bool CanConfigureTerminal(EntityUid terminal, EntityUid user)
    {
        if (Transform(terminal).GridUid is { } grid && HasComp<WGShipCommandComponent>(grid))
            return _shipCommand.CanConfigure(user, grid);

        return _player.TryGetSessionByEntity(user, out var session)
               && WGShipCommandSystem.CountsAsAdmin(_admin.GetAdminData(session), false);
    }

    /// <summary>
    /// A locked berth refuses a walk-in caller, and a commanded ship's berths refuse the barred; the terminal's own
    /// forwarded messages pass the lock.
    /// </summary>
    private bool WalkInLocked(EntityUid console, WGDrydockConsoleComponent comp, EntityUid actor)
    {
        if (_shipServices.Refuses(console, actor, out var refusal))
        {
            Deny(console, comp, actor, refusal);
            return true;
        }

        if (!comp.Locked || _fromTerminal)
            return false;

        Deny(console, comp, actor, Loc.GetString("drydock-console-locked"));
        return true;
    }

    #endregion

    #region Messages

    /// <summary>
    /// Hands a berth console's message to that console's handler, when the berth it names is linked to the terminal and
    /// the caller may operate it. False when it was refused.
    /// </summary>
    private bool Forward(EntityUid terminal, WGDrydockBerthMessage args, Action<EntityUid, WGDrydockConsoleComponent> handle)
    {
        if (!CanOperateTerminal(terminal, args.Actor))
        {
            _popup.PopupCursor(Loc.GetString("drydock-terminal-not-cleared"), args.Actor);
            return false;
        }

        if (!TryGetEntity(args.Berth, out var found) || found is not { } console
            || !TerminalBerths(terminal).Contains(console) || !TryComp<WGDrydockConsoleComponent>(console, out var comp))
        {
            _popup.PopupCursor(Loc.GetString("drydock-terminal-no-berth"), args.Actor);
            return false;
        }

        _fromTerminal = true;
        try
        {
            handle(console, comp);
        }
        finally
        {
            _fromTerminal = false;
        }

        SendTerminalAll(terminal, force: true);
        return true;
    }

    /// <summary>Locks or unlocks one berth, or all of them. False when the user may not, or the berth isn't the terminal's.</summary>
    public bool SetBerthLock(EntityUid terminal, EntityUid user, EntityUid? berth, bool locked)
    {
        if (!CanLockBerths(terminal, user))
            return false;

        var berths = TerminalBerths(terminal);
        if (berth is { } one && !berths.Contains(one))
            return false;

        foreach (var console in berth is { } only ? new List<EntityUid> { only } : berths)
        {
            var comp = Comp<WGDrydockConsoleComponent>(console);
            if (comp.Locked == locked)
                continue;

            comp.Locked = locked;
            _adminLog.Add(LogType.Action, LogImpact.Medium,
                $"{ToPrettyString(user):actor} {(locked ? "locked" : "unlocked")} drydock berth {ToPrettyString(console):console} from {ToPrettyString(terminal):terminal}");
            UpdateUi(console, comp, force: true);
        }

        return true;
    }

    /// <summary>
    /// Sets the commanded ship's drydock price: a price rule for everyone on drydock work at this multiplier (0 free, 1
    /// the usual price), or, with null, no such rule. False when the user may not, the ship isn't commanded, or the
    /// multiplier is out of range.
    /// </summary>
    public bool SetTerminalPrice(EntityUid terminal, EntityUid user, float? multiplier)
    {
        if (Transform(terminal).GridUid is not { } grid || !TryComp<WGShipCommandComponent>(grid, out var command)
            || !CanConfigureTerminal(terminal, user)
            || multiplier is { } m && (!float.IsFinite(m) || m < 0 || m > MaxMultiplier))
            return false;

        var ship = (grid, command);
        if (multiplier is { } set)
        {
            _shipServices.SetRule(ship, new WGShipPriceRule
            {
                Name = PriceRule,
                Audience = WGShipPriceAudience.Everyone,
                Services = WGShipServiceKind.Drydock,
                Multiplier = set,
            }, user);
        }
        else
        {
            _shipServices.RemoveRule(ship, PriceRule, user);
        }

        return true;
    }

    private void OnTerminalRefresh(EntityUid uid, WGDrydockTerminalComponent comp, WGDrydockTerminalRefreshMessage args)
    {
        SendTerminalState(uid, args.Actor);
    }

    private void OnTerminalLock(EntityUid uid, WGDrydockTerminalComponent comp, WGDrydockTerminalLockMessage args)
    {
        EntityUid? berth = null;
        if (args.Berth != null && !TryGetEntity(args.Berth, out berth))
        {
            _popup.PopupCursor(Loc.GetString("drydock-terminal-no-berth"), args.Actor);
            return;
        }

        if (!SetBerthLock(uid, args.Actor, berth, args.Locked))
            _popup.PopupCursor(Loc.GetString("drydock-terminal-not-cleared"), args.Actor);
        SendTerminalAll(uid, force: true);
    }

    private void OnTerminalPrice(EntityUid uid, WGDrydockTerminalComponent comp, WGDrydockTerminalPriceMessage args)
    {
        if (!SetTerminalPrice(uid, args.Actor, args.Multiplier))
        {
            var commanded = Transform(uid).GridUid is { } grid && HasComp<WGShipCommandComponent>(grid);
            _popup.PopupCursor(commanded && CanConfigureTerminal(uid, args.Actor)
                ? Loc.GetString("drydock-terminal-bad-price", ("max", MaxMultiplier))
                : Loc.GetString("drydock-terminal-not-cleared"), args.Actor);
        }
        SendTerminalAll(uid, force: true);
    }

    #endregion

    #region State

    /// <summary>The terminal's state as one viewer sees it.</summary>
    public WGDrydockTerminalState TerminalState(EntityUid terminal, EntityUid viewer)
    {
        var state = new WGDrydockTerminalState
        {
            CanOperate = CanOperateTerminal(terminal, viewer),
            CanLock = CanLockBerths(terminal, viewer),
            CanConfigure = CanConfigureTerminal(terminal, viewer),
        };

        if (Transform(terminal).GridUid is { } grid && TryComp<WGShipCommandComponent>(grid, out var command))
        {
            state.Commanded = true;
            state.Multiplier = TerminalMultiplier(command);
        }

        foreach (var console in TerminalBerths(terminal))
        {
            var comp = Comp<WGDrydockConsoleComponent>(console);
            var berthState = TerminalBerthState(console, comp);
            berthState.Quote = QuoteFor(console, viewer, berthState);
            state.Berths.Add(new WGDrydockTerminalBerth
            {
                Console = GetNetEntity(console),
                Label = BerthLabel(console, comp),
                Locked = comp.Locked,
                State = berthState,
            });
        }

        return state;
    }

    /// <summary>A berth's console state as a terminal row shows it, before any viewer's prices.</summary>
    private WGDrydockConsoleState TerminalBerthState(EntityUid console, WGDrydockConsoleComponent comp)
    {
        var job = CompOrNull<WGDrydockJobComponent>(console);
        var ship = job?.Ship ?? GetBerthedShip(console, comp);
        if (ship is { } shown && TerminatingOrDeleted(shown))
            ship = null;   // a job's ship can be deleted before the job loop drops it this tick

        var state = BuildState(console, ship, comp, job);
        state.BerthLightsOn = comp.BerthLightsUntil != null;
        return state;
    }

    /// <summary>The commanded ship's drydock price rule's multiplier while it is on; null for the usual price.</summary>
    private static float? TerminalMultiplier(WGShipCommandComponent command)
    {
        return command.PriceRules.Find(r => r.Name == PriceRule && r.Enabled)?.Multiplier;
    }

    private void SendTerminalState(EntityUid terminal, EntityUid viewer)
    {
        _ui.ServerSendUiMessage(terminal, WGDrydockTerminalUiKey.Key, new WGDrydockTerminalStateMessage(TerminalState(terminal, viewer)), viewer);
    }

    /// <summary>
    /// Sends every viewer their state: when forced, or when a berth, the drydock price, or what a viewer may do or pays
    /// changed since the last push.
    /// </summary>
    private void SendTerminalAll(EntityUid terminal, bool force)
    {
        if (!TryComp<WGDrydockTerminalComponent>(terminal, out var comp))
            return;

        var actors = _ui.GetActors(terminal, WGDrydockTerminalUiKey.Key).ToList();
        if (actors.Count == 0)
            return;

        var berths = TerminalBerths(terminal).Select(console =>
        {
            var consoleComp = Comp<WGDrydockConsoleComponent>(console);
            return (Console: console, Comp: consoleComp, State: TerminalBerthState(console, consoleComp));
        }).ToList();
        var parts = berths.Select(berth => $"{berth.Console}:{berth.Comp.Locked}:{StateKey(berth.State)}").ToList();

        // A price rule set elsewhere, or a viewer's standing changing, shows in their window without a press.
        if (Transform(terminal).GridUid is { } grid && TryComp<WGShipCommandComponent>(grid, out var command))
            parts.Add($"x{TerminalMultiplier(command)}");

        foreach (var actor in actors)
        {
            parts.Add($"{actor}:{CanOperateTerminal(terminal, actor)}:{CanLockBerths(terminal, actor)}:{CanConfigureTerminal(terminal, actor)}:"
                      + string.Join(",", berths.Select(berth => QuoteKey(QuoteFor(berth.Console, actor, berth.State)))));
        }

        var key = string.Join("|", parts);
        if (!force && key == comp.LastPushed)
            return;

        comp.LastPushed = key;
        foreach (var actor in actors)
            SendTerminalState(terminal, actor);
    }

    /// <summary>Re-sends open terminals' state once a second when a berth changed, as the consoles do.</summary>
    private void RefreshOpenTerminals(TimeSpan curTime)
    {
        if (curTime < _nextTerminalRefresh)
            return;

        _nextTerminalRefresh = curTime + TimeSpan.FromSeconds(1);
        var query = EntityQueryEnumerator<WGDrydockTerminalComponent>();
        while (query.MoveNext(out var uid, out _))
            SendTerminalAll(uid, force: false);
    }

    #endregion
}
