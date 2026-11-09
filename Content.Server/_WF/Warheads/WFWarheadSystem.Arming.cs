using Content.Server._WF.Warheads.Components;
using Content.Server.DeviceLinking.Systems;
using Content.Server.Explosion.EntitySystems;
using Content.Shared._WF.Warheads;
using Content.Shared.Access.Systems;
using Content.Shared.DeviceLinking.Events;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Systems;
using Content.Shared.Tools.Systems;
using Content.Shared.Verbs;

namespace Content.Server._WF.Warheads;

public sealed partial class WFWarheadSystem
{
    [Dependency] private AccessReaderSystem _access = default!;
    [Dependency] private DeviceLinkSystem _deviceLink = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private Content.Shared.Containers.ItemSlots.ItemSlotsSystem _itemSlots = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedToolSystem _tool = default!;

    private void InitializeArming()
    {
        SubscribeLocalEvent<WFWarheadComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<WFWarheadComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<WFWarheadComponent, WFWarheadDisarmDoAfterEvent>(OnDisarmDoAfter);

        SubscribeLocalEvent<WFTimerFuzeComponent, WFWarheadArmDoAfterEvent>(OnArmDoAfter);

        SubscribeLocalEvent<WFRemoteFuzeComponent, MapInitEvent>(OnRemoteMapInit);
        SubscribeLocalEvent<WFRemoteFuzeComponent, WFWarheadSafetyDoAfterEvent>(OnSafetyDoAfter);
        SubscribeLocalEvent<WFRemoteFuzeComponent, SignalReceivedEvent>(OnSignal);

        SubscribeLocalEvent<WFImpactFuzeComponent, TriggerEvent>(OnImpactTrigger);

        SubscribeLocalEvent<WFDeadManTriggerComponent, MapInitEvent>(OnDeadManMapInit);
        SubscribeLocalEvent<WFDeadManTriggerComponent, GetVerbsEvent<AlternativeVerb>>(OnDeadManVerbs);
        SubscribeLocalEvent<WFDeadManTriggerComponent, WFWarheadSafetyDoAfterEvent>(OnDeadManReleaseDoAfter);
        SubscribeLocalEvent<WFDeadManTriggerComponent, ExaminedEvent>(OnDeadManExamined);
    }

    private void OnExamined(Entity<WFWarheadComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        args.PushMarkup(Loc.GetString("wf-warhead-examine-state-" + ent.Comp.State.ToString().ToLowerInvariant()));

        if (ent.Comp.State is WFWarheadState.Countdown or WFWarheadState.Final)
        {
            var left = Math.Max(0, (int) Math.Ceiling((ent.Comp.DetonateAt - _timing.CurTime).TotalSeconds));
            args.PushMarkup(Loc.GetString("wf-warhead-examine-time", ("seconds", left)));
        }
        else if (HasComp<WFTimerFuzeComponent>(ent))
        {
            args.PushMarkup(Loc.GetString("wf-warhead-examine-timer", ("seconds", (int) ent.Comp.Timer)));
        }

        if (HasComp<WFTimerFuzeComponent>(ent))
            args.PushMarkup(Loc.GetString("wf-warhead-examine-fuze-timer"));
        if (HasComp<WFTwoPersonArmingComponent>(ent))
            args.PushMarkup(Loc.GetString("wf-warhead-examine-fuze-two-person"));
        if (HasComp<WFRemoteFuzeComponent>(ent))
            args.PushMarkup(Loc.GetString("wf-warhead-examine-fuze-remote"));
        if (HasComp<WFImpactFuzeComponent>(ent))
            args.PushMarkup(Loc.GetString("wf-warhead-examine-fuze-impact"));
        if (TryComp<WFWarheadKeyLockComponent>(ent, out var keyLock))
            args.PushMarkup(Loc.GetString(_itemSlots.GetItemOrNull(ent, keyLock.Slot) != null
                ? "wf-warhead-examine-key-in" : "wf-warhead-examine-key-out"));
    }

    /// <summary>Whether a keyed warhead has its own key in; says so to the user when it doesn't. True for one with no keyhole.</summary>
    public bool KeyIn(EntityUid warhead, EntityUid? user)
    {
        if (!TryComp<WFWarheadKeyLockComponent>(warhead, out var keyLock))
            return true;

        if (_itemSlots.GetItemOrNull(warhead, keyLock.Slot) is { } key
            && TryComp<WFNukeKeyComponent>(key, out var cut) && cut.Warhead == warhead)
            return true;

        if (user != null)
            _popup.PopupEntity(Loc.GetString("wf-warhead-key-missing"), warhead, user.Value);
        return false;
    }

    private void OnInteractUsing(Entity<WFWarheadComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !_tool.HasQuality(args.Used, ent.Comp.DisarmQuality))
            return;

        switch (ent.Comp.State)
        {
            case WFWarheadState.Safe:
                return;
            case WFWarheadState.Final:
                _popup.PopupEntity(Loc.GetString("wf-warhead-disarm-too-late"), ent, args.User);
                args.Handled = true;
                return;
        }

        args.Handled = _tool.UseTool(args.Used, args.User, ent, ent.Comp.DisarmDelay, ent.Comp.DisarmQuality,
            new WFWarheadDisarmDoAfterEvent());
    }

    private void OnDisarmDoAfter(Entity<WFWarheadComponent> ent, ref WFWarheadDisarmDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;
        if (ent.Comp.State == WFWarheadState.Final)
        {
            _popup.PopupEntity(Loc.GetString("wf-warhead-disarm-too-late"), ent, args.User);
            return;
        }

        Disarm(ent, args.User);
        _popup.PopupEntity(Loc.GetString("wf-warhead-disarmed"), ent, args.User);
    }

    private void OnArmDoAfter(Entity<WFTimerFuzeComponent> ent, ref WFWarheadArmDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || !TryComp<WFWarheadComponent>(ent, out var warhead)
            || warhead.State != WFWarheadState.Safe)
            return;

        args.Handled = true;
        StartCountdown((ent, warhead), args.User);
        _popup.PopupEntity(Loc.GetString("wf-warhead-armed", ("seconds", (int) warhead.Timer)), ent, args.User);
    }

    /// <summary>One key turn. A second, different person within the window starts the countdown.</summary>
    public void TurnKey(Entity<WFTwoPersonArmingComponent> keys, Entity<WFWarheadComponent> warhead, EntityUid user)
    {
        if (!_access.IsAllowed(user, keys))
        {
            _popup.PopupEntity(Loc.GetString("wf-warhead-key-denied"), keys, user);
            return;
        }

        var now = _timing.CurTime;
        var fresh = keys.Comp.FirstKey is { } first
                    && first != user
                    && now - keys.Comp.FirstKeyTime <= TimeSpan.FromSeconds(keys.Comp.Window);

        if (!fresh)
        {
            keys.Comp.FirstKey = user;
            keys.Comp.FirstKeyTime = now;
            _popup.PopupEntity(Loc.GetString("wf-warhead-key-first", ("window", (int) keys.Comp.Window)), keys, user);
            UpdateUi(warhead, warhead.Comp);
            return;
        }

        keys.Comp.FirstKey = null;
        StartCountdown(warhead, user);
        _popup.PopupEntity(Loc.GetString("wf-warhead-armed", ("seconds", (int) warhead.Comp.Timer)), keys, user);
    }

    private void OnRemoteMapInit(Entity<WFRemoteFuzeComponent> ent, ref MapInitEvent args)
    {
        _deviceLink.EnsureSinkPorts(ent, ent.Comp.Port);
    }

    private void OnSafetyDoAfter(Entity<WFRemoteFuzeComponent> ent, ref WFWarheadSafetyDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || !TryComp<WFWarheadComponent>(ent, out var warhead))
            return;

        args.Handled = true;
        switch (warhead.State)
        {
            case WFWarheadState.Safe:
                SetSafety((ent, warhead), false, args.User);
                _popup.PopupEntity(Loc.GetString("wf-warhead-safety-off"), ent, args.User);
                break;
            case WFWarheadState.Armed:
                SetSafety((ent, warhead), true, args.User);
                _popup.PopupEntity(Loc.GetString("wf-warhead-safety-on"), ent, args.User);
                break;
        }
    }

    private void OnSignal(Entity<WFRemoteFuzeComponent> ent, ref SignalReceivedEvent args)
    {
        if (args.Port != ent.Comp.Port || !TryComp<WFWarheadComponent>(ent, out var warhead)
            || warhead.State != WFWarheadState.Armed)
            return;

        StartCountdown((ent, warhead), args.Trigger, ent.Comp.SignalTimer);
    }

    private void OnImpactTrigger(Entity<WFImpactFuzeComponent> ent, ref TriggerEvent args)
    {
        if (!TryComp<WFWarheadComponent>(ent, out var warhead) || GetPayload(warhead) is not { } payload)
            return;

        args.Handled = true;
        Detonate(ent, payload, args.User);
    }

    private void OnDeadManMapInit(Entity<WFDeadManTriggerComponent> ent, ref MapInitEvent args)
    {
        _deviceLink.EnsureSourcePorts(ent, ent.Comp.Port);
    }

    private void OnDeadManVerbs(Entity<WFDeadManTriggerComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || !_hands.IsHolding(args.User, ent))
            return;

        var user = args.User;
        if (!ent.Comp.Gripped)
        {
            args.Verbs.Add(new AlternativeVerb
            {
                Text = Loc.GetString("wf-deadman-verb-grip"),
                Act = () =>
                {
                    ent.Comp.Gripped = true;
                    ent.Comp.Holder = user;
                    _popup.PopupEntity(Loc.GetString("wf-deadman-gripped"), ent, user);
                    _adminLog.Add(Shared.Database.LogType.Explosion, Shared.Database.LogImpact.High,
                        $"{ToPrettyString(user)} gripped dead-man switch {ToPrettyString(ent)}");
                },
            });
            return;
        }

        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("wf-deadman-verb-release"),
            Act = () =>
            {
                _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, user, ent.Comp.ReleaseDelay,
                    new WFWarheadSafetyDoAfterEvent(), ent, used: ent)
                {
                    BreakOnMove = false,
                    NeedHand = true,
                });
            },
        });
    }

    private void OnDeadManReleaseDoAfter(Entity<WFDeadManTriggerComponent> ent, ref WFWarheadSafetyDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || !ent.Comp.Gripped)
            return;

        args.Handled = true;
        ent.Comp.Gripped = false;
        ent.Comp.Holder = null;
        _popup.PopupEntity(Loc.GetString("wf-deadman-released-safely"), ent, args.User);
    }

    private void OnDeadManExamined(Entity<WFDeadManTriggerComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString(ent.Comp.Gripped ? "wf-deadman-examine-gripped" : "wf-deadman-examine-loose"));
    }

    /// <summary>Fires every gripped switch whose holder has let go or gone down.</summary>
    private void UpdateDeadMan()
    {
        var query = EntityQueryEnumerator<WFDeadManTriggerComponent>();
        while (query.MoveNext(out var uid, out var trigger))
        {
            if (!trigger.Gripped)
                continue;

            if (trigger.Holder is { } holder
                && !TerminatingOrDeleted(holder)
                && _hands.IsHolding(holder, uid)
                && !_mobState.IsIncapacitated(holder))
                continue;

            trigger.Gripped = false;
            trigger.Holder = null;
            _adminLog.Add(Shared.Database.LogType.Explosion, Shared.Database.LogImpact.Extreme,
                $"Dead-man switch {ToPrettyString(uid)} was let go");
            _deviceLink.InvokePort(uid, trigger.Port);
        }
    }
}
