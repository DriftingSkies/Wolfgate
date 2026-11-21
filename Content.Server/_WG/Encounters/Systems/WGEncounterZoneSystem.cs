using System.Linq;
using System.Numerics;
using Content.Server._Mono.AlertLevel;
using Content.Server._WG.Encounters.Components;
using Content.Server._WG.NpcCrew.Components;
using Content.Server._WG.NpcCrew.Systems;
using Content.Server._WG.NpcDialogue;
using Content.Server._WG.NpcDialogue.Systems;
using Content.Shared._Mono.Company;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WG.Encounters;
using Content.Shared._WG.NpcCrew;
using Content.Shared.Ghost;
using Content.Shared.Mobs.Systems;
using Content.Shared.Projectiles;
using Content.Shared.Radio;
using Content.Shared.Station.Components;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WG.Encounters.Systems;

/// <summary>
/// The warning and attack zones around encounter ships. A player ship that is not of the ship's company is told
/// over the radio to turn away inside the warning zone, and is fired on inside the attack zone for as long as it
/// stays there.
/// </summary>
public sealed partial class WGEncounterZoneSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private WGEncounterSystem _encounters = default!;
    [Dependency] private WGCrewAlertSystem _alerts = default!;
    [Dependency] private WGCrewEscortSystem _escorts = default!;
    [Dependency] private WGCrewShipStatusSystem _status = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WarLevelSystem _warLevel = default!;
    [Dependency] private WGCrewStealthSystem _stealth = default!;
    [Dependency] private WGCrewSparedSystem _spared = default!;
    [Dependency] private WGNpcDialogueSystem _dialogue = default!;

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan WarnCooldown = TimeSpan.FromSeconds(45);

    /// <summary>How long after a side calls its guns onto a vessel it may call them again, if it never let the vessel go.</summary>
    private static readonly TimeSpan CallCooldown = TimeSpan.FromSeconds(60);
    private static readonly ProtoId<RadioChannelPrototype> Channel = "Traffic";

    /// <summary>Starts part way through the second, off the tick other one-second systems run on.</summary>
    private TimeSpan _next = TimeSpan.FromSeconds(0.2);
    private readonly Dictionary<EntityUid, Crewed> _crewed = new();

    /// <summary>
    /// How long a zone holds its fire on a ship its crew have just come back aboard: the ship was let be while empty,
    /// so its people hear a warning before the guns. Fresh fire from it is answered at once.
    /// </summary>
    public static readonly TimeSpan BoardingGrace = TimeSpan.FromSeconds(20);

    /// <summary>
    /// How long a zone holds its fire on a hull boarders have just seized: it is asked who flies it before the guns
    /// open. Fresh fire from it is answered at once.
    /// </summary>
    public static readonly TimeSpan SeizedGrace = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Grids with a living player aboard, berthed or not; those that had one on the last look; and those that ever
    /// had one. A ship casting off from a station kept its crew, so only one that lay empty is reboarded.
    /// </summary>
    private readonly HashSet<EntityUid> _aboard = new();
    private readonly HashSet<EntityUid> _aboardBefore = new();
    private readonly HashSet<EntityUid> _everCrewed = new();

    /// <summary>When each ship's crew came back aboard after it lay empty.</summary>
    private readonly Dictionary<EntityUid, TimeSpan> _reboarded = new();
    private readonly List<EntityUid> _stale = new();
    private readonly List<(EntityUid Encounter, WGEncounterShipState Ship)> _fleet = new();
    private readonly HashSet<(EntityUid Ship, EntityUid Other)> _rivals = new();
    private readonly List<(EntityUid Shot, EntityUid Shooter)> _shots = new();
    private readonly HashSet<EntityUid> _liveShots = new();
    private readonly HashSet<(EntityUid Ship, EntityUid Shot)> _answeredShots = new();

    /// <summary>A pass is spread over this share of the interval, a slice of ships a tick, so no tick carries them all.</summary>
    private const float SpreadShare = 0.5f;

    /// <summary>The next ship of this pass's <see cref="_fleet"/> to watch, and how many to watch a tick.</summary>
    private int _cursor;
    private int _perTick = 1;

    /// <summary>Hull centres read this tick.</summary>
    private readonly Dictionary<EntityUid, MapCoordinates> _centres = new();

    /// <summary>Each grid's docked ports this pass: the grid alongside, or null for one docked to something on no grid.</summary>
    private readonly Dictionary<EntityUid, List<EntityUid?>> _docked = new();
    private readonly Stack<List<EntityUid?>> _dockLists = new();
    private readonly Stack<Crewed> _crewedPool = new();

    /// <summary>Set when a dock joins or parts mid-pass, so the next slice re-indexes before judging anyone.</summary>
    private bool _docksDirty;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<Content.Server.Shuttles.Events.DockEvent>(_ => _docksDirty = true);
        SubscribeLocalEvent<Content.Server.Shuttles.Events.UndockEvent>(_ => _docksDirty = true);
    }

    /// <summary>A ship with a living player aboard.</summary>
    private sealed class Crewed
    {
        /// <summary>The companies it flies for: its own, or failing that its players'.</summary>
        public readonly List<string> Flags = new();

        /// <summary>The companies of the living players aboard.</summary>
        public readonly HashSet<string> Aboard = new();

        /// <summary>The company the ship itself flies, or empty for none.</summary>
        public string GridFlag = string.Empty;

        /// <summary>Whether a living player aboard is of no company.</summary>
        public bool Unflagged;

        /// <summary>The flags it flies as exact company ids, so a civilian arm is not taken for its navy.</summary>
        public readonly HashSet<string> RawFlags = new();

        public void Reset()
        {
            Flags.Clear();
            Aboard.Clear();
            RawFlags.Clear();
            GridFlag = string.Empty;
            Unflagged = false;
        }
    }

    /// <summary>
    /// Once a second a pass takes stock of the fleet, the crewed ships, the shots in flight and the docks; its ships
    /// are then watched a slice a tick over the first half of the second.
    /// </summary>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        _centres.Clear();
        if (_timing.CurTime >= _next)
        {
            // A pass not done in time is finished before the next one starts.
            WatchSlice(_fleet.Count);
            _next = _timing.CurTime + Interval;
            StartPass();
        }

        WatchSlice(_perTick);
    }

    /// <summary>Takes stock for a pass: the ships that watch or can be watched, and what every ship is judged by.</summary>
    private void StartPass()
    {
        var watching = WatchFleet();
        IndexDocks();
        if (watching)
        {
            FindCrewedShips();
            GatherShots();
        }

        _cursor = 0;
        var ticks = Math.Max(1, (int) (_timing.TickRate * Interval.TotalSeconds * SpreadShare));
        _perTick = Math.Max(1, (_fleet.Count + ticks - 1) / ticks);
    }

    /// <summary>Watches the next ships of the pass: their rivals in the fleet, then their zones.</summary>
    private void WatchSlice(int count)
    {
        if (_docksDirty && _cursor < _fleet.Count)
            IndexDocks();

        for (var done = 0; done < count && _cursor < _fleet.Count; done++)
        {
            var (uid, ship) = _fleet[_cursor++];
            if (!TryComp<WGEncounterComponent>(uid, out var encounter) || Stood(encounter) || !InFleet(ship))
                continue;

            WatchRivals(uid, encounter, ship);
            // A ship lying in wait gives itself away to nobody.
            if (ship.WarnRange > 0f || ship.AttackRange > 0f)
                Watch(encounter, ship);
        }
    }

    /// <summary>Whether a ship has a zone or can be another's intruder: built, not virtual or lurking, and still in the fight.</summary>
    private bool InFleet(WGEncounterShipState ship)
    {
        // A hull still being built has no crew or company yet: no zone of its own, and nobody's intruder.
        return !ship.Lurking && !ship.Virtual && !ship.Building && !TerminatingOrDeleted(ship.Grid) && !Disabled(ship);
    }

    /// <summary>Where a hull's centre of mass is, read once a tick.</summary>
    private MapCoordinates Centre(EntityUid grid)
    {
        if (!_centres.TryGetValue(grid, out var at))
            _centres[grid] = at = CentreOfMass(grid);
        return at;
    }

    /// <summary>Indexes every docked port by its grid once a pass, so no check walks every dock in the sector.</summary>
    private void IndexDocks()
    {
        foreach (var list in _docked.Values)
        {
            list.Clear();
            _dockLists.Push(list);
        }

        _docked.Clear();
        _docksDirty = false;
        var docks = EntityQueryEnumerator<Content.Server.Shuttles.Components.DockingComponent, TransformComponent>();
        while (docks.MoveNext(out _, out var dock, out var xform))
        {
            if (xform.GridUid is not { } grid || dock.DockedWith is not { } other)
                continue;

            if (!_docked.TryGetValue(grid, out var alongside))
                _docked[grid] = alongside = _dockLists.TryPop(out var spare) ? spare : new List<EntityUid?>();
            alongside.Add(Transform(other).GridUid);
        }
    }

    /// <summary>
    /// Whether an encounter's zones have stood down. A resolved encounter's have, except a battle's: whoever won a
    /// decided fight holds the field, and its zones stand until its ships are cleaned up.
    /// </summary>
    public static bool Stood(WGEncounterComponent encounter)
    {
        return encounter.Resolution is { } resolution && resolution != WGEncounterResolution.Decided;
    }

    /// <summary>Lists this pass's fleet: every encounter ship with a zone or that can be another's intruder. True if any has a zone.</summary>
    private bool WatchFleet()
    {
        _fleet.Clear();
        var watching = false;
        var encounters = EntityQueryEnumerator<WGEncounterComponent>();
        while (encounters.MoveNext(out var uid, out var encounter))
        {
            if (Stood(encounter))
                continue;

            foreach (var ship in encounter.Ships.Values)
            {
                if (!InFleet(ship))
                    continue;

                _fleet.Add((uid, ship));
                watching |= ship.WarnRange > 0f || ship.AttackRange > 0f;
            }
        }

        _rivals.RemoveWhere(pair => TerminatingOrDeleted(pair.Ship) || TerminatingOrDeleted(pair.Other));
        return watching;
    }

    /// <summary>
    /// Encounter ships answer each other's zones too: one that comes inside another's zone, and is a ship that zone
    /// minds, is taken for an enemy, and takes the zone's owner for one in turn.
    /// </summary>
    private void WatchRivals(EntityUid uid, WGEncounterComponent encounter, WGEncounterShipState ship)
    {
        // An interrogating ship turns on nobody by itself: it answers fire and rivals that turn on it.
        var range = MathF.Max(ship.WarnRange, ship.AttackRange);
        if (range <= 0f || ship.ZoneTargets == WGEncounterZoneTargets.Interrogate || IsDocked(ship.Grid))
            return;

        var here = Centre(ship.Grid);
        var company = Company(ship.Grid);
        foreach (var (other, rival) in _fleet)
        {
            if (rival.Grid == ship.Grid || TerminatingOrDeleted(rival.Grid))
                continue;

            var key = (ship.Grid, rival.Grid);
            var there = Centre(rival.Grid);
            var near = there.MapId == here.MapId && (there.Position - here.Position).Length() <= range;
            // Out of range and never called on, nothing below would change.
            if (!near && !_rivals.Contains(key))
                continue;

            // Its own side and its formation are not intruders, nor are ships of its own company.
            if (other == uid && rival.Side == ship.Side || _escorts.AreInFormation(ship.Grid, rival.Grid))
                continue;

            // The sides of an encounter that hasn't begun hold their fire on each other until a player comes near.
            if (other == uid && !encounter.Begun)
                continue;

            var theirs = Company(rival.Grid);
            if (WGCompanyFamily.Same(company, theirs) || IsNeutral(ship, theirs))
                continue;
            // The sides of one encounter were set on each other when it was written, ceasefire or no.
            var enemies = other == uid ? Opposed(company, theirs, true) : AtWar(company, theirs);
            if (ship.ZoneTargets == WGEncounterZoneTargets.AtWar && !enemies)
                continue;

            if (!near || _spared.HoldsFireOn(ship.Grid, rival.Grid) || _spared.HoldsFireOn(rival.Grid, ship.Grid))
            {
                _rivals.Remove(key);
                continue;
            }

            // No warning between crews: each takes the other for an enemy, and the side says so once.
            _alerts.ReportZoneThreat(ship.Grid, ship.Group, rival.Grid);
            _alerts.ReportZoneThreat(rival.Grid, rival.Group, ship.Grid);
            if (_rivals.Add(key))
            {
                var rivalGrid = rival.Grid;
                CallOut(encounter, ship, rivalGrid, () => _dialogue.AttackLine(encounter, ship, rivalGrid, Name(rivalGrid), false));
            }
        }
    }

    /// <summary>Every ship with a living player aboard. Zones answer to ships people are flying, not to stations or debris.</summary>
    private void FindCrewedShips()
    {
        foreach (var crewed in _crewed.Values)
        {
            crewed.Reset();
            _crewedPool.Push(crewed);
        }

        _crewed.Clear();
        _aboard.Clear();
        var players = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (players.MoveNext(out var uid, out _, out var xform))
        {
            // A pilot's mech flying free of any grid is a vessel of its own.
            if ((xform.GridUid ?? _alerts.FreeMechOf(uid)) is not { } grid || HasComp<GhostComponent>(uid) || _mobs.IsDead(uid))
                continue;

            _aboard.Add(grid);

            if (!_crewed.TryGetValue(grid, out var crewed))
            {
                if (!IsPlayerShip(grid) || _encounters.DockedAtStation(grid) || _stealth.ShipUnseen(grid))
                    continue;

                crewed = _crewedPool.TryPop(out var spare) ? spare : new Crewed();
                crewed.GridFlag = WGCompanyFamily.Of(Colours(grid));
                _crewed[grid] = crewed;
            }

            // Companies are kept by faction: a Federation civilian aboard flies a Federation flag.
            var own = WGCompanyFamily.Of(Company(uid));
            var company = WGCompanyFamily.Of(Colours(grid));
            if (company.Length == 0)
                company = own;
            if (company.Length > 0 && !crewed.Flags.Contains(company))
                crewed.Flags.Add(company);
            var raw = Colours(grid);
            if (raw.Length == 0)
                raw = Company(uid);
            if (raw.Length > 0)
                crewed.RawFlags.Add(raw);
            if (own.Length > 0)
                crewed.Aboard.Add(own);
            else
                crewed.Unflagged = true;
        }

        NoteBoardings();
    }

    /// <summary>Marks the ships whose crew came back aboard since the last look, for <see cref="BoardingGrace"/>.</summary>
    private void NoteBoardings()
    {
        var now = _timing.CurTime;
        foreach (var grid in _aboard)
        {
            if (!_aboardBefore.Contains(grid) && !_everCrewed.Add(grid))
                _reboarded[grid] = now;
        }

        _aboardBefore.Clear();
        _aboardBefore.UnionWith(_aboard);
        _everCrewed.RemoveWhere(grid => TerminatingOrDeleted(grid));
        foreach (var (grid, at) in _reboarded)
        {
            if (now - at >= BoardingGrace || TerminatingOrDeleted(grid))
                _stale.Add(grid);
        }

        foreach (var grid in _stale)
        {
            _reboarded.Remove(grid);
        }

        _stale.Clear();
    }

    /// <summary>Whether a ship's crew came back aboard too recently for a zone to open fire on it unprovoked.</summary>
    public bool JustReboarded(EntityUid grid)
    {
        return _reboarded.ContainsKey(grid);
    }

    /// <summary>Whether a ship's crew came aboard by seizing it too recently for a zone to open fire on it unprovoked.</summary>
    public bool JustSeized(EntityUid grid)
    {
        return TryComp<WGCrewSeizedHullComponent>(grid, out var seized) && _timing.CurTime - seized.Since < SeizedGrace;
    }

    /// <summary>The company a ship flies for; a seized hull's colours are not its people's, so it flies for none.</summary>
    private string Colours(EntityUid grid)
    {
        return HasComp<WGCrewSeizedHullComponent>(grid) ? string.Empty : Company(grid);
    }

    /// <summary>
    /// Whether a grid is a ship people fly: not a station, outpost or asteroid, and not one of an encounter's own
    /// unless boarders have seized it.
    /// </summary>
    private bool IsPlayerShip(EntityUid grid)
    {
        if (HasComp<WGCrewSeizedHullComponent>(grid))
            return true;

        if (HasComp<WGEncounterGridComponent>(grid))
            return false;

        // Stations, outposts and asteroids are station members; a deed makes one a ship somebody owns.
        return HasComp<ShuttleDeedComponent>(grid) || !HasComp<StationMemberComponent>(grid);
    }

    /// <summary>
    /// Whether a ship is out of the fight, a derelict aside: a disabled or surrendered one no longer hails, fires or
    /// answers rivals.
    /// </summary>
    private bool Disabled(WGEncounterShipState ship)
    {
        return ship.Surrendered || !ship.Derelict && _status.IsDisabled(ship.Grid);
    }

    /// <summary>Whether any port of a grid is docked to another, a ship of its own company and encounter fleet aside: its garrison is not a berth.</summary>
    private bool IsDocked(EntityUid grid)
    {
        if (!_docked.TryGetValue(grid, out var ports))
            return false;

        foreach (var port in ports)
        {
            if (port is { } alongside && HasComp<WGEncounterGridComponent>(alongside)
                && WGCompanyFamily.Same(Company(grid), Company(alongside)))
                continue;

            return true;
        }

        return false;
    }

    /// <summary>Whether a grid is docked to a station that flies one of the neutral companies' flags.</summary>
    private bool BerthedAtNeutral(EntityUid grid, List<string> neutral)
    {
        if (!_docked.TryGetValue(grid, out var ports))
            return false;

        foreach (var port in ports)
        {
            if (port is { } alongside && _encounters.IsNeutralStation(alongside, neutral))
                return true;
        }

        return false;
    }

    /// <summary>Forgets intruders that left the map or the crewed ships, so a ship that comes back is told again.</summary>
    private void Forget(WGEncounterComponent encounter, WGEncounterShipState ship)
    {
        if (ship.Engaged.Count == 0 && ship.Warned.Count == 0)
            return;

        var map = Transform(ship.Grid).MapID;
        _stale.Clear();
        foreach (var uid in ship.Engaged)
        {
            if (IsGone(uid, map))
                _stale.Add(uid);
        }

        foreach (var uid in _stale)
        {
            Release(encounter, ship, uid);
        }

        _stale.Clear();
        foreach (var uid in ship.Warned.Keys)
        {
            if (IsGone(uid, map))
                _stale.Add(uid);
        }

        foreach (var uid in _stale)
        {
            ship.Warned.Remove(uid);
        }
    }

    private bool IsGone(EntityUid intruder, MapId map)
    {
        return TerminatingOrDeleted(intruder) || !_crewed.ContainsKey(intruder) || Transform(intruder).MapID != map;
    }

    /// <summary>An entity's company id, or empty for none.</summary>
    private string Company(EntityUid uid)
    {
        return CompOrNull<CompanyComponent>(uid)?.CompanyName.Id is { } id && id != "None" ? id : string.Empty;
    }

    /// <summary>
    /// Whether two companies are at war: either one's standing lists the other, outright or as a war that has since
    /// been declared.
    /// </summary>
    public bool AtWar(string first, string second)
    {
        return Opposed(first, second, _warLevel.GetWarLevel(EntityUid.Invalid));
    }

    /// <summary>Whether only the ceasefire keeps two companies from war: they are at war once it is declared.</summary>
    public bool UnderCeasefire(string first, string second)
    {
        return !_warLevel.GetWarLevel(EntityUid.Invalid) && !Opposed(first, second, false) && Opposed(first, second, true);
    }

    private bool UnderCeasefire(string company, List<string> flags)
    {
        foreach (var flag in flags)
        {
            if (UnderCeasefire(company, flag))
                return true;
        }

        return false;
    }

    /// <summary>Whether either company's standing lists the other, counting declared wars if one has been.</summary>
    private bool Opposed(string first, string second, bool declared)
    {
        if (first.Length == 0 || second.Length == 0 || first == second)
            return false;

        return Lists(first, second, declared) || Lists(second, first, declared);
    }

    private bool Lists(string company, string other, bool declared)
    {
        return _prototypes.TryIndex<WGStandingPrototype>(company, out var standing)
               && (standing.AtWar.Contains(other) || declared && standing.DeclaredWar.Contains(other));
    }

    private bool AtWar(string company, List<string> flags)
    {
        foreach (var flag in flags)
        {
            if (AtWar(company, flag))
                return true;
        }

        return false;
    }

    /// <summary>Whether a company is one the ship's zones leave alone.</summary>
    private static bool IsNeutral(WGEncounterShipState ship, string company)
    {
        var family = WGCompanyFamily.Of(company);
        if (family.Length == 0)
            return false;

        foreach (var neutral in ship.NeutralCompanies)
        {
            if (WGCompanyFamily.Of(neutral) == family)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Whether an intruder is left alone: it flies a neutral flag or, with none, has none but neutral people aboard,
    /// or it is docked to a neutral company's station.
    /// </summary>
    private bool IsNeutral(WGEncounterShipState ship, EntityUid intruder, Crewed crewed)
    {
        if (ship.NeutralCompanies.Count == 0)
            return false;

        if (crewed.GridFlag.Length > 0)
        {
            if (IsNeutral(ship, crewed.GridFlag))
                return true;
        }
        else if (!crewed.Unflagged && crewed.Aboard.Count > 0 && crewed.Aboard.All(company => IsNeutral(ship, company)))
            return true;

        return BerthedAtNeutral(intruder, ship.NeutralCompanies);
    }

    private void Watch(WGEncounterComponent encounter, WGEncounterShipState ship)
    {
        Forget(encounter, ship);
        ship.Strangers.Clear();
        var here = Centre(ship.Grid);
        var reach = MathF.Max(ship.WarnRange, ship.AttackRange);
        AnswerFire(encounter, ship, here);

        // A ship in port is not on guard: whatever is berthed beside it is none of its business.
        if (IsDocked(ship.Grid))
            return;

        var company = WGCompanyFamily.Of(Company(ship.Grid));
        var now = _timing.CurTime;
        foreach (var (intruder, crewed) in _crewed)
        {
            if (intruder == ship.Grid || TerminatingOrDeleted(intruder))
                continue;

            // Out of reach with nothing held against it, every branch below changes nothing.
            if (!ship.Engaged.Contains(intruder) && !ship.Warned.ContainsKey(intruder)
                && !_alerts.HasZoneWarning(ship.Grid, intruder))
            {
                var far = Centre(intruder);
                if (far.MapId != here.MapId || (far.Position - here.Position).Length() > reach)
                    continue;
            }

            // The crewed list is from the pass's start: one that has since berthed at a station is in sanctuary.
            if (_encounters.DockedAtStation(intruder))
            {
                Release(encounter, ship, intruder);
                _alerts.EndZoneWarning(ship.Grid, intruder);
                continue;
            }

            // Formation partners and the ships a side has taken as allies are not intruders.
            if (_escorts.AreInFormation(ship.Grid, intruder))
                continue;

            // Its own side comes and goes as it likes: a hull showing its colours, or one with its people free aboard.
            // A ship that minds only NPCs lets every player vessel by. Their fire is still answered.
            if (ship.NpcTargetsOnly || _spared.Spared(ship.Grid, intruder) || _spared.HoldsFireOn(ship.Grid, intruder))
            {
                Release(encounter, ship, intruder);
                _alerts.EndZoneWarning(ship.Grid, intruder);
                continue;
            }

            // Neutral companies, and ships berthed with one, are left alone whatever their IFF says.
            if (IsNeutral(ship, intruder, crewed))
            {
                Release(encounter, ship, intruder);
                _alerts.EndZoneWarning(ship.Grid, intruder);
                continue;
            }

            // An interrogating zone minds only a declared threat or an attacker, IFF or none; anyone else in the
            // warning zone is left to be asked.
            if (ship.ZoneTargets == WGEncounterZoneTargets.Interrogate && !IsThreat(ship, intruder))
            {
                Release(encounter, ship, intruder);
                _alerts.EndZoneWarning(ship.Grid, intruder);
                var at = Centre(intruder);
                if (ship.WarnRange > 0f && at.MapId == here.MapId && (at.Position - here.Position).Length() <= ship.WarnRange)
                    ship.Strangers.Add(intruder);
                continue;
            }

            // Anyone else is only known for what he is by his IFF. With that switched off he is nobody's friend: he is
            // told to show himself and shot across the bows in the warning zone, and fired on in the attack zone. With
            // it on, a patrol only minds its faction's enemies.
            // A navy patrol knows the station's own people and the judges by their codes, IFF or none: their ships, the
            // Roswell among them, fly dark by design. They are judged by their company like anyone showing an IFF.
            var masked = IffMasked(intruder) && !(ship.ZoneTargets == WGEncounterZoneTargets.AtWar && StationAuthority(crewed));
            // A ship minding only known threats leaves a player hull running dark alone; its fire is still answered.
            if (masked && ship.KnownThreatsOnly)
            {
                Release(encounter, ship, intruder);
                _alerts.EndZoneWarning(ship.Grid, intruder);
                continue;
            }

            // A civilian ship showing its IFF is never a navy patrol's target, whichever navy its faction is at war with.
            // A zone that warns everyone off, like the USSP remnant's, still does.
            if (!masked && ship.ZoneTargets == WGEncounterZoneTargets.AtWar && IsCivilian(crewed))
            {
                Release(encounter, ship, intruder);
                _alerts.EndZoneWarning(ship.Grid, intruder);
                continue;
            }

            // Under the ceasefire the other navy's ships are warned off like any enemy, and fired on only once the
            // ship has had to answer them.
            var ceasefire = !masked && ship.ZoneTargets == WGEncounterZoneTargets.AtWar && !AtWar(company, crewed.Flags)
                            && UnderCeasefire(company, crewed.Flags);
            if (!masked && ship.ZoneTargets == WGEncounterZoneTargets.AtWar && !ceasefire && !AtWar(company, crewed.Flags))
                continue;

            var there = Centre(intruder);
            if (there.MapId != here.MapId)
                continue;

            // An unknown vessel berthed somewhere is in port, not trespassing: the patrol came to it, and lets it be.
            if (masked && IsDocked(intruder))
            {
                Release(encounter, ship, intruder);
                _alerts.EndZoneWarning(ship.Grid, intruder);
                continue;
            }

            var challenged = masked && ship.ZoneTargets == WGEncounterZoneTargets.AtWar;
            var name = masked ? Loc.GetString("wg-encounter-zone-unknown") : Name(intruder);
            var distance = (there.Position - here.Position).Length();
            // One lying still is hailed to show itself and no more, wherever the zone finds it. Under way, or once
            // fired on, it is treated as any unknown.
            var idle = masked && ship.ZoneTargets != WGEncounterZoneTargets.Interrogate && !ship.Engaged.Contains(intruder)
                       && (!TryComp<Robust.Shared.Physics.Components.PhysicsComponent>(intruder, out var body)
                           || body.LinearVelocity.LengthSquared() < IdleSpeed * IdleSpeed);
            if (idle)
                _alerts.EndZoneWarning(ship.Grid, intruder);

            // A ship badly hurt picks no new fights: it only answers one that is on it.
            var holdsBack = ship.Mood >= WGShipMood.Desperate && !ship.Engaged.Contains(intruder)
                            && !_alerts.IsAttacker(ship.Grid, intruder);
            if (ship.AttackRange > 0f && distance <= ship.AttackRange && !idle && !holdsBack && !JustReboarded(intruder)
                && !JustSeized(intruder)
                && (!ceasefire || ship.Engaged.Contains(intruder) || _alerts.IsAttacker(ship.Grid, intruder)))
            {
                _alerts.EndZoneWarning(ship.Grid, intruder);
                if (ship.Engaged.Add(intruder))
                {
                    // The ship turns to fight what is inside its attack zone; its side says so once.
                    _alerts.ReportZoneEngagement(ship.Grid, ship.Group, intruder);
                    CallOut(encounter, ship, intruder, () => _dialogue.AttackLine(encounter, ship, intruder, name, false));
                }
                else
                    _alerts.ReportZoneThreat(ship.Grid, ship.Group, intruder);
            }
            else if (ship.WarnRange > 0f && distance <= ship.WarnRange)
            {
                // Already fired on, it gets no second warning on its way out: the guns stay on it until it is clear.
                if (ship.Engaged.Contains(intruder))
                {
                    _alerts.ReportZoneThreat(ship.Grid, ship.Group, intruder);
                    continue;
                }

                if (challenged && !idle)
                    _alerts.ReportZoneWarning(ship.Grid, ship.Group, intruder);
                if (ship.Warned.TryGetValue(intruder, out var until) && now < until)
                    continue;

                ship.Warned[intruder] = now + WarnCooldown;
                // One hail for the side: a dozen ships warning in chorus is noise, not a warning. The side warns once,
                // then once more, then holds its peace until the vessel leaves and comes back.
                // A hull the side knew as its own is asked who has it now.
                HailOut(encounter, ship, intruder, HasComp<WGCrewSeizedHullComponent>(intruder)
                    ? () => Loc.GetString("wg-encounter-zone-seized", ("intruder", name))
                    : challenged
                        ? () => Loc.GetString("wg-encounter-zone-identify", ("distance", (int) distance))
                        : () => _dialogue.WarnLine(encounter, ship, intruder, name, (int) distance));
            }
            else if (distance > ship.WarnRange)
            {
                // Out and back in is a fresh trespass, and the side remembers it.
                if (ship.Warned.ContainsKey(intruder) || ship.Engaged.Contains(intruder))
                    _dialogue.Left(encounter, ship, intruder, ship.Engaged.Contains(intruder));
                Release(encounter, ship, intruder);
                _alerts.EndZoneWarning(ship.Grid, intruder);
            }
        }
    }

    /// <summary>Whether an interrogating ship has cause to fire on a vessel: it declared itself a threat, fired on the ship, or is already engaged.</summary>
    public bool IsThreat(WGEncounterShipState ship, EntityUid intruder)
    {
        return ship.Declared.Contains(intruder) || ship.Engaged.Contains(intruder) || _alerts.IsAttacker(ship.Grid, intruder);
    }

    /// <summary>Where a shot is now, read once a tick.</summary>
    private MapCoordinates ShotPosition(EntityUid shot)
    {
        if (!_centres.TryGetValue(shot, out var at))
            _centres[shot] = at = _transform.GetMapCoordinates(shot);
        return at;
    }

    /// <summary>Below this speed, in metres a second, a vessel is lying still.</summary>
    private const float IdleSpeed = 2f;

    /// <summary>Every ship-weapon or free-flying mech round in flight, with the grid or mech it was fired from.</summary>
    private void GatherShots()
    {
        _shots.Clear();
        _liveShots.Clear();
        var query = EntityQueryEnumerator<ProjectileComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var projectile, out _))
        {
            _liveShots.Add(uid);
            var shooter = _alerts.IsShipWeapon(projectile.Weapon)
                ? Transform(projectile.Weapon!.Value).GridUid
                : _alerts.FreeMechOf(projectile.Weapon ?? projectile.Shooter);
            if (shooter is not { } source)
                continue;

            _shots.Add((uid, source));
        }

        _answeredShots.RemoveWhere(key => !_liveShots.Contains(key.Shot));
    }

    /// <summary>
    /// Ship-weapon rounds from another vessel that come inside the attack zone are an attack, hit or miss: the ship
    /// answers them as it would a hit, wherever the vessel that fired them is, and says so when it is new to it.
    /// Rounds from its own side, its formation and its own guns are not; a few strays from its own faction are let pass.
    /// </summary>
    private void AnswerFire(WGEncounterComponent encounter, WGEncounterShipState ship, MapCoordinates here)
    {
        if (ship.AttackRange <= 0f)
            return;

        var reach = ship.AttackRange * ship.AttackRange;
        foreach (var shot in _shots)
        {
            // The pass gathered the shots; a slice later in it reads where each one is now, or skips it if gone.
            if (shot.Shooter == ship.Grid || TerminatingOrDeleted(shot.Shot))
                continue;

            var at = ShotPosition(shot.Shot);
            if (at.MapId != here.MapId || (at.Position - here.Position).LengthSquared() > reach
                || !_answeredShots.Add((ship.Grid, shot.Shot)))
                continue;

            if (_escorts.AreInFormation(ship.Grid, shot.Shooter) || SameSide(encounter, ship, shot.Shooter)
                || _spared.HoldsFireOn(ship.Grid, shot.Shooter))
                continue;

            var fresh = !_alerts.IsAttacker(ship.Grid, shot.Shooter);
            if (!_alerts.ReportIncomingFire(ship.Grid, ship.Group, shot.Shooter))
                continue;

            if (fresh)
            {
                var name = IffMasked(shot.Shooter) ? Loc.GetString("wg-encounter-zone-unknown") : Name(shot.Shooter);
                var shooter = shot.Shooter;
                CallOut(encounter, ship, shooter, () => _dialogue.AttackLine(encounter, ship, shooter, name, true));
            }
        }
    }

    /// <summary>
    /// Hails a vessel for the side, once per cooldown whichever of its ships speaks. A ship with nobody to speak gives
    /// the side's turn back, so the next ship takes it.
    /// </summary>
    private void HailOut(WGEncounterComponent encounter, WGEncounterShipState ship, EntityUid intruder, Func<string?> line)
    {
        Speak(encounter.Hailed, ship, intruder, WarnCooldown, line);
    }

    /// <summary>
    /// Calls the side's guns onto a vessel, once for the side while any of its ships holds the vessel in its attack zone
    /// and once per cooldown otherwise, since a shooter answered or a rival crew may never enter a zone to be let go of.
    /// </summary>
    private void CallOut(WGEncounterComponent encounter, WGEncounterShipState ship, EntityUid intruder, Func<string?> line)
    {
        Speak(encounter.Called, ship, intruder, CallCooldown, line);
    }

    /// <summary>
    /// Takes the side's turn to speak of a vessel if it is free, and gives it back if nobody aboard could or the air
    /// was too busy. The line is made only once the turn is the side's; null says nothing. It is heard around the
    /// ship and the vessel, not across the sector.
    /// </summary>
    private void Speak(Dictionary<(string Side, EntityUid Intruder), TimeSpan> turns, WGEncounterShipState ship, EntityUid intruder,
        TimeSpan cooldown, Func<string?> line)
    {
        var key = (ship.Side, intruder);
        var now = _timing.CurTime;
        if (turns.TryGetValue(key, out var until) && now < until)
            return;

        if (line() is not { } text)
            return;

        turns[key] = now + cooldown;
        if (!_encounters.TrySay(ship, Channel, text, intruder))
            turns.Remove(key);
    }

    /// <summary>A ship lets a vessel out of its attack zone; once no ship of its side holds it, the side may call it again.</summary>
    private static void Release(WGEncounterComponent encounter, WGEncounterShipState ship, EntityUid intruder)
    {
        if (!ship.Engaged.Remove(intruder))
            return;

        foreach (var other in encounter.Ships.Values)
        {
            if (other.Side == ship.Side && other.Engaged.Contains(intruder))
                return;
        }

        encounter.Called.Remove((ship.Side, intruder));
    }

    /// <summary>Whether a grid is another ship of the same encounter on the same side.</summary>
    private bool SameSide(WGEncounterComponent encounter, WGEncounterShipState ship, EntityUid grid)
    {
#pragma warning disable CS0618 // The caller only has the component here.
        return TryComp<WGEncounterGridComponent>(grid, out var marker) && marker.Encounter == encounter.Owner
            && encounter.Ships.TryGetValue(marker.Key, out var other) && other.Side == ship.Side;
#pragma warning restore CS0618
    }

    /// <summary>Whether a ship flies only civilian flags: a faction's civilian arm, or no company at all.</summary>
    private static bool IsCivilian(Crewed crewed) =>
        crewed.RawFlags.All(company => company.EndsWith("Civilian", StringComparison.Ordinal));

    /// <summary>Whether a ship has its IFF switched off, so nobody can tell whose it is.</summary>
    /// <summary>Companies a navy patrol knows whatever their IFF says: Caelestinus staff and the judges.</summary>
    private static readonly string[] StationCompanies = { "Colonial", "WGJudges" };

    /// <summary>Whether a ship flies for the station or carries its staff: its flag, or any player aboard.</summary>
    private static bool StationAuthority(Crewed crewed)
    {
        foreach (var company in StationCompanies)
        {
            if (crewed.GridFlag == company || crewed.Aboard.Contains(company))
                return true;
        }

        return false;
    }

    private bool IffMasked(EntityUid grid)
    {
        return TryComp<Content.Shared.Shuttles.Components.IFFComponent>(grid, out var iff)
               && (iff.Flags & (Content.Shared.Shuttles.Components.IFFFlags.HideLabel | Content.Shared.Shuttles.Components.IFFFlags.Hide)) != 0;
    }

    /// <summary>Where a hull's centre of mass is: the point the radar draws it and its zones around.</summary>
    private MapCoordinates CentreOfMass(EntityUid grid)
    {
        var centre = CompOrNull<Robust.Shared.Physics.Components.PhysicsComponent>(grid)?.LocalCenter ?? System.Numerics.Vector2.Zero;
        return _transform.ToMapCoordinates(new EntityCoordinates(grid, centre));
    }
}
