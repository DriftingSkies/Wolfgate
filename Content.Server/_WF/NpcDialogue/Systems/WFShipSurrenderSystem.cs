using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Encounters.Systems;
using Content.Server._WF.Hailing;
using Content.Server._WF.NpcCrew;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server._WF.NpcDialogue.Components;
using Content.Server.Administration.Logs;
using Content.Server.NPC.HTN;
using Content.Shared._WF.Hailing;
using Content.Shared._WF.NpcCrew;
using Content.Shared.CombatMode.Pacification;
using Content.Shared.Database;
using Robust.Shared.Timing;

namespace Content.Server._WF.NpcDialogue.Systems;

/// <summary>
/// A beaten encounter ship may give up to the player vessel that beat it. Its crew stand down, holster and stop
/// fighting, the ship holds still and counts as out of the fight, and it hails the victor to accept. Refused, or fired
/// on once it has given up, it fights to the end and never offers again.
/// </summary>
public sealed partial class WFShipSurrenderSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private HailingSystem _hailing = default!;
    [Dependency] private HTNSystem _htn = default!;
    [Dependency] private WFCrewAlertSystem _alerts = default!;
    [Dependency] private WFCrewObjectiveSystem _objectives = default!;
    [Dependency] private WFCrewSystem _crew = default!;
    [Dependency] private WFDockingClampSystem _clamps = default!;
    [Dependency] private WFEncounterSystem _encounters = default!;
    [Dependency] private WFNpcDialogueSystem _dialogue = default!;

    private const string Accept = "surrender-accept";
    private const string Refuse = "surrender-refuse";

    /// <summary>Fire landing this soon after the surrender was already in flight.</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromSeconds(10);

    /// <summary>How long the last line of the surrender hail stays up before the channel closes.</summary>
    private static readonly TimeSpan CloseDelay = TimeSpan.FromSeconds(4);

    private static readonly TimeSpan HoldCheck = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    private TimeSpan _next = TimeSpan.FromSeconds(0.7);

    /// <summary>The crewman each surrender hail is made by.</summary>
    private readonly Dictionary<EntityUid, EntityUid> _speakers = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFCrewHullHitEvent>(OnHullHit);
        SubscribeLocalEvent<HailAnsweredEvent>(OnAnswered);
        SubscribeLocalEvent<HailOptionChosenEvent>(OnChosen);
        SubscribeLocalEvent<HailCallEndedEvent>(OnCallEnded);
    }

    /// <summary>
    /// The ship gives up to <paramref name="victor"/>: crew stand down, the ship holds still, and it says so on the
    /// radio and over a hail. False when it has nobody left to surrender.
    /// </summary>
    public bool Surrender(Entity<WFEncounterComponent> encounter, WFEncounterShipState ship, EntityUid victor)
    {
        if (ship.Surrendered || TerminatingOrDeleted(ship.Grid) || _dialogue.CrewAboard(ship) == 0)
            return false;

        var now = _timing.CurTime;
        ship.Surrendered = true;
        ship.SurrenderedTo = victor;
        ship.SurrenderedAt = now;
        ship.SurrenderAccepted = false;
        ship.Retreating = false;
        EnsureComp<WFCrewSurrenderedComponent>(ship.Grid).Group = ship.Group;
        _alerts.StandDown(ship.Grid, ship.Group);
        StandDownCrew(ship);
        HoldStill(ship);

        // A raider lets go of the ship it had clamped.
        if (TryComp<WFDockingClampComponent>(ship.Grid, out var clamps) && clamps.Held.Count > 0)
            _clamps.Release((ship.Grid, clamps), null);

        _dialogue.Say(encounter.Comp, ship, "surrender", victor, WFNpcRadioPriority.High);
        if (_encounters.TryFindHelm(ship, out var console, out var speaker)
            && _hailing.TryHailAutomated(console, speaker, victor))
        {
            ship.SurrenderConsole = console;
            _speakers[console] = speaker;
        }

        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"Encounter ship {ToPrettyString(ship.Grid):entity} surrendered to {ToPrettyString(victor):entity}");
        return true;
    }

    /// <summary>
    /// The ship takes back its surrender and fights <paramref name="attacker"/> to the end, saying
    /// <paramref name="key"/> if given.
    /// </summary>
    public void Resume(Entity<WFEncounterComponent> encounter, WFEncounterShipState ship, EntityUid attacker, string? key)
    {
        if (!ship.Surrendered)
            return;

        ship.Surrendered = false;
        ship.SurrenderAccepted = false;
        ship.LastStand = true;
        ship.Mood = WFShipMood.Broken;
        ship.SpokenMood = WFShipMood.Broken;
        RemComp<WFCrewSurrenderedComponent>(ship.Grid);
        var crew = EntityQueryEnumerator<WFSurrenderedCrewComponent, WFCrewComponent>();
        while (crew.MoveNext(out var uid, out var held, out var member))
        {
            if (member.Group != ship.Group || _crew.HomeGrid(uid, member) != ship.Grid)
                continue;

            member.Engagement = held.Engagement;
            if (held.Duty != member.Duty)
                _crew.SetDuty((uid, member), held.Duty);
            if (held.Pacified)
                RemComp<PacifiedComponent>(uid);
            RemComp<WFSurrenderedCrewComponent>(uid);
            if (TryComp<HTNComponent>(uid, out var htn))
                _htn.Replan(htn);
        }

        if (key != null)
            _dialogue.Say(encounter.Comp, ship, key, attacker, WFNpcRadioPriority.High);
        _alerts.ReportShipThreat(ship.Grid, ship.Group, attacker);
        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"Encounter ship {ToPrettyString(ship.Grid):entity} took back its surrender and fights {ToPrettyString(attacker):entity}");
    }

    /// <summary>Every living crewman of the ship stops fighting and puts his guns away; gunners leave the guns.</summary>
    private void StandDownCrew(WFEncounterShipState ship)
    {
        var crew = EntityQueryEnumerator<WFCrewComponent>();
        while (crew.MoveNext(out var uid, out var member))
        {
            if (member.Group != ship.Group || _crew.HomeGrid(uid, member) != ship.Grid || HasComp<WFSurrenderedCrewComponent>(uid))
                continue;

            var held = EnsureComp<WFSurrenderedCrewComponent>(uid);
            held.Engagement = member.Engagement;
            held.Duty = member.Duty;
            held.Pacified = !HasComp<PacifiedComponent>(uid);
            member.Engagement = WFCrewEngagement.Never;
            if (member.Duty == WFCrewDuties.Gunnery)
                _crew.SetDuty((uid, member), WFCrewDuties.Guard);
            EnsureComp<PacifiedComponent>(uid);
            if (TryComp<WFPilotDutyComponent>(uid, out var pilot))
                pilot.FleeAttackers = false;
            if (TryComp<HTNComponent>(uid, out var htn))
                _htn.Replan(htn);
        }
    }

    /// <summary>Gives the ship a hold with no end, so it lies still to be boarded.</summary>
    private void HoldStill(WFEncounterShipState ship)
    {
        var hold = new List<WFCrewObjective> { new() { Kind = WFCrewObjectiveKind.Hold } };
        if (!_objectives.SetQueue(ship.Grid, ship.Group, hold))
            return;

        ship.HasOrders = true;
        ship.Flown = false;
        ship.Fleeing = false;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        if (now < _next)
            return;

        _next = now + Interval;
        var query = EntityQueryEnumerator<WFEncounterComponent>();
        while (query.MoveNext(out _, out var encounter))
        {
            foreach (var ship in encounter.Ships.Values)
            {
                if (!ship.Surrendered || TerminatingOrDeleted(ship.Grid))
                    continue;

                if (ship.SurrenderCloseAt is { } close && now >= close)
                    EndHail(ship);

                if (now < ship.NextSurrenderHold)
                    continue;

                // The captain may put back the course it saved when its alert ended; the ship stays where it gave up.
                ship.NextSurrenderHold = now + HoldCheck;
                if (Underway(ship))
                    HoldStill(ship);
            }
        }
    }

    /// <summary>Whether a pilot of the ship is flying anything but a hold.</summary>
    private bool Underway(WFEncounterShipState ship)
    {
        var pilots = EntityQueryEnumerator<WFPilotDutyComponent, WFCrewComponent>();
        while (pilots.MoveNext(out var uid, out var duty, out var member))
        {
            if (member.Group == ship.Group && _crew.HomeGrid(uid, member) == ship.Grid && duty.Orders != WFPilotOrder.Hold)
                return true;
        }

        return false;
    }

    /// <summary>A player vessel firing on a ship that gave up, once the shots already in flight have landed, is a betrayal.</summary>
    private void OnHullHit(ref WFCrewHullHitEvent args)
    {
        if (!HasComp<WFCrewSurrenderedComponent>(args.Grid) || !_dialogue.IsPlayerVessel(args.AttackerGrid)
            || !TryShip(args.Grid, out var encounter, out var ship)
            || _timing.CurTime - ship.SurrenderedAt < Grace)
            return;

        EndHail(ship);
        Resume(encounter, ship, args.AttackerGrid, "surrender-betrayed");
    }

    private void OnAnswered(ref HailAnsweredEvent args)
    {
        if (!TryHail(args.Grid, args.Console, out var encounter, out var ship) || !_speakers.TryGetValue(args.Console, out var speaker))
            return;

        var victor = ship.SurrenderedTo is { } to ? _dialogue.NameOf(to) : string.Empty;
        _hailing.Say(args.Console, speaker, SurrenderLine(encounter.Comp, ship, "surrender-hail", victor));
        _hailing.Offer(args.Console, speaker, new List<HailOption>
        {
            new(Accept, Loc.GetString("wf-npc-surrender-option-accept")),
            new(Refuse, Loc.GetString("wf-npc-surrender-option-refuse")),
        });
    }

    private void OnChosen(ref HailOptionChosenEvent args)
    {
        if (args.Id is not (Accept or Refuse) || !TryHail(args.OffererGrid, args.Offerer, out var encounter, out var ship)
            || !_speakers.TryGetValue(args.Offerer, out var speaker))
            return;

        var victor = args.ChooserGrid is { } chooser ? _dialogue.NameOf(chooser) : string.Empty;
        _hailing.ClearOptions(args.Offerer);
        ship.SurrenderCloseAt = _timing.CurTime + CloseDelay;
        if (args.Id == Accept)
        {
            ship.SurrenderAccepted = true;
            _hailing.Say(args.Offerer, speaker, SurrenderLine(encounter.Comp, ship, "surrender-accepted", victor));
            _adminLog.Add(LogType.Action, LogImpact.Low,
                $"{ToPrettyString(args.ChooserEntity):player} accepted the surrender of {ToPrettyString(ship.Grid):entity}");
            return;
        }

        _hailing.Say(args.Offerer, speaker, SurrenderLine(encounter.Comp, ship, "surrender-refused", victor));
        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"{ToPrettyString(args.ChooserEntity):player} refused the surrender of {ToPrettyString(ship.Grid):entity}");
        Resume(encounter, ship, args.ChooserGrid ?? ship.SurrenderedTo ?? args.Chooser, null);
    }

    private void OnCallEnded(ref HailCallEndedEvent args)
    {
        if (!_speakers.Remove(args.Console))
            return;

        if (TryHail(args.Grid, args.Console, out _, out var ship))
        {
            ship.SurrenderConsole = null;
            ship.SurrenderCloseAt = null;
        }
    }

    /// <summary>Closes the surrender hail, if it is still open.</summary>
    private void EndHail(WFEncounterShipState ship)
    {
        ship.SurrenderCloseAt = null;
        if (ship.SurrenderConsole is not { } console)
            return;

        ship.SurrenderConsole = null;
        _speakers.Remove(console);
        if (_hailing.GetState(console) == null)
            return;

        _hailing.ClearOptions(console);
        _hailing.End(console);
    }

    private string SurrenderLine(WFEncounterComponent encounter, WFEncounterShipState ship, string key, string victor)
    {
        return _dialogue.LineFor(_dialogue.Talk(encounter, ship.Side, ship.SurrenderedTo ?? ship.Grid), ship, key, victor);
    }

    /// <summary>The surrendered encounter ship whose surrender hail goes through a console.</summary>
    private bool TryHail(EntityUid? grid, EntityUid console, out Entity<WFEncounterComponent> encounter,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out WFEncounterShipState? ship)
    {
        ship = null;
        encounter = default;
        if (grid is not { } uid || !TryShip(uid, out encounter, out var found) || found.SurrenderConsole != console
            || !found.Surrendered)
            return false;

        ship = found;
        return true;
    }

    /// <summary>The encounter ship a grid is.</summary>
    private bool TryShip(EntityUid grid, out Entity<WFEncounterComponent> encounter,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out WFEncounterShipState? ship)
    {
        encounter = default;
        ship = null;
        if (!TryComp<WFEncounterGridComponent>(grid, out var marker) || !TryComp<WFEncounterComponent>(marker.Encounter, out var comp)
            || !comp.Ships.TryGetValue(marker.Key, out ship))
            return false;

        encounter = (marker.Encounter, comp);
        return true;
    }
}
