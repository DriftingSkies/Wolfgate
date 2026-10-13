using Content.Server._WF.Encounters.Components;
using Content.Server._WF.ShipPa;
using Content.Server.Administration.Logs;
using Content.Server.Power.EntitySystems;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Shared.Database;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Server._WF.Encounters.Systems;

/// <summary>
/// Docking clamps on raiders: a ship one docks with is held, unable to undock from it or jump, until someone releases
/// the clamps at the raider's helm or the raider has no powered helm left.
/// </summary>
public sealed class WFDockingClampSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private ShipPaSystem _pa = default!;

    /// <summary>How long clamps released by hand stay open, so the ship that got free is not bitten again at once.</summary>
    public static readonly TimeSpan OpenTime = TimeSpan.FromMinutes(10);

    private static readonly SoundSpecifier ClampSound = new SoundPathSpecifier("/Audio/Machines/airlock_deny.ogg");
    private static readonly Color PaColor = Color.FromHex("#ff6040");

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DockEvent>(OnDock);
        SubscribeLocalEvent<UndockEvent>(OnUndock);
        SubscribeLocalEvent<ConsoleFTLAttemptEvent>(OnFtlAttempt);
        SubscribeLocalEvent<GetVerbsEvent<AlternativeVerb>>(OnGetVerbs);
    }

    private void OnDock(DockEvent args)
    {
        Bite(args.GridAUid, args.GridBUid, args.DockA.Owner);
        Bite(args.GridBUid, args.GridAUid, args.DockB.Owner);
    }

    private void OnUndock(UndockEvent args)
    {
        LetGo(args.GridAUid, args.GridBUid);
        LetGo(args.GridBUid, args.GridAUid);
    }

    /// <summary>A raider's clamps take hold of the ship it has docked with, if they are shut and it has power.</summary>
    private void Bite(EntityUid raider, EntityUid prey, EntityUid port)
    {
        if (!TryComp<WFDockingClampComponent>(raider, out var clamps) || HasComp<WFDockingClampComponent>(prey)
            || _timing.CurTime < clamps.OpenUntil || !HasHelm(raider) || !clamps.Held.Add(prey))
            return;

        _audio.PlayPvs(ClampSound, port);
        _pa.Announce(prey, Loc.GetString("wf-clamps-engaged", ("ship", Name(raider))), sender: Loc.GetString("wf-clamps-sender"),
            color: PaColor);
    }

    private void LetGo(EntityUid raider, EntityUid prey)
    {
        if (TryComp<WFDockingClampComponent>(raider, out var clamps))
            clamps.Held.Remove(prey);
    }

    /// <summary>Whether a ship is held by some raider's clamps right now. Clamps on a raider with no powered helm let go.</summary>
    public bool IsHeld(EntityUid ship)
    {
        var query = EntityQueryEnumerator<WFDockingClampComponent>();
        while (query.MoveNext(out var raider, out var clamps))
        {
            if (clamps.Held.Contains(ship) && Holding((raider, clamps)))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Whether an undock asked for at a console is refused: the port joins a raider with a ship its clamps hold, and
    /// the console is not the raider's own. The person at the console is told why.
    /// </summary>
    public bool Refuses(EntityUid console, Entity<DockingComponent> dock)
    {
        if (dock.Comp.DockedWith is not { } other || Transform(dock).GridUid is not { } here
            || Transform(other).GridUid is not { } there || Transform(console).GridUid is not { } asking)
            return false;

        if (!Clamped(here, there, asking) && !Clamped(there, here, asking))
            return false;

        _popup.PopupEntity(Loc.GetString("wf-clamps-refuse"), console, PopupType.MediumCaution);
        return true;
    }

    private bool Clamped(EntityUid raider, EntityUid prey, EntityUid asking)
    {
        return asking != raider && TryComp<WFDockingClampComponent>(raider, out var clamps) && clamps.Held.Contains(prey)
            && Holding((raider, clamps));
    }

    /// <summary>Whether a raider's clamps still have power; without it they open and everything held is let go.</summary>
    private bool Holding(Entity<WFDockingClampComponent> raider)
    {
        if (raider.Comp.Held.Count == 0)
            return false;

        if (HasHelm(raider))
            return true;

        Release(raider, null);
        return false;
    }

    /// <summary>Opens a raider's clamps. Released by hand they stay open a while; a power loss only frees what is held.</summary>
    public void Release(Entity<WFDockingClampComponent> raider, EntityUid? user)
    {
        foreach (var prey in raider.Comp.Held)
        {
            if (!TerminatingOrDeleted(prey))
                _pa.Announce(prey, Loc.GetString("wf-clamps-released"), sender: Loc.GetString("wf-clamps-sender"), color: PaColor);
        }

        raider.Comp.Held.Clear();
        if (user == null)
            return;

        raider.Comp.OpenUntil = _timing.CurTime + OpenTime;
        _adminLog.Add(LogType.Action, LogImpact.Low, $"{ToPrettyString(user.Value):player} released the docking clamps of {ToPrettyString(raider):grid}");
    }

    private bool HasHelm(EntityUid grid)
    {
        var consoles = EntityQueryEnumerator<ShuttleConsoleComponent, TransformComponent>();
        while (consoles.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid == grid && this.IsPowered(uid, EntityManager))
                return true;
        }

        return false;
    }

    private void OnFtlAttempt(ref ConsoleFTLAttemptEvent args)
    {
        if (args.Cancelled || !IsHeld(args.Uid))
            return;

        args.Cancelled = true;
        args.Reason = Loc.GetString("wf-clamps-refuse");
    }

    private void OnGetVerbs(GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || !HasComp<ShuttleConsoleComponent>(args.Target)
            || Transform(args.Target).GridUid is not { } grid || !TryComp<WFDockingClampComponent>(grid, out var clamps)
            || clamps.Held.Count == 0)
            return;

        var user = args.User;
        var target = args.Target;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("wf-clamps-release-verb"),
            Priority = 5,
            Act = () =>
            {
                Release((grid, clamps), user);
                _popup.PopupEntity(Loc.GetString("wf-clamps-release-done"), target, user);
            },
        });
    }
}
