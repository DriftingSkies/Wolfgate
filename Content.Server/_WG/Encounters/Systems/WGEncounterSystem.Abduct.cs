using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Server._WG.Encounters.Components;
using Content.Server._WG.Hailing;
using Content.Server._WG.NpcCrew.Components;
using Content.Server._WG.NpcCrew.Systems;
using Content.Shared._WG.Encounters;
using Content.Shared._WG.Hailing;
using Content.Shared._WG.NpcCrew;
using Content.Shared.Access.Components;
using Content.Shared.Database;
using Content.Shared.Doors.Components;
using Content.Shared.Ghost;
using Content.Shared.Popups;
using Robust.Shared.Player;
using Robust.Shared.Random;

namespace Content.Server._WG.Encounters.Systems;

/// <summary>
/// Abductors: a ship that roams, now and then docks with a lightly crewed player ship and takes someone off it alive.
/// Its hands stun, cuff and drag the prisoner to the chair in its cell, its guards hold the port, and it casts off to
/// hold them for a ransom. Anyone can pay it: the prisoner from the chair, or whoever answers its hail aboard their
/// ship. Word goes out to the sector and a warrant sits on the ship, so others can come for them. A prisoner nobody
/// pays for is sent home when its patience runs out.
/// </summary>
public sealed partial class WGEncounterSystem
{
    [Dependency] private WGAbductCellSystem _cells = default!;
    [Dependency] private WGCrewGrabberSystem _grabbers = default!;
    [Dependency] private WGCrewSecuritySystem _crewSecurity = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public const string AbductPay = "abduct-pay";
    public const string AbductRefuse = "abduct-refuse";

    /// <summary>How near its mark it closes before it goes in to dock.</summary>
    private const float AbductCloseRange = 600f;

    /// <summary>Failed runs at getting a port on a mark before it gives up.</summary>
    private const int AbductDockMisses = 3;

    /// <summary>Hails a prisoner's ship gets.</summary>
    private const int AbductHails = 3;

    /// <summary>How long it waits for someone to come and collect a prisoner paid for over the hail.</summary>
    private const int AbductHandoverMinutes = 10;

    /// <summary>What a ship holding someone shows on the mass scanner: a colour no other ship flies.</summary>
    private static readonly Color AbductScannerColor = Color.Magenta;

    private static readonly TimeSpan AbductHailEvery = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan AbductCloseDelay = TimeSpan.FromSeconds(4);

    /// <summary>How soon it looks again after finding no mark.</summary>
    private static readonly TimeSpan AbductLookAgain = TimeSpan.FromSeconds(90);

    /// <summary>How long it chases a mark without getting a port on it.</summary>
    private static readonly TimeSpan AbductChaseTime = TimeSpan.FromMinutes(10);

    /// <summary>How long its hands have to get someone to the chair.</summary>
    private static readonly TimeSpan AbductGrabTime = TimeSpan.FromMinutes(4);

    /// <summary>How long it leaves alone a ship it has been at, whichever way that went.</summary>
    private static readonly TimeSpan AbductSpare = TimeSpan.FromMinutes(30);

    private void InitializeAbduct()
    {
        SubscribeLocalEvent<WGAbductCellComponent, WGAbductRansomOfferedEvent>(OnRansomOffered);
    }

    private WGAbductState NewAbduct(WGEncounterAbduct config)
    {
        return new WGAbductState
        {
            Config = config,
            PhaseSince = _timing.CurTime,
            NextMark = _timing.CurTime + AbductInterval(config) * 0.5,
        };
    }

    private TimeSpan AbductInterval(WGEncounterAbduct config)
    {
        return TimeSpan.FromSeconds(_random.NextFloat(config.MarkMin, MathF.Max(config.MarkMin, config.MarkMax)));
    }

    /// <summary>One look at an abductor: its roaming, its run at a mark, and whoever it holds.</summary>
    private void Abduct(Entity<WGEncounterComponent> encounter, string key, WGEncounterShipState ship, WGAbductState abduct)
    {
        if (abduct.CloseAt is { } close && _timing.CurTime >= close)
            EndAbductCall(abduct);

        // Cast off with someone still on the mark: those hands are left there, as a raider's are.
        if (abduct.Strays && !_docking.GetDocks(ship.Grid).Any(dock => dock.Comp.DockedWith != null))
        {
            abduct.Strays = false;
            LeaveBehind(encounter, ship);
        }

        // Hands on the mark are still crew while the run is on; with nobody aboard, the prisoner isn't dropped for that.
        var handsOut = abduct.Phase == WGAbductPhase.Grab || abduct.Strays;
        if (!HasLivingCrew(ship, anywhere: handsOut) && !ship.Virtual)
        {
            // Nobody left to hold anyone: whoever is in the cell waits for a rescue with the door open.
            if (abduct.Cell is { } cell && abduct.Prisoner != null)
                _cells.Open(cell);
            abduct.Prisoner = null;
            if (abduct.Cell is not { } chair || _cells.Buckled(chair) == null)
                AbductClearHostage(ship, abduct);
            return;
        }

        if (!ship.Virtual)
            AbductRings(ship, abduct);

        switch (abduct.Phase)
        {
            case WGAbductPhase.Roam:
                AbductRoam(encounter, key, ship, abduct);
                break;
            case WGAbductPhase.Close:
                AbductClose(encounter, key, ship, abduct);
                break;
            case WGAbductPhase.Dock:
                AbductDock(encounter, key, ship, abduct);
                break;
            case WGAbductPhase.Grab:
                AbductGrab(encounter, key, ship, abduct);
                break;
            case WGAbductPhase.Ransom:
                AbductRansom(encounter, key, ship, abduct);
                break;
            case WGAbductPhase.Handover:
                AbductHandover(encounter, key, ship, abduct);
                break;
        }
    }

    /// <summary>
    /// Anyone may call it about a prisoner: a ring at its helm is picked up with the price while it holds someone, and
    /// told to come and collect during a handover. It takes no other calls.
    /// </summary>
    private void AbductRings(WGEncounterShipState ship, WGAbductState abduct)
    {
        var talking = abduct.Phase is WGAbductPhase.Ransom or WGAbductPhase.Handover && abduct.Prisoner != null
                      && (abduct.Console is not { } line || _hailing.GetState(line) == null);
        _rings.Clear();
        var ringing = EntityQueryEnumerator<HailConsoleComponent, TransformComponent>();
        while (ringing.MoveNext(out var uid, out var hail, out var xform))
        {
            if (xform.GridUid == ship.Grid && hail.State == HailState.Ringing)
                _rings.Add(uid);
        }

        foreach (var console in _rings)
        {
            if (!talking || !TryFindHelm(ship, out _, out var speaker))
            {
                _hailing.AnswerAutomated(console, console, false);
                continue;
            }

            talking = false;
            _hailing.AnswerAutomated(console, speaker, true);
            if (_hailing.GetState(console) != HailState.Connected)
                continue;

            abduct.Console = console;
            abduct.Operator = speaker;
            abduct.CloseAt = null;
            if (abduct.Phase == WGAbductPhase.Handover)
            {
                AbductSay(abduct, "handover", ("name", Name(abduct.Prisoner!.Value)), ("minutes", AbductHandoverMinutes));
                abduct.CloseAt = _timing.CurTime + AbductCloseDelay;
                continue;
            }

            AbductDemand(console, speaker, abduct);
        }

        _rings.Clear();
    }

    /// <summary>States the price down an open line and offers the answers.</summary>
    private void AbductDemand(EntityUid console, EntityUid speaker, WGAbductState abduct)
    {
        var ransom = abduct.Config.Ransom;
        AbductSay(abduct, "demand", ("name", Name(abduct.Prisoner!.Value)), ("ransom", ransom));
        if (!_hailing.Offer(console, speaker, new List<HailOption>
            {
                new(AbductPay, Loc.GetString($"{abduct.Config.Lines}-option-pay", ("ransom", ransom))),
                new(AbductRefuse, Loc.GetString($"{abduct.Config.Lines}-option-refuse")),
            }))
            Log.Warning($"Abductor console {ToPrettyString(console)} could not offer its answers.");
    }

    /// <summary>Paid over the hail: the prisoner is cut loose, and the ship holds still and lets anyone dock and walk them off.</summary>
    private void AbductBeginHandover(WGEncounterShipState ship, WGAbductState abduct)
    {
        AbductStandDown(ship);
        RecallBoardingParty(ship);
        SetAbductPhase(abduct, WGAbductPhase.Handover);
        abduct.HandoverEnds = _timing.CurTime + TimeSpan.FromMinutes(AbductHandoverMinutes);
        if (abduct.Cell is { } cell)
            _cells.Open(cell);
        if (abduct.Prisoner is { } prisoner)
        {
            _cells.Unbind(prisoner);
            _popup.PopupEntity(Loc.GetString($"{abduct.Config.Lines}-handover-told"), prisoner, prisoner, PopupType.Large);
        }

        var crew = EntityQueryEnumerator<WGCrewComponent, WGCrewSecurityComponent>();
        while (crew.MoveNext(out var uid, out var member, out var rules))
        {
            if (member.Group != ship.Group)
                continue;

            abduct.Truce[uid] = (member.Engagement, rules.Boarding, rules.Docking);
            member.Engagement = WGCrewEngagement.Never;
            rules.Boarding = WGCrewSecurityResponse.Ignore;
            rules.Docking = WGCrewSecurityResponse.Ignore;
            _crewSecurity.Reset(uid);
        }

        // No clamps on whoever comes for them, and no locked doors between the port and the cell.
        if (TryComp<WGDockingClampComponent>(ship.Grid, out var clamps))
        {
            EntityManager.System<WGDockingClampSystem>().Release((ship.Grid, clamps), null);
            clamps.OpenUntil = abduct.HandoverEnds;
        }

        var locks = EntityQueryEnumerator<DoorComponent, AccessReaderComponent, TransformComponent>();
        while (locks.MoveNext(out var uid, out _, out var reader, out var xform))
        {
            if (xform.GridUid != ship.Grid || !reader.Enabled)
                continue;

            reader.Enabled = false;
            Dirty(uid, reader);
            abduct.Unlocked.Add(uid);
        }

        _objectives.SetQueue(ship.Grid, ship.Group, new List<WGCrewObjective> { new() { Kind = WGCrewObjectiveKind.Hold } });
    }

    /// <summary>Waits for the prisoner to be off the ship and their rescuers with them; out of time, a prisoner still aboard is sent home.</summary>
    private void AbductHandover(Entity<WGEncounterComponent> encounter, string key, WGEncounterShipState ship, WGAbductState abduct)
    {
        var aboard = abduct.Prisoner is { } held && !TerminatingOrDeleted(held) && !_mobs.IsDead(held)
                     && Transform(held).GridUid == ship.Grid;
        if (_timing.CurTime >= abduct.HandoverEnds)
        {
            if (aboard)
                AbductRelease(ship, abduct, true);
        }
        else if (aboard || AbductCaptives(ship.Grid).Count > 0)
            return;

        AbductEndTruce(abduct);
        AbductEnd(encounter, key, ship, abduct);
    }

    /// <summary>Gives its crew their rules and its doors their locks back.</summary>
    private void AbductEndTruce(WGAbductState abduct)
    {
        foreach (var (uid, rules) in abduct.Truce)
        {
            if (TryComp<WGCrewComponent>(uid, out var member))
                member.Engagement = rules.Engagement;
            if (TryComp<WGCrewSecurityComponent>(uid, out var security))
            {
                security.Boarding = rules.Boarding;
                security.Docking = rules.Docking;
            }
        }

        abduct.Truce.Clear();
        foreach (var door in abduct.Unlocked)
        {
            if (TryComp<AccessReaderComponent>(door, out var reader))
            {
                reader.Enabled = true;
                Dirty(door, reader);
            }
        }

        abduct.Unlocked.Clear();
    }

    /// <summary>Shows the ship on every scanner for what it is, whatever it was hiding behind.</summary>
    private void AbductMarkHostage(WGEncounterShipState ship, WGAbductState abduct)
    {
        if (HasComp<WGHostageShipComponent>(ship.Grid))
            return;

        AddComp<WGHostageShipComponent>(ship.Grid);
        abduct.ScannerColor = _shuttles.GetIFFColor(ship.Grid);
        _shuttles.SetIFFColor(ship.Grid, AbductScannerColor);
        _shuttles.RemoveIFFFlag(ship.Grid, Content.Shared.Shuttles.Components.IFFFlags.HideLabel);
    }

    private void AbductClearHostage(WGEncounterShipState ship, WGAbductState abduct)
    {
        if (!RemComp<WGHostageShipComponent>(ship.Grid))
            return;

        if (abduct.ScannerColor is { } color)
            _shuttles.SetIFFColor(ship.Grid, color);
        abduct.ScannerColor = null;
        if (ship.Stealth)
            _shuttles.AddIFFFlag(ship.Grid, Content.Shared.Shuttles.Components.IFFFlags.HideLabel);
    }

    private void AbductRoam(Entity<WGEncounterComponent> encounter, string key, WGEncounterShipState ship, WGAbductState abduct)
    {
        var now = _timing.CurTime;
        if (_objectives.QueueStatus(ship.Grid, ship.Group) is null or "complete" or "target-lost" or "dock-failed")
            AbductWander(encounter, key, ship, abduct);

        if (now < abduct.NextMark)
            return;

        // A simulated hull is spawned on its way to the mark, and its cell looked for then.
        if (!ship.Virtual && _cells.FindCell(ship.Grid) == null)
        {
            Log.Error($"Abductor {ToPrettyString(ship.Grid)} has no cell and can take nobody.");
            abduct.NextMark = TimeSpan.MaxValue;
            return;
        }

        if (AbductPick(ship, abduct) is not { } mark)
        {
            abduct.NextMark = now + AbductLookAgain;
            return;
        }

        AbductGoAfter(encounter, key, ship, abduct, mark);
    }

    /// <summary>Starts a run at a ship: whatever it was flying is dropped for it. False if it can't take the order.</summary>
    private bool AbductGoAfter(Entity<WGEncounterComponent> encounter, string key, WGEncounterShipState ship, WGAbductState abduct, EntityUid mark)
    {
        abduct.Spared.Remove(mark);
        abduct.Mark = mark;
        abduct.DockMisses = 0;
        abduct.NextMark = _timing.CurTime + AbductInterval(abduct.Config);
        SetAbductPhase(abduct, WGAbductPhase.Close);
        Log.Info($"Abductor {ToPrettyString(ship.Grid)} goes after {ToPrettyString(mark)}.");
        if (!AbductChase(ship, mark))
            return false;

        AbductClose(encounter, key, ship, abduct);
        return true;
    }

    private bool AbductChase(WGEncounterShipState ship, EntityUid mark)
    {
        return _objectives.SetQueue(ship.Grid, ship.Group, new List<WGCrewObjective>
        {
            new() { Kind = WGCrewObjectiveKind.Follow, Target = GetNetEntity(mark), Range = AbductCloseRange / 2f },
        });
    }

    /// <summary>
    /// An admin's order to go for a ship, whatever its crew or how lately it was left alone. Returns why it can't, or
    /// null.
    /// </summary>
    private string? AbductAdminOrder(Entity<WGEncounterComponent> encounter, string key, WGEncounterShipState ship, WGAbductState abduct, EntityUid mark)
    {
        if (abduct.Phase is WGAbductPhase.Grab or WGAbductPhase.Ransom)
            return Loc.GetString("wg-encounter-admin-order-boarding");
        if (DockedAtStation(mark) || _stealth.ShipUnseen(mark))
            return Loc.GetString("wg-encounter-admin-order-abduct-unreachable");
        if (AbductCaptives(mark).Count == 0)
            return Loc.GetString("wg-encounter-admin-order-abduct-nobody");

        return AbductGoAfter(encounter, key, ship, abduct, mark) ? null : Loc.GetString("wg-encounter-admin-order-failed");
    }

    /// <summary>An admin's other orders end a run that hasn't reached anyone yet.</summary>
    private void AbductAdminCancel(WGAbductState abduct)
    {
        abduct.NextMark = _timing.CurTime + AbductInterval(abduct.Config);
        if (abduct.Phase is not (WGAbductPhase.Close or WGAbductPhase.Dock))
            return;

        abduct.Mark = null;
        SetAbductPhase(abduct, WGAbductPhase.Roam);
    }

    /// <summary>The ship with the fewest players aboard among the nearest few in reach, if any is small enough to take on.</summary>
    private EntityUid? AbductPick(WGEncounterShipState ship, WGAbductState abduct)
    {
        foreach (var (spared, until) in abduct.Spared.ToArray())
        {
            if (_timing.CurTime >= until || TerminatingOrDeleted(spared))
                abduct.Spared.Remove(spared);
        }

        var skip = new HashSet<EntityUid>(abduct.Spared.Keys);
        EntityUid? best = null;
        var fewest = abduct.Config.MaxCrew + 1;
        for (var i = 0; i < MarkCandidates; i++)
        {
            if (NearestCrewedShip(ship.Grid, abduct.Config.Reach, skip, ship.NeutralCompanies) is not { } candidate)
                break;

            skip.Add(candidate);
            var aboard = AbductCaptives(candidate).Count;
            if (aboard == 0 || aboard >= fewest)
                continue;

            fewest = aboard;
            best = candidate;
        }

        return best;
    }

    /// <summary>The living, connected players standing on a grid.</summary>
    private List<EntityUid> AbductCaptives(EntityUid grid)
    {
        var found = new List<EntityUid>();
        var players = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (players.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid == grid && !HasComp<GhostComponent>(uid) && _mobs.IsAlive(uid))
                found.Add(uid);
        }

        return found;
    }

    private void AbductClose(Entity<WGEncounterComponent> encounter, string key, WGEncounterShipState ship, WGAbductState abduct)
    {
        if (!AbductMark(ship, abduct, out var mark))
        {
            AbductDrop(encounter, key, ship, abduct, null);
            return;
        }

        var distance = (_transform.GetWorldPosition(mark) - _transform.GetWorldPosition(ship.Grid)).Length();
        if (distance <= AbductCloseRange && !ship.Virtual)
        {
            SetAbductPhase(abduct, WGAbductPhase.Dock);
            AbductIssueDock(ship, mark);
            return;
        }

        if (_timing.CurTime - abduct.PhaseSince > AbductChaseTime)
        {
            AbductDrop(encounter, key, ship, abduct, AbductSpare);
            return;
        }

        if (_objectives.QueueStatus(ship.Grid, ship.Group) is null or "complete" or "target-lost")
            AbductChase(ship, mark);
    }

    private bool AbductIssueDock(WGEncounterShipState ship, EntityUid mark)
    {
        return _objectives.SetQueue(ship.Grid, ship.Group, new List<WGCrewObjective>
        {
            new() { Kind = WGCrewObjectiveKind.Dock, Target = GetNetEntity(mark) },
            new() { Kind = WGCrewObjectiveKind.Hold },
        });
    }

    private void AbductDock(Entity<WGEncounterComponent> encounter, string key, WGEncounterShipState ship, WGAbductState abduct)
    {
        // Looked at first: an encounter ship counts as a station, so its own dock would read as the mark being berthed.
        if (abduct.Mark is { } alongside && !TerminatingOrDeleted(alongside) && _docking.AreGridsDocked(ship.Grid, alongside))
        {
            AbductBoard(encounter, key, ship, abduct, alongside);
            return;
        }

        if (!AbductMark(ship, abduct, out var mark))
        {
            AbductDrop(encounter, key, ship, abduct, null);
            return;
        }

        var status = _objectives.QueueStatus(ship.Grid, ship.Group);
        if (_timing.CurTime - abduct.PhaseSince > AbductChaseTime
            || status == "dock-failed" && ++abduct.DockMisses >= AbductDockMisses)
        {
            AbductDrop(encounter, key, ship, abduct, AbductSpare);
            return;
        }

        if (status is null or "dock-failed" or "target-lost")
            AbductIssueDock(ship, mark);
    }

    /// <summary>Alongside: nobody goes looking for a fight, the guards hold the port and the hands go for someone.</summary>
    private void AbductBoard(Entity<WGEncounterComponent> encounter, string key, WGEncounterShipState ship, WGAbductState abduct, EntityUid mark)
    {
        abduct.Cell = _cells.FindCell(ship.Grid);
        if (abduct.Cell == null)
        {
            Log.Error($"Abductor {ToPrettyString(ship.Grid)} has no cell and can take nobody.");
            AbductDrop(encounter, key, ship, abduct, AbductSpare);
            return;
        }

        ship.Boarded = mark;
        var crew = EntityQueryEnumerator<WGCrewComponent>();
        while (crew.MoveNext(out var uid, out var member))
        {
            if (member.Group != ship.Group)
                continue;

            member.Pursues = false;
            if (TryComp<WGPilotDutyComponent>(uid, out var pilot))
                pilot.AbsentCrewWait = 180f;
        }

        abduct.Strays = true;
        SendBoardingParty(ship, mark);
        SetAbductPhase(abduct, WGAbductPhase.Grab);
    }

    private void AbductGrab(Entity<WGEncounterComponent> encounter, string key, WGEncounterShipState ship, WGAbductState abduct)
    {
        if (abduct.Cell is not { } cell || TerminatingOrDeleted(cell))
        {
            AbductFail(encounter, key, ship, abduct);
            return;
        }

        if (abduct.Prisoner is { } held && !TerminatingOrDeleted(held) && _cells.IsHeld(held, cell))
        {
            AbductSecured(encounter, ship, abduct, held);
            return;
        }

        var retaking = abduct.RansomEnds != null;
        if (_timing.CurTime - abduct.PhaseSince > AbductGrabTime || AbductGrabbers(ship).Count == 0 && !AbductFreeHands(ship))
        {
            AbductFail(encounter, key, ship, abduct);
            return;
        }

        var target = abduct.Prisoner;
        if (target is not { } current || !AbductStillTakeable(ship, abduct, current))
            target = AbductNextCaptive(ship, abduct);

        if (target is not { } prisoner)
        {
            AbductFail(encounter, key, ship, abduct);
            return;
        }

        // Shaken off with nobody aboard yet: it goes back in for another port on the mark.
        var on = Transform(prisoner).GridUid;
        if (!retaking && on != ship.Grid && abduct.Mark is { } mark && !_docking.AreGridsDocked(ship.Grid, mark))
        {
            AbductStandDown(ship);
            RecallBoardingParty(ship);
            abduct.Prisoner = null;
            SetAbductPhase(abduct, WGAbductPhase.Dock);
            AbductIssueDock(ship, mark);
            return;
        }

        abduct.Prisoner = prisoner;
        AbductSendHands(ship, abduct, prisoner, cell);
    }

    /// <summary>Whether its hands can still go for someone: on the mark or loose aboard, and not gone over to a ghost or a corpse.</summary>
    private bool AbductStillTakeable(WGEncounterShipState ship, WGAbductState abduct, EntityUid target)
    {
        if (TerminatingOrDeleted(target) || !_mobs.IsAlive(target) || HasComp<GhostComponent>(target))
            return false;

        var on = Transform(target).GridUid;
        return on == ship.Grid || on != null && on == abduct.Mark;
    }

    private EntityUid? AbductNextCaptive(WGEncounterShipState ship, WGAbductState abduct)
    {
        // A prisoner who got clear of the ship is clear; nobody else is taken in their place.
        if (abduct.RansomEnds != null || abduct.Mark is not { } mark || TerminatingOrDeleted(mark))
            return null;

        var here = _transform.GetWorldPosition(ship.Grid);
        return AbductCaptives(mark).OrderBy(uid => (_transform.GetWorldPosition(uid) - here).LengthSquared())
            .Cast<EntityUid?>().FirstOrDefault();
    }

    /// <summary>The ship's deckhands, up to its number of grabbers, onto the prisoner. Whoever is already on it keeps at it.</summary>
    private void AbductSendHands(WGEncounterShipState ship, WGAbductState abduct, EntityUid prisoner, EntityUid cell)
    {
        // The drag is one man's work: the rest wait for what comes of it rather than crowd the door.
        var dragged = CompOrNull<Content.Shared.Movement.Pulling.Components.PullableComponent>(prisoner)?.Puller != null;
        var working = 0;
        foreach (var grabber in AbductGrabbers(ship))
        {
            if (CompOrNull<WGCrewGrabberComponent>(grabber)?.Target != prisoner)
                _grabbers.Order(grabber, prisoner, cell);
            working++;
        }

        if (dragged)
            return;

        var crew = EntityQueryEnumerator<WGCrewComponent, TransformComponent>();
        while (crew.MoveNext(out var uid, out var member, out _))
        {
            if (working >= abduct.Config.Grabbers)
                break;

            if (member.Group != ship.Group || member.Role != WGCrewRoles.Deckhand || member.Duty == WGCrewDuties.Pilot
                || !_mobs.IsAlive(uid) || HasComp<ActorComponent>(uid) || HasComp<WGCrewGrabberComponent>(uid))
                continue;

            _grabbers.Order(uid, prisoner, cell);
            working++;
        }
    }

    private List<EntityUid> AbductGrabbers(WGEncounterShipState ship)
    {
        var found = new List<EntityUid>();
        var query = EntityQueryEnumerator<WGCrewGrabberComponent, WGCrewComponent>();
        while (query.MoveNext(out var uid, out _, out var member))
        {
            if (member.Group == ship.Group && _mobs.IsAlive(uid))
                found.Add(uid);
        }

        return found;
    }

    /// <summary>Whether any deckhand lives to send.</summary>
    private bool AbductFreeHands(WGEncounterShipState ship)
    {
        var crew = EntityQueryEnumerator<WGCrewComponent>();
        while (crew.MoveNext(out var uid, out var member))
        {
            if (member.Group == ship.Group && member.Role == WGCrewRoles.Deckhand && member.Duty != WGCrewDuties.Pilot && _mobs.IsAlive(uid))
                return true;
        }

        return false;
    }

    private void AbductStandDown(WGEncounterShipState ship)
    {
        foreach (var grabber in AbductGrabbers(ship))
        {
            _grabbers.StandDown(grabber);
        }
    }

    /// <summary>
    /// Someone is in the chair. The first time, word goes out, the warrant rises and the ship casts off to wait; a
    /// prisoner put back after a break just goes back to waiting.
    /// </summary>
    private void AbductSecured(Entity<WGEncounterComponent> encounter, WGEncounterShipState ship, WGAbductState abduct, EntityUid prisoner)
    {
        AbductStandDown(ship);
        AbductMarkHostage(ship, abduct);
        var ransom = abduct.Config.Ransom;
        _cells.Hold((abduct.Cell!.Value, Comp<WGAbductCellComponent>(abduct.Cell.Value)), prisoner, ransom);
        SetAbductPhase(abduct, WGAbductPhase.Ransom);
        if (abduct.RansomEnds != null)
            return;

        abduct.Taken++;
        abduct.RansomEnds = _timing.CurTime + TimeSpan.FromSeconds(abduct.Config.HoldTime);
        abduct.Hails = 0;
        abduct.Refused = false;
        abduct.NextHail = _timing.CurTime;
        RecallBoardingParty(ship);

        if (!_bounties.AddToBounty(encounter, ransom))
            Log.Warning($"Encounter {ToPrettyString(encounter)} has no warrant to add a ransom of {ransom} to.");

        // A hidden abductor comes out on the sector map: the point of the warrant is that it can be found.
        Reveal(encounter.AsNullable(), true);
        _chat.DispatchGlobalAnnouncement(
            Loc.GetString($"{abduct.Config.Lines}-taken", ("name", Name(prisoner)), ("ship", ship.Name), ("ransom", ransom)),
            Loc.GetString($"{abduct.Config.Lines}-sender"), colorOverride: Color.Orange);
        _popup.PopupEntity(Loc.GetString($"{abduct.Config.Lines}-prisoner-told", ("ransom", ransom)), prisoner, prisoner, PopupType.LargeCaution);
        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(prisoner):player} was abducted by encounter ship {ToPrettyString(ship.Grid):entity}");

        // Out of the prey's port and off a way, where it can be found if anyone comes looking.
        var clear = _transform.GetWorldPosition(ship.Grid) + _random.NextAngle().ToVec() * RaidExit;
        _objectives.SetQueue(ship.Grid, ship.Group, new List<WGCrewObjective>
        {
            new() { Kind = WGCrewObjectiveKind.Undock },
            new() { Kind = WGCrewObjectiveKind.GoTo, Position = clear, Range = 200f },
            new() { Kind = WGCrewObjectiveKind.Hold },
        });
    }

    private void AbductRansom(Entity<WGEncounterComponent> encounter, string key, WGEncounterShipState ship, WGAbductState abduct)
    {
        if (abduct.Cell is not { } cell || TerminatingOrDeleted(cell) || abduct.Prisoner is not { } prisoner || TerminatingOrDeleted(prisoner))
        {
            AbductEnd(encounter, key, ship, abduct);
            return;
        }

        // Dead or left their body: nothing more to hold, and a body isn't worth the air.
        if (_mobs.IsDead(prisoner))
        {
            _cells.Open(cell);
            AbductEnd(encounter, key, ship, abduct);
            return;
        }

        // Off the ship altogether: they got away.
        if (Transform(prisoner).GridUid != ship.Grid)
        {
            _cells.Open(cell);
            EndAbductCall(abduct);
            AbductEnd(encounter, key, ship, abduct);
            return;
        }

        if (_timing.CurTime >= abduct.RansomEnds)
        {
            AbductRelease(ship, abduct, false);
            AbductEnd(encounter, key, ship, abduct);
            return;
        }

        // Loose aboard: the hands go and fetch them. Out of the chair in the cell they can stew.
        if (_cells.IsLoose(prisoner, cell))
        {
            // The door is opened for the walk back; it is shut again once they are in the chair.
            _cells.Open(cell);
            SetAbductPhase(abduct, WGAbductPhase.Grab);
            return;
        }

        _cells.Seal(cell);
        AbductHailHome(ship, abduct);
    }

    /// <summary>Rings the prisoner's ship now and then, for whoever is aboard it to answer.</summary>
    private void AbductHailHome(WGEncounterShipState ship, WGAbductState abduct)
    {
        var now = _timing.CurTime;
        if (abduct.Refused || abduct.Hails >= AbductHails || now < abduct.NextHail || abduct.Mark is not { } mark
            || TerminatingOrDeleted(mark) || abduct.CloseAt != null || abduct.Console is { } busy && _hailing.GetState(busy) != null)
            return;

        abduct.NextHail = now + AbductHailEvery;
        if (!TryFindHelm(ship, out var console, out var speaker))
            return;

        abduct.Console = console;
        abduct.Operator = speaker;
        if (_hailing.TryHailAutomated(console, speaker, mark, out _))
            abduct.Hails++;
    }

    /// <summary>Whoever answered hears the price and gets the choice. False for a hail that wasn't an abductor's.</summary>
    private bool AbductAnswered(ref HailAnsweredEvent args)
    {
        if (!TryAbductor(args.Grid, args.Console, out _, out _, out var abduct) || abduct.Phase != WGAbductPhase.Ransom
            || abduct.Operator is not { } speaker || abduct.Prisoner is not { } prisoner)
            return false;

        AbductDemand(args.Console, speaker, abduct);
        return true;
    }

    private bool AbductChosen(ref HailOptionChosenEvent args)
    {
        if (!TryAbductor(args.OffererGrid, args.Offerer, out var encounter, out var ship, out var abduct))
            return false;

        if (args.Id != AbductPay)
        {
            abduct.Refused = true;
            AbductSay(abduct, "refused");
            abduct.CloseAt = _timing.CurTime + AbductCloseDelay;
            return true;
        }

        // Set first: a paid ransom ends the run, and the call stays up for the answer.
        abduct.CloseAt = _timing.CurTime + AbductCloseDelay;
        var name = abduct.Prisoner is { } held ? Name(held) : string.Empty;
        if (abduct.Phase != WGAbductPhase.Ransom || !AbductPayRansom(encounter, ship, abduct, args.ChooserEntity, handover: true))
        {
            AbductSay(abduct, "short", ("ransom", abduct.Config.Ransom));
            return true;
        }

        AbductSay(abduct, "handover", ("name", name), ("minutes", AbductHandoverMinutes));
        return true;
    }

    private void OnRansomOffered(Entity<WGAbductCellComponent> ent, ref WGAbductRansomOfferedEvent args)
    {
        if (Transform(ent).GridUid is not { } grid || !TryComp<WGEncounterGridComponent>(grid, out var marker)
            || !TryComp<WGEncounterComponent>(marker.Encounter, out var comp) || comp.Resolution != null
            || !comp.Ships.TryGetValue(marker.Key, out var ship) || ship.Abduct is not { Phase: WGAbductPhase.Ransom } abduct
            || abduct.Prisoner != args.Payer)
            return;

        if (!AbductPayRansom((marker.Encounter, comp), ship, abduct, args.Payer))
            _popup.PopupEntity(Loc.GetString($"{abduct.Config.Lines}-prisoner-short", ("ransom", abduct.Config.Ransom)), args.Payer, args.Payer, PopupType.MediumCaution);
    }

    /// <summary>
    /// Takes the ransom from someone's account and lets the prisoner go: sent home, or with <paramref name="handover"/>
    /// left free aboard for someone to dock and collect. False if the payer can't cover it.
    /// </summary>
    public bool AbductPayRansom(Entity<WGEncounterComponent> encounter, WGEncounterShipState ship, WGAbductState abduct, EntityUid payer,
        bool handover = false)
    {
        var ransom = abduct.Config.Ransom;
        if (abduct.Prisoner == null || !_bank.TryBankWithdraw(payer, ransom))
            return false;

        abduct.Ransoms++;
        abduct.Collected += ransom;
        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(payer):player} paid a ransom of {ransom} spesos to encounter ship {ToPrettyString(ship.Grid):entity}");
        if (handover)
        {
            AbductBeginHandover(ship, abduct);
            return true;
        }

        AbductRelease(ship, abduct, true);
        var key = encounter.Comp.Ships.First(pair => pair.Value == ship).Key;
        AbductEnd(encounter, key, ship, abduct);
        return true;
    }

    /// <summary>Sends the prisoner home: their own ship if it is still there, else the nearest station, cuffs off.</summary>
    private void AbductRelease(WGEncounterShipState ship, WGAbductState abduct, bool paid)
    {
        if (abduct.Prisoner is not { } prisoner || TerminatingOrDeleted(prisoner))
            return;

        _cells.Unbind(prisoner);
        if (abduct.Cell is { } cell)
            _cells.Open(cell);

        var home = abduct.Mark is { } mark && !TerminatingOrDeleted(mark) && Transform(mark).MapID == Transform(ship.Grid).MapID && _cells.SendAboard(prisoner, mark);
        if (!home && NearestStationGrid(ship.Grid) is { } station)
            home = _cells.SendAboard(prisoner, station);
        if (!home)
            Log.Warning($"Abductor {ToPrettyString(ship.Grid)} let {ToPrettyString(prisoner)} go with nowhere to send them.");

        var line = paid ? "released-paid" : "released-free";
        _popup.PopupEntity(Loc.GetString($"{abduct.Config.Lines}-{line}"), prisoner, prisoner, PopupType.Large);
        abduct.Prisoner = null;
    }

    /// <summary>The nearest station on the map.</summary>
    private EntityUid? NearestStationGrid(EntityUid from)
    {
        var here = _transform.GetMapCoordinates(from);
        EntityUid? nearest = null;
        var best = float.MaxValue;
        var grids = EntityQueryEnumerator<Content.Shared.Station.Components.StationMemberComponent, TransformComponent>();
        while (grids.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapID != here.MapId || !IsStationGrid(uid))
                continue;

            var distance = (_transform.GetWorldPosition(uid) - here.Position).LengthSquared();
            if (distance >= best)
                continue;

            best = distance;
            nearest = uid;
        }

        return nearest;
    }

    /// <summary>The run came to nothing: whoever is off the ship comes back and it casts off.</summary>
    private void AbductFail(Entity<WGEncounterComponent> encounter, string key, WGEncounterShipState ship, WGAbductState abduct)
    {
        AbductStandDown(ship);
        RecallBoardingParty(ship);
        // Someone already dragged aboard isn't carried off cuffed with no way to pay: they go home.
        if (abduct.Prisoner is { } taken && !TerminatingOrDeleted(taken) && Transform(taken).GridUid == ship.Grid)
            AbductRelease(ship, abduct, false);

        if (abduct.Cell is { } cell)
            _cells.Open(cell);

        abduct.Prisoner = null;
        AbductEnd(encounter, key, ship, abduct);
    }

    /// <summary>Done with this mark, whichever way it went: left alone a while, and the ship roams again.</summary>
    private void AbductEnd(Entity<WGEncounterComponent> encounter, string key, WGEncounterShipState ship, WGAbductState abduct)
    {
        // A hail with a last line still to be heard hangs up on its timer; any other ending hangs up now.
        if (abduct.CloseAt == null)
            EndAbductCall(abduct);

        if (abduct.Mark is { } mark)
            abduct.Spared[mark] = _timing.CurTime + AbductSpare;

        // Nobody is on a run any more: the crew go back to looking for trouble aboard.
        var crew = EntityQueryEnumerator<WGCrewComponent>();
        while (crew.MoveNext(out var member))
        {
            if (member.Group == ship.Group)
                member.Pursues = true;
        }

        ship.Boarded = null;
        AbductClearHostage(ship, abduct);
        if (abduct.Cell is { } cell && TryComp<WGAbductCellComponent>(cell, out var comp))
            comp.Prisoner = null;

        abduct.Mark = null;
        abduct.Prisoner = null;
        abduct.RansomEnds = null;
        SetAbductPhase(abduct, WGAbductPhase.Roam);
        abduct.NextMark = _timing.CurTime + AbductInterval(abduct.Config);
        AbductWander(encounter, key, ship, abduct);
    }

    private void AbductDrop(Entity<WGEncounterComponent> encounter, string key, WGEncounterShipState ship, WGAbductState abduct, TimeSpan? spare)
    {
        var now = _timing.CurTime;
        if (abduct.Mark is { } mark && spare is { } time)
            abduct.Spared[mark] = now + time;

        abduct.Mark = null;
        SetAbductPhase(abduct, WGAbductPhase.Roam);
        abduct.NextMark = now + AbductLookAgain;
        AbductWander(encounter, key, ship, abduct);
    }

    private void AbductWander(Entity<WGEncounterComponent> encounter, string key, WGEncounterShipState ship, WGAbductState abduct)
    {
        _objectives.SetQueue(ship.Grid, ship.Group, AbductLegs(encounter, key, ship, abduct));
    }

    /// <summary>Its prototype's wander legs about the nearest player ship it has no business with yet, or about where it began.</summary>
    private List<WGCrewObjective> AbductLegs(Entity<WGEncounterComponent> encounter, string key, WGEncounterShipState ship, WGAbductState abduct)
    {
        var queue = new List<WGCrewObjective>();
        if (_docking.GetDocks(ship.Grid).Any(dock => dock.Comp.DockedWith != null))
            queue.Add(new WGCrewObjective { Kind = WGCrewObjectiveKind.Undock });

        if (_prototypes.TryIndex(encounter.Comp.Prototype, out var prototype)
            && prototype.Ships.FirstOrDefault(entry => entry.Key == key) is { } entry)
        {
            System.Numerics.Vector2? around = NearestCrewedShip(ship.Grid, except: abduct.Spared.Keys) is { } traffic
                ? _transform.GetWorldPosition(traffic)
                : null;
            queue.AddRange(BuildQueue(encounter.Comp, entry, ship.Grid, around));
        }

        if (queue.Count == 0)
            queue.Add(new WGCrewObjective { Kind = WGCrewObjectiveKind.Hold });

        return queue;
    }

    /// <summary>Its mark, if it is still there to go for and not berthed at a station.</summary>
    private bool AbductMark(WGEncounterShipState ship, WGAbductState abduct, out EntityUid mark)
    {
        mark = abduct.Mark ?? default;
        return abduct.Mark != null && !TerminatingOrDeleted(mark) && Transform(mark).MapID == Transform(ship.Grid).MapID
               && !DockedAtStation(mark) && !_stealth.ShipUnseen(mark);
    }

    private void SetAbductPhase(WGAbductState abduct, WGAbductPhase phase)
    {
        abduct.Phase = phase;
        abduct.PhaseSince = _timing.CurTime;
    }

    private void EndAbductCall(WGAbductState abduct)
    {
        abduct.CloseAt = null;
        if (abduct.Console is not { } console || _hailing.GetState(console) == null)
            return;

        _hailing.ClearOptions(console);
        _hailing.End(console);
    }

    /// <summary>
    /// A resolved encounter lets its prisoner go: a crew still alive sends them home, and with none left the door stands
    /// open and the ship stays for as long as they are aboard, for them or their rescuers to walk off.
    /// </summary>
    private void EndAbduction(WGEncounterShipState ship)
    {
        if (ship.Abduct is not { } abduct)
            return;

        EndAbductCall(abduct);
        AbductEndTruce(abduct);
        if (abduct.Prisoner == null)
            return;

        if (HasLivingCrew(ship))
        {
            AbductRelease(ship, abduct, false);
            return;
        }

        if (abduct.Cell is { } cell)
            _cells.Open(cell);
        abduct.Prisoner = null;
    }

    private void AbductSay(WGAbductState abduct, string step, params (string, object)[] args)
    {
        if (abduct.Console is { } console && abduct.Operator is { } speaker)
            _hailing.Say(console, speaker, Loc.GetString($"{abduct.Config.Lines}-{step}", args));
    }

    /// <summary>The abductor whose hails go through a console, while its encounter runs.</summary>
    private bool TryAbductor(EntityUid? grid, EntityUid console, out Entity<WGEncounterComponent> encounter,
        [NotNullWhen(true)] out WGEncounterShipState? ship, [NotNullWhen(true)] out WGAbductState? abduct)
    {
        encounter = default;
        ship = null;
        abduct = null;
        if (grid is not { } uid || !TryComp<WGEncounterGridComponent>(uid, out var marker)
            || !TryComp<WGEncounterComponent>(marker.Encounter, out var comp) || comp.Resolution != null
            || !comp.Ships.TryGetValue(marker.Key, out ship) || ship.Abduct is not { } state || state.Console != console)
            return false;

        encounter = (marker.Encounter, comp);
        abduct = state;
        return true;
    }

    /// <summary>What an abductor is doing, for the admin's lists; null for any other ship.</summary>
    public string? AbductStatus(WGEncounterShipState ship)
    {
        if (ship.Abduct is not { } abduct)
            return null;

        var mark = abduct.Mark is { } ship2 && !TerminatingOrDeleted(ship2) ? Name(ship2) : Loc.GetString("wg-encounter-extort-status-nobody");
        return abduct.Phase switch
        {
            WGAbductPhase.Close => Loc.GetString("wg-encounter-abduct-status-close", ("ship", mark)),
            WGAbductPhase.Dock => Loc.GetString("wg-encounter-abduct-status-dock", ("ship", mark)),
            WGAbductPhase.Grab => Loc.GetString("wg-encounter-abduct-status-grab", ("ship", mark)),
            WGAbductPhase.Ransom => Loc.GetString("wg-encounter-abduct-status-ransom", ("name", abduct.Prisoner is { } held && !TerminatingOrDeleted(held) ? Name(held) : mark)),
            WGAbductPhase.Handover => Loc.GetString("wg-encounter-abduct-status-handover"),
            _ => Loc.GetString("wg-encounter-extort-status-roam"),
        };
    }
}
