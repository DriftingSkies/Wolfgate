using Content.Server._WG.NpcCrew.Components;
using Content.Server.Construction;
using Content.Server.Construction.Components;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared._Mono.Company;
using Content.Shared._WG.NpcCrew;
using Content.Shared.Construction.Components;
using Content.Shared.GameTicking;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Systems;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WG.NpcCrew.Systems;

/// <summary>
/// Turns a whole crew on whoever downs one of them or tampers with their ship. Every crewman aboard who may fight,
/// pilot and gunners included, leaves his station for it.
/// </summary>
public sealed partial class WGCrewRallySystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private NpcFactionSystem _factions = default!;
    [Dependency] private NPCRetaliationSystem _retaliation = default!;
    [Dependency] private HTNSystem _htn = default!;
    [Dependency] private WGCrewSystem _crew = default!;
    [Dependency] private WGCrewAlertSystem _alerts = default!;
    [Dependency] private WGCrewWeaponSystem _weapons = default!;

    /// <summary>How recently a crewman must have been hit for his attacker to be taken for the one who downed him.</summary>
    private static readonly TimeSpan StruckWindow = TimeSpan.FromSeconds(10);

    /// <summary>How long one person tampering with one ship counts as one act.</summary>
    private static readonly TimeSpan TamperSpacing = TimeSpan.FromSeconds(2);

    private readonly Dictionary<(EntityUid Grid, EntityUid Culprit), TimeSpan> _tampered = new();
    private readonly List<EntityUid> _hostiles = new();
    private readonly List<string> _groups = new();
    private readonly List<(EntityUid Grid, EntityUid Culprit)> _stale = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGCrewMemberDownEvent>(OnCrewDown);
        SubscribeLocalEvent<ConstructionComponent, ConstructionToolUseStartedEvent>(OnToolUse);
        SubscribeLocalEvent<AnchorableComponent, UnanchorAttemptEvent>(OnUnanchorAttempt);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => _tampered.Clear());
    }

    private void OnCrewDown(ref WGCrewMemberDownEvent args)
    {
        if (!TryComp<WGCrewComponent>(args.Mob, out var crew) || HasComp<ActorComponent>(args.Mob)
            || _crew.HomeGrid(args.Mob, crew) is not { } grid)
            return;

        EntityUid? culprit = args.Origin is { } origin && !TerminatingOrDeleted(origin) ? _crew.Wielder(origin) : null;
        // A body the consciousness system drives goes down with no origin: the last to hit him did it.
        culprit ??= LastStriker(crew);
        if (culprit is { } who && (who == args.Mob || !HasComp<MobStateComponent>(who) || _crew.SameCrew(args.Mob, who)))
            culprit = null;

        Rally(grid, crew.Group, culprit);
    }

    private void OnToolUse(EntityUid uid, ConstructionComponent component, ConstructionToolUseStartedEvent args)
    {
        Tampered(uid, args.User);
    }

    private void OnUnanchorAttempt(EntityUid uid, AnchorableComponent component, UnanchorAttemptEvent args)
    {
        if (!args.Cancelled)
            Tampered(uid, args.User);
    }

    /// <summary>
    /// A stranger aboard an NPC ship damaging, cutting into or unbolting part of it: its crew turns on him. Not his
    /// own crew or company, and not someone the ship lets off.
    /// </summary>
    public void Tampered(EntityUid target, EntityUid culprit)
    {
        if (!HasComp<ActorComponent>(culprit) || HasComp<MobStateComponent>(target)
            || !TryComp(target, out TransformComponent? xform) || !xform.Anchored || xform.GridUid is not { } grid
            || Transform(culprit).GridUid != grid)
            return;

        var now = _timing.CurTime;
        var key = (grid, culprit);
        if (_tampered.TryGetValue(key, out var until) && now < until)
            return;

        if (WGCompanyFamily.Same(CompOrNull<CompanyComponent>(grid)?.CompanyName.Id, CompOrNull<CompanyComponent>(culprit)?.CompanyName.Id))
            return;

        // Every crew aboard is asked: one that counts him its own or lets him off speaks for the ship.
        _groups.Clear();
        var query = EntityQueryEnumerator<WGCrewComponent>();
        while (query.MoveNext(out var uid, out var crew))
        {
            if (HasComp<ActorComponent>(uid) || !_mobs.IsAlive(uid) || _crew.HomeGrid(uid, crew) != grid)
                continue;

            if (_crew.SameCrew(uid, culprit) || _crew.Forgives(uid, culprit))
                return;

            if (!_groups.Contains(crew.Group))
                _groups.Add(crew.Group);
        }

        if (_groups.Count == 0)
            return;

        Prune(now);
        _tampered[key] = now + TamperSpacing;
        foreach (var group in _groups)
            Rally(grid, group, culprit);

        var ev = new WGCrewShipTamperedEvent(grid, culprit);
        RaiseLocalEvent(ref ev);
    }

    /// <summary>
    /// Turns every crewman of a ship who may fight on the culprit, and those the shared alert does not arm, such as the
    /// pilot and gunners, on the hostiles it knows aboard as well.
    /// </summary>
    public void Rally(EntityUid grid, string group, EntityUid? culprit)
    {
        _hostiles.Clear();
        EntityUid? named = culprit is { } who && !TerminatingOrDeleted(who) && _mobs.IsAlive(who) ? who : null;
        foreach (var hostile in _alerts.GetSharedHostiles(grid, group))
        {
            if (hostile != named)
                _hostiles.Add(hostile);
        }

        if (named == null && _hostiles.Count == 0)
            return;

        var query = EntityQueryEnumerator<WGCrewComponent, HTNComponent>();
        while (query.MoveNext(out var uid, out var crew, out var htn))
        {
            if (crew.Group != group || crew.Engagement == WGCrewEngagement.Never || HasComp<ActorComponent>(uid)
                || !_mobs.IsAlive(uid) || _crew.HomeGrid(uid, crew) != grid)
                continue;

            if (named is { } target)
                Turn(uid, target);

            // The alert already arms on-sight crew with its hostiles, and takes them back when it ends. Station crew
            // engage only an attacker they remember, so they are given the memory.
            if (!crew.ShareAlerts || crew.Engagement != WGCrewEngagement.OnSight || _weapons.IsStationCrew(uid))
            {
                foreach (var hostile in _hostiles)
                    Turn(uid, hostile);
            }

            _weapons.ForgetThreat(uid);
            _htn.Replan(htn);
        }
    }

    /// <summary>Has a crewman remember a hostile as his own attacker.</summary>
    private void Turn(EntityUid uid, EntityUid hostile)
    {
        if (hostile == uid)
            return;

        if (TryComp<NPCRetaliationComponent>(uid, out var retaliation))
        {
            // Upstream never remembers a friendly-faction attacker; the crew still answers him.
            if (!_retaliation.TryRetaliate((uid, retaliation), hostile) && HasComp<MobStateComponent>(hostile)
                && !_crew.SameCrew(uid, hostile) && !_crew.Forgives(uid, hostile))
                _crew.Remember(uid, retaliation, hostile);
        }
        else if (!_factions.IsEntityFriendly(uid, hostile))
            _factions.AggroEntity(uid, hostile);
    }

    /// <summary>Whoever hit a crewman last, within <see cref="StruckWindow"/>.</summary>
    private EntityUid? LastStriker(WGCrewComponent crew)
    {
        EntityUid? last = null;
        var when = _timing.CurTime - StruckWindow;
        foreach (var (attacker, at) in crew.Struck)
        {
            if (at < when || TerminatingOrDeleted(attacker))
                continue;

            when = at;
            last = attacker;
        }

        return last;
    }

    private void Prune(TimeSpan now)
    {
        if (_tampered.Count < 64)
            return;

        _stale.Clear();
        foreach (var (key, until) in _tampered)
        {
            if (now >= until)
                _stale.Add(key);
        }

        foreach (var key in _stale)
            _tampered.Remove(key);
    }
}
