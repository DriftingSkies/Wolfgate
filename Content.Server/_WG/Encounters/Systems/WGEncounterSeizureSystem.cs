using Content.Server._WG.Encounters.Components;
using Content.Server._WG.NpcCrew.Components;
using Content.Server._WG.NpcCrew.Systems;
using Content.Shared._Mono.Company;
using Content.Shared._WG.NpcCrew;
using Content.Shared.Cuffs.Components;
using Content.Shared.Ghost;
using Content.Shared.Mobs.Systems;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WG.Encounters.Systems;

/// <summary>
/// Notices encounter ships that boarders have taken and marks them with <see cref="WGCrewSeizedHullComponent"/>: one
/// with none of its crew left alive aboard and a stranger on it, or with a stranger its crew is fighting at its helm.
/// A crew back aboard with the boarders gone takes the ship back. Hulks are left to their salvage claim.
/// </summary>
public sealed partial class WGEncounterSeizureSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private WGCrewSystem _crew = default!;
    [Dependency] private WGCrewAlertSystem _alerts = default!;
    [Dependency] private WGCrewSecuritySystem _security = default!;
    [Dependency] private WGCrewEscortSystem _escorts = default!;

    /// <summary>How long a ship must lie crewless with strangers aboard, or retaken with none, before it changes hands.</summary>
    public static readonly TimeSpan SeizeDelay = TimeSpan.FromSeconds(15);

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    /// <summary>Starts part way through the second, off the tick other one-second systems run on.</summary>
    private TimeSpan _next = TimeSpan.FromSeconds(0.4);

    private readonly Dictionary<EntityUid, List<EntityUid>> _strangers = new();
    private readonly Dictionary<EntityUid, List<EntityUid>> _crews = new();
    private readonly List<EntityUid> _held = new();
    private readonly HashSet<EntityUid> _due = new();
    private readonly List<EntityUid> _stale = new();

    /// <summary>Since when each ship's change of hands has been due without a break.</summary>
    private readonly Dictionary<EntityUid, TimeSpan> _pending = new();

    /// <summary>Whether boarders hold a ship.</summary>
    public bool IsSeized(EntityUid grid) => HasComp<WGCrewSeizedHullComponent>(grid);

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _next)
            return;

        _next = _timing.CurTime + Interval;
        Check();
    }

    /// <summary>One pass over the encounter ships with strangers aboard and the ships already seized.</summary>
    public void Check()
    {
        FindStrangers();
        _held.Clear();
        var seized = EntityQueryEnumerator<WGCrewSeizedHullComponent>();
        while (seized.MoveNext(out var uid, out _))
        {
            _held.Add(uid);
        }

        if (_strangers.Count == 0 && _held.Count == 0)
        {
            _pending.Clear();
            return;
        }

        FindCrews();
        _due.Clear();
        foreach (var (grid, strangers) in _strangers)
        {
            if (IsSeized(grid) || State(grid) is { Derelict: true })
                continue;

            var crew = _crews[grid];
            var helmsman = Helmsman(grid, strangers);
            if (crew.Count == 0 ? helmsman != null || Due(grid) : helmsman is { } pilot && Fought(grid, crew, pilot))
                Seize(grid);
        }

        foreach (var grid in _held)
        {
            // Claimed, it is its owner's ship and nobody's prize.
            if (!HasComp<WGEncounterGridComponent>(grid))
                RemComp<WGCrewSeizedHullComponent>(grid);
            else if (_crews[grid].Count > 0 && !_strangers.ContainsKey(grid) && Due(grid))
                Retake(grid);
        }

        _stale.Clear();
        foreach (var grid in _pending.Keys)
        {
            if (!_due.Contains(grid))
                _stale.Add(grid);
        }

        foreach (var grid in _stale)
        {
            _pending.Remove(grid);
        }
    }

    /// <summary>Marks an encounter ship as held by boarders now. False if it is no encounter's ship or already seized.</summary>
    public bool Seize(EntityUid grid)
    {
        if (!TryComp<WGEncounterGridComponent>(grid, out var marker) || IsSeized(grid))
            return false;

        AddComp<WGCrewSeizedHullComponent>(grid).Since = _timing.CurTime;
        _pending.Remove(grid);
        _escorts.Invalidate();
        Log.Info($"Encounter ship {ToPrettyString(grid)} was seized by boarders.");
        var ev = new WGEncounterShipSeizedEvent(grid, marker.Encounter);
        RaiseLocalEvent(ref ev);
        return true;
    }

    private void Retake(EntityUid grid)
    {
        RemComp<WGCrewSeizedHullComponent>(grid);
        _pending.Remove(grid);
        _escorts.Invalidate();
        Log.Info($"Encounter ship {ToPrettyString(grid)} was taken back by its crew.");
    }

    /// <summary>Whether a ship's change of hands has been due for the whole delay, counting this pass.</summary>
    private bool Due(EntityUid grid)
    {
        _due.Add(grid);
        var now = _timing.CurTime;
        if (_pending.TryGetValue(grid, out var since))
            return now - since >= SeizeDelay;

        _pending[grid] = now;
        return false;
    }

    /// <summary>The living, free players aboard each encounter ship who are not of its crew or its company.</summary>
    private void FindStrangers()
    {
        _strangers.Clear();
        var players = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (players.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid is not { } grid || !HasComp<WGEncounterGridComponent>(grid) || HasComp<GhostComponent>(uid)
                || _mobs.IsDead(uid) || CompOrNull<CuffableComponent>(uid)?.CuffedHandCount > 0
                || TryComp<WGCrewComponent>(uid, out var crew) && crew.Group == State(grid)?.Group
                || WGCompanyFamily.Same(Company(grid), Company(uid)))
                continue;

            if (!_strangers.TryGetValue(grid, out var aboard))
                _strangers[grid] = aboard = new List<EntityUid>();
            aboard.Add(uid);
        }
    }

    /// <summary>The living NPC crew aboard their own ship, for each ship with strangers aboard or already seized.</summary>
    private void FindCrews()
    {
        _crews.Clear();
        foreach (var grid in _strangers.Keys)
        {
            _crews[grid] = new List<EntityUid>();
        }

        foreach (var grid in _held)
        {
            _crews.TryAdd(grid, new List<EntityUid>());
        }

        var crews = EntityQueryEnumerator<WGCrewComponent, TransformComponent>();
        while (crews.MoveNext(out var uid, out var crew, out var xform))
        {
            if (xform.GridUid is not { } grid || !_crews.TryGetValue(grid, out var aboard) || HasComp<ActorComponent>(uid)
                || !_mobs.IsAlive(uid) || _crew.HomeGrid(uid, crew) != grid
                || State(grid) is { } state && crew.Group != state.Group)
                continue;

            aboard.Add(uid);
        }
    }

    /// <summary>The stranger flying a ship from one of its helms, if any.</summary>
    private EntityUid? Helmsman(EntityUid grid, List<EntityUid> strangers)
    {
        foreach (var uid in strangers)
        {
            if (TryComp<PilotComponent>(uid, out var pilot) && pilot.Console is { } console && Transform(console).GridUid == grid)
                return uid;
        }

        return null;
    }

    /// <summary>Whether a ship's crew has turned on someone aboard: a friend given the helm is no boarder.</summary>
    private bool Fought(EntityUid grid, List<EntityUid> crew, EntityUid visitor)
    {
        if (State(grid) is { } state && _alerts.IsSharedHostile(grid, state.Group, visitor))
            return true;

        foreach (var member in crew)
        {
            if (_security.IsHostileVisitor(member, visitor))
                return true;
        }

        return false;
    }

    /// <summary>What a ship's encounter knows of it, while the encounter is still there.</summary>
    private WGEncounterShipState? State(EntityUid grid)
    {
        return TryComp<WGEncounterGridComponent>(grid, out var marker) && TryComp<WGEncounterComponent>(marker.Encounter, out var encounter)
            && encounter.Ships.TryGetValue(marker.Key, out var ship)
            ? ship
            : null;
    }

    private string? Company(EntityUid uid)
    {
        return CompOrNull<CompanyComponent>(uid)?.CompanyName.Id;
    }
}
