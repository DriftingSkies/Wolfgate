using System.Linq;
using System.Numerics;
using Content.Server._WG.Encounters.Components;
using Content.Server._WG.NpcCrew;
using Content.Server._WG.NpcCrew.Components;
using Content.Server._WG.NpcCrew.Systems;
using Content.Server._WG.ShipAccess;
using Content.Server._WG.ShipShields;
using Content.Server.NPC.HTN;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._WG.Encounters;
using Content.Shared._WG.JudgeLine;
using Content.Shared._WG.NpcCrew;
using Content.Shared._WG.ShipAccess;
using Content.Shared.Damage;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Radio;
using Content.Shared.Station.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.JudgeLine;

/// <summary>
/// What the responder does on scene. It checks on arrival which emergencies are real; none, and none when the call was
/// made, is a hoax. A real emergency other than the one chosen is dealt with instead. Each kind has its plan:
/// <list type="bullet">
/// <item>Under fire: escort the caller's ship, allied with it, and fight whoever hit it lately.</item>
/// <item>Boarded: dock and send the guards across after the hostiles aboard.</item>
/// <item>Medical: dock so the paramedic treats the hurt aboard; a caller with no ship is picked up instead.</item>
/// <item>Stranded: dock and hand over a relief crate of fuel and repair stock.</item>
/// <item>Adrift: come alongside, let the caller aboard and fly them to the nearest station.</item>
/// <item>Assault: come to the caller and fight whoever is attacking them.</item>
/// </list>
/// Once nothing has been wrong for a while the guards come home and the responder leaves.
/// </summary>
public sealed partial class WGJudgeLineSystem
{
    [Dependency] private DockingSystem _docking = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private NpcFactionSystem _factions = default!;
    [Dependency] private WGCrewAlertSystem _alerts = default!;
    [Dependency] private WGCrewEscortSystem _escorts = default!;
    [Dependency] private WGCrewObjectiveSystem _objectives = default!;
    [Dependency] private Robust.Shared.Physics.Systems.SharedPhysicsSystem _physics = default!;
    [Dependency] private WGCrewWorkSystem _work = default!;
    [Dependency] private WGShipAccessServerSystem _shipAccess = default!;

    private static readonly ProtoId<RadioChannelPrototype> Channel = "Common";
    private static readonly ProtoId<WGCrewRolePrototype> GuardRole = "WGCrewMarine";
    private static readonly ProtoId<WGCrewRolePrototype> MedicRole = "WGCrewParamedic";
    public static readonly EntProtoId ReliefCrate = "WGJudgeLineReliefCrate";

    /// <summary>How long a hit on a ship counts as it being under fire.</summary>
    private static readonly TimeSpan HitMemory = TimeSpan.FromSeconds(90);

    /// <summary>Within this of its target the responder has arrived.</summary>
    private const float ArrivalRange = 600f;

    /// <summary>Past this after its drop the responder takes stock wherever it is.</summary>
    private static readonly TimeSpan ArrivalTimeout = TimeSpan.FromMinutes(4);

    /// <summary>The longest the responder stays on scene.</summary>
    private static readonly TimeSpan SceneLimit = TimeSpan.FromMinutes(15);

    /// <summary>How long nothing must be wrong before the responder leaves; longer after a fight.</summary>
    private static readonly TimeSpan QuietSpell = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan FightQuietSpell = TimeSpan.FromSeconds(90);

    /// <summary>The longest the paramedic works a docked ship before the responder leaves.</summary>
    private static readonly TimeSpan MedicalLimit = TimeSpan.FromMinutes(8);

    /// <summary>How long a stranded crew has to fix their ship with the relief crate before the responder leaves.</summary>
    private static readonly TimeSpan StrandedLimit = TimeSpan.FromMinutes(5);

    /// <summary>How long the judges' engineer has to fuel a stranded ship's plant before they fill it themselves.</summary>
    private static readonly TimeSpan RefuelGrace = TimeSpan.FromSeconds(90);

    /// <summary>How long a picked-up caller has to disembark at the station.</summary>
    private static readonly TimeSpan DisembarkLimit = TimeSpan.FromMinutes(3);

    /// <summary>Damage at which someone counts as needing a medic.</summary>
    private const float HurtDamage = 50f;

    /// <summary>How near the caller an attacker counts for an assault.</summary>
    private const float AssaultRange = 20f;

    private const float EscortRange = 250f;
    private const float PickupRange = 60f;

    /// <summary>Within this of a caller who is down in open space the judges pull them aboard.</summary>
    private const float HaulRange = 150f;

    /// <summary>Every ship weapon hit lately, by the ship hit and then by the attacking ship.</summary>
    private readonly Dictionary<EntityUid, Dictionary<EntityUid, TimeSpan>> _hits = new();

    private void OnHullHit(ref WGCrewHullHitEvent args) => RecordHit(args.Grid, args.AttackerGrid);

    private void OnShieldHit(ref WGShipShieldAttackedEvent args) => RecordHit(args.Grid, args.AttackerGrid);

    /// <summary>Records a ship weapon hit on one grid by another.</summary>
    public void RecordHit(EntityUid grid, EntityUid attacker)
    {
        if (grid == attacker)
            return;

        if (!_hits.TryGetValue(grid, out var by))
            _hits[grid] = by = new Dictionary<EntityUid, TimeSpan>();
        by[attacker] = _timing.CurTime;
    }

    private void PruneHits()
    {
        var now = _timing.CurTime;
        foreach (var (grid, by) in _hits.ToList())
        {
            foreach (var (attacker, at) in by.ToList())
            {
                if (now - at > HitMemory || TerminatingOrDeleted(attacker))
                    by.Remove(attacker);
            }

            if (by.Count == 0 || TerminatingOrDeleted(grid))
                _hits.Remove(grid);
        }
    }

    /// <summary>The ships that hit a grid within a span.</summary>
    private List<EntityUid> Attackers(EntityUid grid, TimeSpan within)
    {
        if (!_hits.TryGetValue(grid, out var by))
            return new List<EntityUid>();

        var now = _timing.CurTime;
        return by.Where(pair => now - pair.Value <= within && !TerminatingOrDeleted(pair.Key)).Select(pair => pair.Key).ToList();
    }

    /// <summary>The ship the responder is sent to: the caller's own, or the one they stand on now if not a station.</summary>
    private EntityUid? TargetGrid(WGJudgeLineCall call)
    {
        if (call.Grid is { } grid && !TerminatingOrDeleted(grid) && Transform(grid).MapID == call.Position.MapId)
            return grid;

        if (TerminatingOrDeleted(call.Caller))
            return null;

        if (Transform(call.Caller).GridUid is { } standing && !IsStation(standing)
            && (Ship(call) is not { } ship || ship.Grid != standing))
            return standing;

        return null;
    }

    /// <summary>The emergencies that are real for a call now, the one chosen first.</summary>
    public List<WGJudgeLineKind> Assess(WGJudgeLineCall call)
    {
        var real = new List<WGJudgeLineKind>();
        var target = TargetGrid(call);
        foreach (var kind in Enum.GetValues<WGJudgeLineKind>())
        {
            if (IsReal(call, kind, target))
                real.Add(kind);
        }

        if (real.Remove(call.Chosen))
            real.Insert(0, call.Chosen);
        return real;
    }

    private bool IsReal(WGJudgeLineCall call, WGJudgeLineKind kind, EntityUid? target)
    {
        return kind switch
        {
            WGJudgeLineKind.UnderFire => target is { } grid && Attackers(grid, HitMemory).Count > 0,
            WGJudgeLineKind.Boarded => target is { } grid && Hostiles(call, grid).Count > 0,
            WGJudgeLineKind.Medical => Patients(call, target).Count > 0,
            WGJudgeLineKind.Stranded => target is { } grid && (!HasThrust(grid) || _work.PlantOut(grid)),
            WGJudgeLineKind.Adrift => Transform(call.Caller).GridUid == null,
            WGJudgeLineKind.Assault => Assailants(call).Count > 0,
            _ => false,
        };
    }

    /// <summary>Sets the responder to work on the call's emergency.</summary>
    private void Begin(WGJudgeLineCall call, WGEncounterShipState ship)
    {
        var target = call.Kind == WGJudgeLineKind.Adrift ? null : TargetGrid(call);
        if (call.OrderedTo is { } old && old != target && !TerminatingOrDeleted(old))
            _escorts.RemoveAlly(ship.Grid, old);
        call.OrderedTo = target;
        call.Waypoint = call.Position.Position;
        var queue = new List<WGCrewObjective>();
        if (target is { } grid)
        {
            _escorts.AddAlly(ship.Grid, grid);
            if (call.Kind == WGJudgeLineKind.UnderFire)
            {
                queue.Add(new WGCrewObjective { Kind = WGCrewObjectiveKind.Escort, Target = GetNetEntity(grid), Range = EscortRange });
                ReportAttackers(ship, grid);
            }
            else
            {
                queue.Add(new WGCrewObjective { Kind = WGCrewObjectiveKind.Dock, Target = GetNetEntity(grid) });
            }
        }
        else
        {
            // Nothing to dock with: come alongside the caller and let them aboard.
            if (TryComp<WGShipAccessComponent>(ship.Grid, out var access))
                _shipAccess.TryAddPerson((ship.Grid, access), call.Caller);
            queue.Add(new WGCrewObjective { Kind = WGCrewObjectiveKind.GoTo, Position = call.Position.Position, Range = PickupRange });
            queue.Add(new WGCrewObjective { Kind = WGCrewObjectiveKind.Hold, Duration = (float) SceneLimit.TotalSeconds });
        }

        if (call.Encounter is { } encounter)
            _encounters.SetOrders(encounter, ShipKey, queue);
    }

    /// <summary>Sends the responder on again when the caller moved to another ship, or drifted away from the pickup point.</summary>
    private void Retarget(WGJudgeLineCall call, WGEncounterShipState ship)
    {
        var target = call.Kind == WGJudgeLineKind.Adrift ? null : TargetGrid(call);
        if (target != call.OrderedTo
            || target == null && Vector2.Distance(call.Position.Position, call.Waypoint) > PickupRange)
            Begin(call, ship);
    }

    /// <summary>Works a call on scene: takes stock on arrival, then deals with the emergency until it is over.</summary>
    private void Run(WGJudgeLineCall call, WGEncounterShipState ship)
    {
        var now = _timing.CurTime;

        // A caller whose ship opens fire on the judges loses the call; the crew's own alerts answer the shots.
        if (call.Grid is { } own && Attackers(ship.Grid, HitMemory).Contains(own))
        {
            Close(call, Loc.GetString("wg-judgeline-status-betrayed"), refund: false);
            return;
        }

        if (!call.Aboard)
            Retarget(call, ship);

        if (call.Arrived == null)
        {
            if (!Reached(call, ship) && now - call.DueAt < ArrivalTimeout)
                return;

            call.Arrived = now;
            call.LastTrouble = now;
            var real = Assess(call);
            if (real.Count == 0 && !call.RealAtCall)
            {
                Hoax(call, ship);
                return;
            }

            if (real.Count > 0 && real[0] != call.Kind)
            {
                call.Kind = real[0];
                Notify(call, Loc.GetString("wg-judgeline-status-switched", ("kind", KindName(call.Kind))));
                Begin(call, ship);
            }

            Notify(call, Loc.GetString("wg-judgeline-status-on-scene"));
            Say(call, ship, Loc.GetString($"wg-judgeline-radio-{call.Kind.ToString().ToLowerInvariant()}"));
        }

        if (Work(call, ship))
            call.LastTrouble = now;
        else if (call.GaveUp)
        {
            Close(call, Loc.GetString("wg-judgeline-status-timeout"), refund: false);
            return;
        }

        var spell = call.Kind is WGJudgeLineKind.UnderFire or WGJudgeLineKind.Boarded or WGJudgeLineKind.Assault
            ? FightQuietSpell
            : QuietSpell;
        if (now - call.LastTrouble >= spell)
            Close(call, Loc.GetString("wg-judgeline-status-resolved"), refund: false);
        else if (now - call.Arrived >= SceneLimit)
            Close(call, Loc.GetString("wg-judgeline-status-timeout"), refund: false);
    }

    /// <summary>Whether the responder is at its target: docked with it or within arrival range.</summary>
    private bool Reached(WGJudgeLineCall call, WGEncounterShipState ship)
    {
        if (TargetGrid(call) is { } grid && call.Kind != WGJudgeLineKind.Adrift)
            return DockedWith(ship.Grid, grid) || Distance(ship.Grid, _transform.GetMapCoordinates(grid)) <= ArrivalRange;

        return Distance(ship.Grid, call.Position) <= ArrivalRange;
    }

    /// <summary>Deals with the emergency a step. Returns whether it is still going on.</summary>
    private bool Work(WGJudgeLineCall call, WGEncounterShipState ship)
    {
        var target = TargetGrid(call);
        var now = _timing.CurTime;
        switch (call.Kind)
        {
            case WGJudgeLineKind.UnderFire when target is { } grid:
                ReportAttackers(ship, grid);
                return Attackers(grid, TimeSpan.FromSeconds(30)).Count > 0 || _alerts.IsAlerted(ship.Grid, ship.Group);

            case WGJudgeLineKind.Boarded when target is { } grid:
            {
                var hostiles = Hostiles(call, grid);
                if (DockedWith(ship.Grid, grid) && hostiles.Count > 0)
                    SendGuards(call, ship, hostiles);
                return hostiles.Count > 0;
            }

            case WGJudgeLineKind.Assault:
            {
                var assailants = Assailants(call);
                if (assailants.Count > 0 && (target == null || DockedWith(ship.Grid, target.Value)))
                    SendGuards(call, ship, assailants);
                return assailants.Count > 0;
            }

            case WGJudgeLineKind.Medical when target is { } grid:
                if (!DockedWith(ship.Grid, grid))
                    return true;
                call.Docked ??= now;
                return Within(call, MedicalLimit, Patients(call, grid).Count > 0);

            case WGJudgeLineKind.Stranded when target is { } grid:
                if (!DockedWith(ship.Grid, grid))
                    return true;
                call.Docked ??= now;
                if (!call.Relief)
                {
                    HandOverRelief(call, ship, grid);
                    SendEngineer(call, ship, grid);
                }

                var dry = _work.PlantOut(grid);
                if (dry && !call.Refuelled && now - call.Docked >= RefuelGrace)
                {
                    call.Refuelled = true;
                    _work.Refuel(grid);
                    Notify(call, Loc.GetString("wg-judgeline-status-refuelled"));
                    dry = _work.PlantOut(grid);
                }

                return Within(call, StrandedLimit, dry || !HasThrust(grid));

            default:
                return Pickup(call, ship);
        }
    }

    /// <summary>Whether docked work goes on: the trouble is still there and time is left. Out of time, the judges give up.</summary>
    private bool Within(WGJudgeLineCall call, TimeSpan limit, bool trouble)
    {
        if (!trouble)
            return false;
        if (_timing.CurTime - call.Docked < limit)
            return true;

        call.GaveUp = true;
        return false;
    }

    /// <summary>
    /// Picks up a caller with no ship to go to: waits alongside until they come aboard, or pulls them aboard if they are
    /// down, flies them to the nearest station that takes the ship and waits for them to disembark. A medical case is
    /// treated aboard on the way.
    /// </summary>
    private bool Pickup(WGJudgeLineCall call, WGEncounterShipState ship)
    {
        var aboard = Transform(call.Caller).GridUid == ship.Grid;
        if (!call.Aboard)
        {
            if (!aboard && !TryHaul(call, ship))
                return true;

            call.Aboard = true;
            SendToStation(call, ship, null);
            return true;
        }

        if (call.Station is not { } dropOff)
            return aboard;

        if (!DockedWith(ship.Grid, dropOff))
        {
            // Turned away, or the order ended without a berth: try the next station.
            if (_objectives.QueueStatus(ship.Grid, ship.Group) is "dock-failed" or "target-lost" or "complete")
            {
                call.Refused.Add(dropOff);
                SendToStation(call, ship, dropOff);
            }

            return aboard;
        }

        if (call.Docked == null)
        {
            call.Docked = _timing.CurTime;
            Notify(call, Loc.GetString("wg-judgeline-status-disembark"));
        }

        return aboard && _timing.CurTime - call.Docked < DisembarkLimit;
    }

    /// <summary>Pulls a caller who is down in open space aboard once the responder is alongside, beside its paramedic if it has one.</summary>
    private bool TryHaul(WGJudgeLineCall call, WGEncounterShipState ship)
    {
        if (Transform(call.Caller).GridUid != null || !_mobState.IsIncapacitated(call.Caller)
            || Distance(ship.Grid, call.Position) > HaulRange)
            return false;

        var aboard = Crew(ship).Where(member => Transform(member).GridUid == ship.Grid).ToList();
        var beside = aboard.FirstOrDefault(member => member.Comp.Role == MedicRole);
        if (beside.Owner == default && aboard.Count > 0)
            beside = aboard[0];

        var at = beside.Owner != default ? Transform(beside).Coordinates : new EntityCoordinates(ship.Grid, Vector2.Zero);
        _transform.SetCoordinates(call.Caller, at);
        _physics.SetLinearVelocity(call.Caller, Vector2.Zero);
        Notify(call, Loc.GetString("wg-judgeline-status-hauled"));
        return true;
    }

    /// <summary>
    /// Orders the responder to the nearest public station that hasn't turned it away this trip; once every one has, they
    /// are all tried again.
    /// </summary>
    private void SendToStation(WGJudgeLineCall call, WGEncounterShipState ship, EntityUid? refused)
    {
        var station = NearestStation(ship.Grid, call.Refused);
        if (station == null && call.Refused.Count > 0)
        {
            call.Refused.Clear();
            station = NearestStation(ship.Grid, call.Refused);
        }

        call.Station = station;
        if (station is not { } next || call.Encounter is not { } encounter)
            return;

        _encounters.SetOrders(encounter, ShipKey,
            new List<WGCrewObjective> { new() { Kind = WGCrewObjectiveKind.Dock, Target = GetNetEntity(next) } });
        Notify(call, refused is { } turned && turned != next
            ? Loc.GetString("wg-judgeline-status-diverted", ("refused", Name(turned)), ("station", Name(next)))
            : Loc.GetString("wg-judgeline-status-aboard", ("station", Name(next))));
    }

    /// <summary>Has the responder's crew take every ship that hit the target lately for an attacker.</summary>
    private void ReportAttackers(WGEncounterShipState ship, EntityUid grid)
    {
        foreach (var attacker in Attackers(grid, HitMemory))
        {
            if (attacker != ship.Grid && !_alerts.IsHostileShip(ship.Grid, ship.Group, attacker))
                _alerts.ReportShipThreat(ship.Grid, ship.Group, attacker);
        }
    }

    /// <summary>Posts the responder's guards beside the first threat and sets the whole crew on every one.</summary>
    private void SendGuards(WGJudgeLineCall call, WGEncounterShipState ship, List<EntityUid> threats)
    {
        var crew = Crew(ship).ToList();
        foreach (var member in crew)
        {
            foreach (var threat in threats)
            {
                _factions.AggroEntity(member.Owner, threat);
            }
        }

        var post = Transform(threats[0]).Coordinates;
        if (Transform(threats[0]).GridUid is not { } grid)
            return;

        post = _transform.WithEntityId(post, grid);
        foreach (var member in crew)
        {
            if (member.Comp.Role != GuardRole)
                continue;
            if (!call.Posted.ContainsKey(member.Owner))
                call.Posted[member.Owner] = member.Comp.Post;
            member.Comp.Post = post;
        }
    }

    /// <summary>Posts the responder's engineer at a stranded ship's plant, where he fuels it from the relief crate.</summary>
    private void SendEngineer(WGJudgeLineCall call, WGEncounterShipState ship, EntityUid grid)
    {
        if (!_work.TryFindPost(grid, out var post))
            return;

        foreach (var member in Crew(ship))
        {
            if (member.Comp.Role != WGCrewRoles.Engineer)
                continue;
            if (!call.Posted.ContainsKey(member.Owner))
                call.Posted[member.Owner] = member.Comp.Post;
            member.Comp.Post = post;
        }
    }

    /// <summary>Calls the guards home, drops the alliance and has the responder leave.</summary>
    private void StandDown(WGJudgeLineCall call)
    {
        foreach (var (guard, post) in call.Posted)
        {
            if (TryComp<WGCrewComponent>(guard, out var crew))
                crew.Post = post;
        }

        call.Posted.Clear();
        if (call.Encounter is not { } encounter || !TryComp<WGEncounterComponent>(encounter, out var comp))
            return;

        if (Ship(call) is { } ship)
        {
            if (TargetGrid(call) is { } grid)
                _escorts.RemoveAlly(ship.Grid, grid);
            if (_docking.GetDocks(ship.Grid).Any(dock => dock.Comp.DockedWith != null))
                _encounters.SetOrders(encounter, ShipKey, new List<WGCrewObjective> { new() { Kind = WGCrewObjectiveKind.Undock } });
            Say(call, ship, Loc.GetString("wg-judgeline-radio-leaving"));
        }

        // Unpinned and completed, it jumps out once nobody is aboard or docked with it.
        comp.Pinned = false;
        _encounters.Resolve((encounter, comp), WGEncounterResolution.Completed);
    }

    /// <summary>Counts a hoax against the caller and leaves, keeping the fee.</summary>
    private void Hoax(WGJudgeLineCall call, WGEncounterShipState ship)
    {
        Say(call, ship, Loc.GetString("wg-judgeline-radio-hoax"));
        if (call.User is { } user)
            _strikes[user] = _strikes.GetValueOrDefault(user) + 1;

        var strikes = call.User is { } struck ? _strikes[struck] : 1;
        Close(call, Loc.GetString("wg-judgeline-status-hoax", ("strikes", strikes), ("limit", StrikeLimit)), refund: false,
            cooldown: TimeSpan.FromMinutes(_config.GetCVar(JudgeLineCVars.HoaxCooldown)));
    }

    /// <summary>How many hoaxes a player has called in this round.</summary>
    public int Strikes(NetUserId user) => _strikes.GetValueOrDefault(user);

    /// <summary>Puts the relief crate down on the stranded ship beside the caller, or by the dock.</summary>
    private void HandOverRelief(WGJudgeLineCall call, WGEncounterShipState ship, EntityUid grid)
    {
        call.Relief = true;
        var at = Transform(call.Caller).GridUid == grid
            ? Transform(call.Caller).Coordinates
            : _docking.GetDocks(grid).FirstOrDefault(dock => dock.Comp.DockedWith is { } other && Transform(other).GridUid == ship.Grid) is { Owner: var port } && port.IsValid()
                ? Transform(port).Coordinates
                : new EntityCoordinates(grid, Vector2.Zero);
        Spawn(ReliefCrate, at);
        Notify(call, Loc.GetString("wg-judgeline-status-relief"));
    }

    /// <summary>NPCs aboard a ship that don't belong there: anyone hostile to the caller, or another ship's crew.</summary>
    private List<EntityUid> Hostiles(WGJudgeLineCall call, EntityUid grid)
    {
        var found = new List<EntityUid>();
        var responder = Ship(call);
        var query = EntityQueryEnumerator<HTNComponent, MobStateComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var mob, out var xform))
        {
            if (xform.GridUid != grid || !_mobState.IsAlive(uid, mob) || HasComp<ActorComponent>(uid))
                continue;

            if (TryComp<WGCrewComponent>(uid, out var crew))
            {
                // The ship's own NPC crew, and the judges, are no boarders.
                if ((responder != null && crew.Group == responder.Group) || crew.Post?.EntityId == grid)
                    continue;
                found.Add(uid);
                continue;
            }

            if (IsHostileTo(uid, call.Caller))
                found.Add(uid);
        }

        return found;
    }

    /// <summary>Whether an NPC's factions are set against someone's: a mouse or a pet is no boarder.</summary>
    private bool IsHostileTo(EntityUid npc, EntityUid target)
    {
        if (!TryComp<NpcFactionMemberComponent>(npc, out var member) || !TryComp<NpcFactionMemberComponent>(target, out var other))
            return false;

        var hostile = member.HostileFactions;
        return hostile.Overlaps(other.Factions);
    }

    /// <summary>Whoever is attacking the caller in person: those who hurt them lately and hostile NPCs close by.</summary>
    private List<EntityUid> Assailants(WGJudgeLineCall call)
    {
        var found = new List<EntityUid>();
        var now = _timing.CurTime;
        if (TryComp<WGJudgeLineCallerComponent>(call.Caller, out var caller))
        {
            foreach (var (assailant, at) in caller.Assailants)
            {
                if (now - at <= HitMemory && !TerminatingOrDeleted(assailant) && _mobState.IsAlive(assailant)
                    && !HasComp<MapGridComponent>(assailant))
                    found.Add(assailant);
            }
        }

        var here = _transform.GetMapCoordinates(call.Caller);
        var responder = Ship(call);
        var query = EntityQueryEnumerator<HTNComponent, MobStateComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var mob, out var xform))
        {
            if (found.Contains(uid) || xform.MapID != here.MapId || !_mobState.IsAlive(uid, mob)
                || (_transform.GetWorldPosition(xform) - here.Position).LengthSquared() > AssaultRange * AssaultRange)
                continue;
            if (TryComp<WGCrewComponent>(uid, out var crew) && responder != null && crew.Group == responder.Group)
                continue;
            if (IsHostileTo(uid, call.Caller))
                found.Add(uid);
        }

        return found;
    }

    /// <summary>People who need a medic: on the target ship, or the caller wherever they are.</summary>
    private List<EntityUid> Patients(WGJudgeLineCall call, EntityUid? grid)
    {
        var found = new List<EntityUid>();
        if (NeedsMedic(call.Caller))
            found.Add(call.Caller);
        if (grid == null)
            return found;

        var query = EntityQueryEnumerator<MindContainerComponent, MobStateComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var mind, out _, out var xform))
        {
            if (uid != call.Caller && xform.GridUid == grid && mind.HasMind && !HasComp<WGCrewComponent>(uid) && NeedsMedic(uid))
                found.Add(uid);
        }

        return found;
    }

    private bool NeedsMedic(EntityUid uid)
    {
        if (_mobState.IsCritical(uid) || _mobState.IsDead(uid))
            return true;

        return TryComp<DamageableComponent>(uid, out var damage) && damage.TotalDamage >= HurtDamage;
    }

    /// <summary>Whether a ship has any working thrust.</summary>
    private bool HasThrust(EntityUid grid)
    {
        return !TryComp<ShuttleComponent>(grid, out var shuttle) || shuttle.LinearThrust.Any(thrust => thrust > 0f);
    }

    private bool DockedWith(EntityUid grid, EntityUid other)
    {
        foreach (var dock in _docking.GetDocks(grid))
        {
            if (dock.Comp.DockedWith is { } port && Transform(port).GridUid == other)
                return true;
        }

        return false;
    }

    /// <summary>The public station nearest a grid on its map, but for any skipped.</summary>
    private EntityUid? NearestStation(EntityUid grid, HashSet<EntityUid>? skip = null)
    {
        var here = _transform.GetMapCoordinates(grid);
        EntityUid? best = null;
        var bestDistance = float.MaxValue;
        var query = EntityQueryEnumerator<StationMemberComponent, MapGridComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out _, out var xform))
        {
            if (xform.MapID != here.MapId || !IsStation(uid) || _scheduler.IsHidden(uid) || skip?.Contains(uid) == true
                || _docking.GetDocks(uid).Count == 0)
                continue;

            var distance = (_transform.GetWorldPosition(xform) - here.Position).LengthSquared();
            if (distance < bestDistance)
            {
                best = uid;
                bestDistance = distance;
            }
        }

        return best;
    }

    /// <summary>The responder's living crew.</summary>
    private IEnumerable<Entity<WGCrewComponent>> Crew(WGEncounterShipState ship)
    {
        var query = EntityQueryEnumerator<WGCrewComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var crew, out var xform))
        {
            if (crew.Group == ship.Group && _mobState.IsAlive(uid) && !HasComp<ActorComponent>(uid))
                yield return (uid, crew);
        }
    }

    private float Distance(EntityUid grid, MapCoordinates point)
    {
        var at = _transform.GetMapCoordinates(grid);
        return at.MapId == point.MapId ? (at.Position - point.Position).Length() : float.MaxValue;
    }

    /// <summary>Has the responder say a line on the radio, heard around the caller.</summary>
    private void Say(WGJudgeLineCall call, WGEncounterShipState ship, string text)
    {
        _encounters.TrySay(ship, Channel, text, near: TargetGrid(call) ?? ship.Grid);
    }
}
