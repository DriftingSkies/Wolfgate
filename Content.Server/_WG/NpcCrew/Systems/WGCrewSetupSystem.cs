using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Content.Server._WG.Administration.Systems;
using Content.Server._WG.NpcCrew.Components;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Shared._Mono.Company;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WG.NpcCrew;
using Content.Shared.Administration;
using Content.Shared.Database;
using Content.Shared.Dataset;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Prototypes;
using Content.Shared.NPC.Systems;
using Content.Shared.Radio;
using Content.Shared.Roles;
using Content.Shared.Verbs;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._WG.NpcCrew.Systems;

/// <summary>Validates crew plans and applies missions for both admin tools and encounter callers.</summary>
public sealed partial class WGCrewSetupSystem : EntitySystem
{
    [Dependency] private IAdminManager _admins = default!;
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private WGCrewAccessSystem _crewAccess = default!;
    [Dependency] private WGCrewSystem _crew = default!;
    [Dependency] private WGCrewPlannerSystem _planner = default!;
    [Dependency] private WGCrewObjectiveSystem _objectives = default!;
    [Dependency] private WGCrewWorkSystem _work = default!;
    [Dependency] private WGPilotDutySystem _pilots = default!;
    [Dependency] private WGRadioOperatorSystem _radio = default!;
    [Dependency] private NpcFactionSystem _factions = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private AdminVesselSpawnSystem _vessels = default!;
    [Dependency] private Robust.Shared.Random.IRobustRandom _random = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WGCrewSetupRequest>(OnRequest);
        SubscribeLocalEvent<WGCrewComponent, GetVerbsEvent<Verb>>(OnVerbs);
    }

    private void OnVerbs(EntityUid uid, WGCrewComponent crew, GetVerbsEvent<Verb> args)
    {
        if (!TryComp<ActorComponent>(args.User, out var actor)
            || !_admins.HasAdminFlag(actor.PlayerSession, AdminFlags.Spawn) || CrewGrid(uid, crew) is not { } grid)
            return;
        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("wg-crew-setup-title"), Category = VerbCategory.Admin,
            Act = () =>
            {
                if (_admins.HasAdminFlag(actor.PlayerSession, AdminFlags.Spawn) && !TerminatingOrDeleted(grid))
                    RaiseNetworkEvent(new WGCrewSetupOpenEvent(GetNetEntity(grid), crew.Group), Filter.SinglePlayer(actor.PlayerSession));
            },
        });
    }

    /// <summary>The ship a crewman belongs to: his current job's ship, else his post's grid, else the grid he stands on.</summary>
    public EntityUid? CrewGrid(EntityUid uid, WGCrewComponent crew, TransformComponent? xform = null)
    {
        if (_work.HomeGrid(uid) is { } home)
            return home;
        if (crew.Post is { } post && post.EntityId.IsValid() && HasComp<MapGridComponent>(post.EntityId))
            return post.EntityId;
        return (xform ?? Transform(uid)).GridUid;
    }

    /// <summary>Every crewman of a crew, wherever he currently stands.</summary>
    private List<EntityUid> Members(EntityUid grid, string group)
    {
        var members = new List<EntityUid>();
        var query = EntityQueryEnumerator<WGCrewComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var crew, out var xform))
        {
            if (crew.Group == group && CrewGrid(uid, crew, xform) == grid)
                members.Add(uid);
        }
        return members;
    }

    /// <summary>
    /// Cancels a crew's queue and work and deletes its crewmen. Returns how many were removed. With
    /// <paramref name="keepCorpses"/>, the dead lying on some other grid are left there as ordinary bodies.
    /// </summary>
    public int ClearCrew(EntityUid grid, string group, bool keepCorpses = false)
    {
        var members = Members(grid, group);
        _objectives.Cancel(grid, group);
        var removed = 0;
        foreach (var member in members)
        {
            if (keepCorpses && LeaveCorpse(member, grid))
                continue;
            QueueDel(member);
            removed++;
        }
        return removed;
    }

    /// <summary>
    /// A dead crewman somebody has carried off his ship is theirs to keep: he stops being crew and stays where he lies.
    /// </summary>
    private bool LeaveCorpse(EntityUid uid, EntityUid? ship)
    {
        if (!_mobs.IsDead(uid) || Transform(uid).GridUid is not { } lying || lying == ship)
            return false;

        RemComp<WGCrewComponent>(uid);
        return true;
    }

    /// <summary>Deletes a group on every ship and cancels its queues and work. Returns how many crewmen were removed.</summary>
    public int ClearGroup(string group, bool keepCorpses = false)
    {
        var members = new List<EntityUid>();
        var ships = new HashSet<EntityUid>();
        var query = EntityQueryEnumerator<WGCrewComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var crew, out var xform))
        {
            if (crew.Group != group)
                continue;
            members.Add(uid);
            if (CrewGrid(uid, crew, xform) is { } ship)
                ships.Add(ship);
        }
        foreach (var ship in ships)
            _objectives.Cancel(ship, group);
        var removed = 0;
        foreach (var member in members)
        {
            if (keepCorpses && LeaveCorpse(member, null))
                continue;
            QueueDel(member);
            removed++;
        }
        return removed;
    }

    /// <summary>Whether a grid is too large to scan for posts without stalling the server.</summary>
    public bool IsTooLarge(EntityUid grid)
    {
        if (!TryComp<MapGridComponent>(grid, out var map))
            return false;
        var box = map.LocalAABB;
        return box.Width * box.Height > WGCrewLimits.MaxPlanArea;
    }

    /// <summary>Builds the same roster used by commands, optionally adding a captain to a spare deck post.</summary>
    public List<WGCrewSetupPost> Plan(EntityUid grid, int deckhands, bool captain)
    {
        return Plan(grid, deckhands, captain, out _);
    }

    /// <summary>Plans a roster capped at the window's row limit; total is the uncapped post count.</summary>
    public List<WGCrewSetupPost> Plan(EntityUid grid, int deckhands, bool captain, out int total)
    {
        var posts = _planner.Plan(grid, Math.Clamp(deckhands, 0, 32) + (captain ? 1 : 0));
        if (captain && posts.All(post => post.Role != WGCrewRoles.Captain))
        {
            var index = posts.FindLastIndex(post => post.Kind == WGCrewPostKind.Deck);
            if (index >= 0)
                posts[index] = posts[index] with { Role = WGCrewRoles.Captain };
        }
        total = posts.Count;
        return posts.Take(WGCrewLimits.MaxListItems)
            .Select(post => new WGCrewSetupPost { Role = post.Role.Id, Position = post.Coordinates.Position }).ToList();
    }

    /// <summary>Checks an entire plan before creating anything, including posts, prototypes and mission targets.</summary>
    public bool Validate(EntityUid grid, List<WGCrewSetupPost> posts, WGCrewMission mission)
    {
        if (!ValidateMission(grid, mission) || !TryComp<MapGridComponent>(grid, out var map)
            || posts is null || posts.Count is < 1 or > WGCrewLimits.MaxListItems)
            return false;
        foreach (var post in posts)
        {
            if (post is null || !Bounded(post.Position) || post.Role is null || post.Loadout is null
                || !_prototypes.HasIndex<WGCrewRolePrototype>(post.Role)
                || post.Engagement is { } engagement && !Enum.IsDefined(engagement)
                || post.Loadout.Length > 0 && !_prototypes.HasIndex<StartingGearPrototype>(post.Loadout))
                return false;
            var coordinates = new EntityCoordinates(grid, post.Position);
            var tile = _maps.TileIndicesFor(grid, map, coordinates);
            // A post without air is allowed; the crewman put there is spawned in a void kit.
            if (!_planner.IsStandable(grid, tile, map))
                return false;
        }
        return true;
    }

    private bool ValidateMission(EntityUid grid, WGCrewMission mission)
    {
        if (mission is null || !HasComp<MapGridComponent>(grid) || Transform(grid).MapUid == null || Transform(grid).MapID == MapId.Nullspace
            || !Fits(mission.Group, WGCrewLimits.MaxGroup) || mission.Group.Length == 0
            || !Fits(mission.Callsign, WGCrewLimits.MaxCallsign) || !Fits(mission.Battlegroup, WGCrewLimits.MaxBattlegroup)
            || !Enum.IsDefined(mission.Order)
            || !Enum.IsDefined(mission.BoardingResponse) || !Enum.IsDefined(mission.DockingResponse)
            || !Enum.IsDefined(mission.Disengage) || !Enum.IsDefined(mission.Skill)
            || !string.IsNullOrEmpty(mission.Profile) && !Known<WGCrewProfilePrototype>(mission.Profile)
            || !float.IsFinite(mission.DisengageRange) || mission.DisengageRange is < 50 or > WGCrewLimits.MaxRange
            || mission.Navigation == null || !mission.Navigation.IsValid()
            || !float.IsFinite(mission.Range) || mission.Range is < 1 or > WGCrewLimits.MaxRange
            || !Bounded(mission.Destination) || !Known<NpcFactionPrototype>(mission.Faction)
            || !Known<RadioChannelPrototype>(mission.LocalChannel)
            || !Known<RadioChannelPrototype>(mission.AlertChannel)
            || mission.Company is null || mission.Company.Length > 0 && !Known<CompanyPrototype>(mission.Company))
            return false;
        if (mission.Order is WGPilotOrder.Follow or WGPilotOrder.Dock
            && (mission.Target is not { } target || !TryGetEntity(target, out var other) || other == grid
                || !HasComp<MapGridComponent>(other) || Transform(other.Value).MapID != Transform(grid).MapID))
            return false;
        return true;
    }

    private bool Known<T>(string? id) where T : class, IPrototype => id != null && _prototypes.HasIndex<T>(id);

    private static bool Fits(string? text, int max) => text != null && text.Length <= max;

    private static bool Bounded(Vector2 point) => float.IsFinite(point.X) && float.IsFinite(point.Y)
        && MathF.Abs(point.X) <= WGCrewLimits.MaxCoordinate && MathF.Abs(point.Y) <= WGCrewLimits.MaxCoordinate;

    /// <summary>Encounter entry point: spawns the validated roster through the ordinary crew factory.</summary>
    public bool TrySpawn(EntityUid grid, List<WGCrewSetupPost> posts, WGCrewMission mission, out List<EntityUid> spawned)
    {
        return TrySpawn(grid, posts, mission, true, out spawned);
    }

    /// <summary>
    /// As the other overload. With <paramref name="profileSkills"/> off, the mission's skill stays as set and the
    /// profile's skill pool is not drawn from, so a ship's own skill setting is not overridden.
    /// </summary>
    public bool TrySpawn(EntityUid grid, List<WGCrewSetupPost> posts, WGCrewMission mission, bool profileSkills,
        out List<EntityUid> spawned)
    {
        spawned = new List<EntityUid>();
        if (!Validate(grid, posts, mission))
            return false;
        _objectives.Cancel(grid, mission.Group);
        ApplyCompany(grid, mission.Company);
        _factions.ClearFactions(grid);
        _factions.AddFaction(grid, mission.Faction);
        // A profile rolls each crewman's body and loadout, and one skill for the crew.
        _prototypes.TryIndex<WGCrewProfilePrototype>(mission.Profile ?? string.Empty, out var profile);
        if (profileSkills && profile != null && profile.Skills.Count > 0)
            mission.Skill = _random.Pick(profile.Skills);
        foreach (var post in posts)
        {
            var loadout = post.Loadout.Length > 0 ? new ProtoId<StartingGearPrototype>(post.Loadout) : (ProtoId<StartingGearPrototype>?) null;
            if (loadout == null && profile != null && profile.Loadouts.TryGetValue(post.Role, out var pool) && pool.Count > 0)
                loadout = _random.Pick(pool);
            // A role the profile has no loadout for keeps its own mob and gear, so nobody is spawned naked.
            var body = loadout != null && profile != null && profile.Bodies.Count > 0 ? _random.Pick(profile.Bodies) : (EntProtoId?) null;
            var uid = _crew.SpawnCrewman(post.Role, new EntityCoordinates(grid, post.Position), mission.Group, loadout, body,
                profile != null ? FlavourName(profile) : null, profile?.ID);
            if (uid is not { } mob)
                continue;
            if (profile != null && profile.Components.Count > 0)
                EntityManager.AddComponents(mob, profile.Components);
            if (loadout is { } gear && profile != null && profile.SkillOffsets.TryGetValue(gear, out var offset)
                && TryComp<WGCrewComponent>(mob, out var member))
                member.SkillOffset = offset;
            if (post.Engagement is { } engagement)
                _crew.SetEngagement(mob, engagement);
            else if (profile != null && profile.Engagement.TryGetValue(post.Role, out var manner))
                _crew.SetEngagement(mob, manner);
            // A coward never fights, even when struck: he takes shelter instead.
            if (post.Engagement == null && profile != null && profile.Cowards.TryGetValue(post.Role, out var chance)
                && _random.Prob(Math.Clamp(chance, 0f, 1f)))
                _crew.SetEngagement(mob, WGCrewEngagement.Never);
            ApplyMission(mob, grid, mission);
            spawned.Add(mob);
        }
        return spawned.Count == posts.Count;
    }

    /// <summary>A name rolled from a profile's datasets, or null for a profile that leaves crews the names their bodies roll.</summary>
    private string? FlavourName(WGCrewProfilePrototype profile)
    {
        if (profile.FirstNames is not { } firstId || profile.LastNames is not { } lastId
            || !_prototypes.TryIndex(firstId, out var first) || !_prototypes.TryIndex(lastId, out var last)
            || first.Values.Count == 0 || last.Values.Count == 0)
            return null;

        return $"{_random.Pick(first.Values)} {_random.Pick(last.Values)}";
    }

    /// <summary>Sets the company, or removes it when the company is empty.</summary>
    private void ApplyCompany(EntityUid uid, string company)
    {
        if (company.Length == 0)
        {
            RemComp<CompanyComponent>(uid);
            return;
        }
        var component = EnsureComp<CompanyComponent>(uid);
        component.CompanyName = company;
        Dirty(uid, component);
    }

    /// <summary>Updates an existing crew without requiring a new spawn roster.</summary>
    /// <summary>Records on a grid the profile its crew flies under, for the rules a profile sets between its own ships.</summary>
    public void MarkProfile(EntityUid grid, ProtoId<WGCrewProfilePrototype>? profileId)
    {
        if (profileId is { } id && _prototypes.HasIndex(id))
            EnsureComp<WGCrewShipProfileComponent>(grid).Profile = id;
    }

    /// <summary>Hull parts waiting to be swapped, worked off a few each tick so crewing a ship never costs one long tick.</summary>
    private readonly Queue<(EntityUid Old, EntProtoId New)> _hullSwaps = new();

    /// <summary>How long one tick may spend swapping hull parts.</summary>
    private static readonly TimeSpan HullSwapBudget = TimeSpan.FromMilliseconds(2);

    /// <summary>
    /// Queues the hull parts a profile swaps out on a grid, to be replaced in place and anchored as they were over the
    /// next ticks. Returns how many.
    /// </summary>
    public int ApplyHullSwaps(EntityUid grid, ProtoId<WGCrewProfilePrototype>? profileId)
    {
        if (profileId is not { } id || !_prototypes.TryIndex(id, out var profile) || profile.HullSwaps.Count == 0)
            return 0;

        // Hull parts are anchored, so they are the grid's own children.
        var count = 0;
        var children = Transform(grid).ChildEnumerator;
        while (children.MoveNext(out var uid))
        {
            if (MetaData(uid).EntityPrototype is not { } proto || !profile.HullSwaps.TryGetValue(proto.ID, out var replacement))
                continue;
            _hullSwaps.Enqueue((uid, replacement));
            count++;
        }

        return count;
    }

    /// <summary>Whether any hull part is still waiting to be swapped.</summary>
    public bool HullSwapsPending => _hullSwaps.Count > 0;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_hullSwaps.Count == 0)
            return;

        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < HullSwapBudget && _hullSwaps.TryDequeue(out var swap))
        {
            if (TerminatingOrDeleted(swap.Old))
                continue;
            var xform = Transform(swap.Old);
            var rotation = xform.LocalRotation;
            var spawned = Spawn(swap.New, xform.Coordinates);
            QueueDel(swap.Old);
            _transform.SetLocalRotation(spawned, rotation);
            if (!Transform(spawned).Anchored)
                _transform.AnchorEntity(spawned);
        }
    }

    public bool TryApplyMission(EntityUid grid, WGCrewMission mission)
    {
        if (!ValidateMission(grid, mission))
            return false;
        _objectives.Cancel(grid, mission.Group);
        ApplyCompany(grid, mission.Company);
        _factions.ClearFactions(grid);
        _factions.AddFaction(grid, mission.Faction);
        foreach (var uid in Members(grid, mission.Group))
            ApplyMission(uid, grid, mission);
        return true;
    }

    /// <summary>Reissues a validated mission without changing membership or issuing new credentials.</summary>
    private void ApplySettings(EntityUid mob, WGCrewMission mission)
    {
        if (TryComp<WGCrewComponent>(mob, out var crew))
        {
            crew.Navigation = mission.Navigation.Clone();
            crew.Battlegroup = mission.Battlegroup.Trim();
            crew.Disengage = mission.Disengage;
            crew.Skill = mission.Skill;
            crew.DisengageRange = mission.DisengageRange;
            crew.CallsForHelp = mission.CallsForHelp;
            if (_crew.HomeGrid(mob, crew) is { } home)
                EntityManager.System<WGCrewShipStatusSystem>().SetPolicy(home, mission.Disengage, mission.DisengageRange);
            EntityManager.System<WGCrewEscortSystem>().Invalidate();
        }
        if (TryComp<WGPilotDutyComponent>(mob, out var pilot))
        {
            pilot.ReactToAttacks = mission.HeaveTo;
            pilot.FleeAttackers = mission.FleeAttackers;
            pilot.FaceThreats = mission.FaceThreats;
            _pilots.SetNavigation(mob, mission.Navigation);
        }
        EntityManager.System<WGCrewSecuritySystem>().Reset(mob);
        var security = EnsureComp<WGCrewSecurityComponent>(mob);
        security.Boarding = mission.BoardingResponse;
        security.Docking = mission.DockingResponse;
        ApplyCompany(mob, mission.Company);
        _factions.ClearFactions(mob);
        _factions.AddFaction(mob, mission.Faction);
        if (TryComp<WGCaptainComponent>(mob, out var captain))
            captain.HeaveTo = mission.HeaveTo;
        if (TryComp<WGRadioOperatorComponent>(mob, out var radio))
        {
            _radio.SetCallsign((mob, radio), mission.Callsign);
            radio.LocalChannel = mission.LocalChannel;
            radio.AlertChannel = mission.AlertChannel;
            // Raiders under a borrowed flag don't get on that company's channel.
            radio.FactionChannel = mission.Faction == "PirateNF" ? null : WGCrewCommsSystem.FactionChannel(mission.Company);
            radio.CallsForHelp = mission.CallsForHelp;
        }
    }

    /// <summary>Updates crew settings and issues the selected immediate flight order.</summary>
    public void ApplyMission(EntityUid mob, EntityUid grid, WGCrewMission mission)
    {
        ApplySettings(mob, mission);
        if (!TryComp<WGCrewComponent>(mob, out var crew) || crew.Duty != WGCrewDuties.Pilot)
            return;
        var destination = new EntityCoordinates(Transform(grid).MapUid!.Value, mission.Destination);
        switch (mission.Order)
        {
            case WGPilotOrder.Hold: _pilots.Hold(mob); break;
            case WGPilotOrder.GoTo: _pilots.GoTo(mob, new List<EntityCoordinates> { destination }); break;
            case WGPilotOrder.Loiter: _pilots.Loiter(mob, destination, mission.Range); break;
            case WGPilotOrder.Follow: _pilots.Follow(mob, GetEntity(mission.Target!.Value), mission.Range); break;
            case WGPilotOrder.Dock: _pilots.Dock(mob, GetEntity(mission.Target!.Value)); break;
            case WGPilotOrder.Undock: _pilots.Undock(mob); break;
        }
    }

    /// <summary>
    /// Fills null fields, trims text and rejects oversize input. Returns the Fluent key of the reason, or null if the request is usable.
    /// </summary>
    private static string? Sanitize(WGCrewSetupRequest request)
    {
        request.Vessel = request.Vessel?.Trim() ?? string.Empty;
        request.Posts ??= new List<WGCrewSetupPost>();
        request.Objectives ??= new List<WGCrewObjective>();
        request.Mission ??= new WGCrewMission();
        var mission = request.Mission;
        mission.Group = mission.Group?.Trim() ?? string.Empty;
        mission.Callsign = mission.Callsign?.Trim() ?? string.Empty;
        mission.Battlegroup = mission.Battlegroup?.Trim() ?? string.Empty;
        mission.Company ??= string.Empty;
        mission.Faction ??= string.Empty;
        mission.LocalChannel ??= string.Empty;
        mission.AlertChannel ??= string.Empty;
        if (mission.Group.Length > WGCrewLimits.MaxGroup)
            return "wg-crew-setup-group-too-long";
        if (mission.Callsign.Length > WGCrewLimits.MaxCallsign)
            return "wg-crew-setup-callsign-too-long";
        if (mission.Battlegroup.Length > WGCrewLimits.MaxBattlegroup)
            return "wg-crew-setup-battlegroup-too-long";
        if (request.Posts.Count > WGCrewLimits.MaxListItems)
            return "wg-crew-setup-too-many-posts";
        if (request.Objectives.Count > WGCrewLimits.MaxListItems)
            return "wg-crew-setup-too-many-objectives";
        foreach (var post in request.Posts)
        {
            if (post is null)
                return "wg-crew-setup-invalid";
            post.Role = post.Role?.Trim() ?? string.Empty;
            post.Loadout = post.Loadout?.Trim() ?? string.Empty;
        }
        foreach (var objective in request.Objectives)
        {
            if (objective is null || !Bounded(objective.Position))
                return "wg-crew-setup-bad-coordinates";
        }
        if (request.Action is WGCrewSetupAction.Clear or WGCrewSetupAction.Objectives or WGCrewSetupAction.AppendObjective
                or WGCrewSetupAction.Pause or WGCrewSetupAction.Resume or WGCrewSetupAction.Skip or WGCrewSetupAction.Rules
                or WGCrewSetupAction.Orders or WGCrewSetupAction.Spawn or WGCrewSetupAction.Preview
            && mission.Group.Length == 0)
            return "wg-crew-setup-group-required";
        return null;
    }

    private void OnRequest(WGCrewSetupRequest request, EntitySessionEventArgs args)
    {
        if (Handle(request, args.SenderSession) is not { } response)
            return;
        RaiseNetworkEvent(response, Filter.SinglePlayer(args.SenderSession));
        EntityManager.System<WGCrewUiDiagnosticsSystem>().Reply("crew", args.SenderSession, response.Crews.Count, response.Grid);
    }

    /// <summary>Sanitises and runs one setup request. Returns the reply, or null when the caller lacks permission.</summary>
    public WGCrewSetupResponse? Handle(WGCrewSetupRequest request, ICommonSession session)
    {
        if (!_admins.HasAdminFlag(session, AdminFlags.Spawn))
            return null;
        var response = new WGCrewSetupResponse { RequestId = request.RequestId, Action = request.Action, Grid = request.Grid };
        var refreshed = false;
        if (!Enum.IsDefined(request.Action))
            response.Message = Loc.GetString("wg-crew-setup-invalid");
        else if (Sanitize(request) is { } rejection)
            response.Message = Loc.GetString(rejection);
        else
            refreshed = Run(request, session, response);
        if (!refreshed)
            response.Crews = _objectives.Snapshot();
        return response;
    }

    /// <summary>Runs a sanitised request into the response. Returns true when the response already carries a fresh crew snapshot.</summary>
    private bool Run(WGCrewSetupRequest request, ICommonSession session, WGCrewSetupResponse response)
    {
        var action = request.Action;
        var group = request.Mission.Group;
        if (action == WGCrewSetupAction.Crews)
            return false;
        if (action == WGCrewSetupAction.List)
        {
            var query = EntityQueryEnumerator<MapGridComponent, MetaDataComponent>();
            while (query.MoveNext(out var uid, out var map, out var meta))
            {
                var box = map.LocalAABB;
                response.Grids.Add(new WGCrewSetupGrid(GetNetEntity(uid), meta.EntityName, box.Width * box.Height > WGCrewLimits.MaxPlanArea));
            }
            var current = session.AttachedEntity is { } player ? Transform(player).GridUid : null;
            response.Grids = response.Grids.OrderBy(info => GetEntity(info.Id) == current ? 0 : 1).ThenBy(info => info.Name).ToList();
            return false;
        }
        if (action == WGCrewSetupAction.SpawnVessel)
        {
            if (session.AttachedEntity is { } actor && _prototypes.TryIndex<VesselPrototype>(request.Vessel, out var vessel)
                && !vessel.Abstract && Transform(actor).MapID != MapId.Nullspace
                && _vessels.TrySpawnVessel(vessel, Transform(actor).MapID, _transform.GetWorldPosition(actor), actor, out var created))
                response.Grid = GetNetEntity(created.Value);
            else
                response.Message = Loc.GetString("wg-crew-setup-invalid");
            return false;
        }
        if (request.Grid is not { } net || !TryGetEntity(net, out var found) || found is not { } grid || !HasComp<MapGridComponent>(grid))
        {
            response.Message = Loc.GetString("wg-crew-setup-invalid");
            return false;
        }

        if (action == WGCrewSetupAction.Plan)
        {
            if (IsTooLarge(grid))
            {
                response.Message = Loc.GetString("wg-crew-setup-grid-too-large");
                return false;
            }
            response.Posts = Plan(grid, request.Deckhands, request.Captain, out var total);
            if (response.Posts.Count == 0)
                response.Message = Loc.GetString("wg-crew-setup-no-safe-posts");
            else if (total > response.Posts.Count)
                response.Message = Loc.GetString("wg-crew-setup-plan-capped", ("shown", response.Posts.Count), ("total", total));
        }
        else if (action == WGCrewSetupAction.Clear)
        {
            var count = ClearCrew(grid, group);
            if (count == 0)
                response.Message = Loc.GetString("wg-crew-setup-no-crew");
            else
                Audit(session, grid, group, $"cleared {count} crew members from");
        }
        else if (action is WGCrewSetupAction.Objectives or WGCrewSetupAction.AppendObjective)
        {
            if (!_objectives.SetQueue(grid, group, request.Objectives, action == WGCrewSetupAction.AppendObjective))
                response.Message = Loc.GetString("wg-crew-setup-invalid");
            else
                Audit(session, grid, group, $"{(action == WGCrewSetupAction.AppendObjective ? "appended" : "replaced")} {request.Objectives.Count} objectives for");
        }
        else if (action is WGCrewSetupAction.Pause or WGCrewSetupAction.Resume or WGCrewSetupAction.Skip)
        {
            var queued = _objectives.Snapshot().FirstOrDefault(row => row.Grid == net && row.Group == group)?.Objectives.Count > 0;
            if (!queued)
            {
                response.Message = Loc.GetString("wg-crew-setup-no-queue");
                return false;
            }
            _objectives.Control(grid, group, action);
            var verb = action switch
            {
                WGCrewSetupAction.Pause => "paused the objective queue of",
                WGCrewSetupAction.Resume => "resumed the objective queue of",
                _ => "skipped the current objective of",
            };
            Audit(session, grid, group, verb);
            response.Crews = _objectives.Snapshot();
            return true;
        }
        else if (action == WGCrewSetupAction.Rules)
        {
            request.Mission.Order = WGPilotOrder.Hold;
            var members = Members(grid, group);
            if (members.Count == 0)
                response.Message = Loc.GetString("wg-crew-setup-no-crew");
            else if (!ValidateMission(grid, request.Mission))
                response.Message = Loc.GetString("wg-crew-setup-invalid");
            else
            {
                ApplyCompany(grid, request.Mission.Company);
                _factions.ClearFactions(grid);
                _factions.AddFaction(grid, request.Mission.Faction);
                foreach (var member in members)
                    ApplySettings(member, request.Mission);
                Audit(session, grid, group, "changed the rules of");
            }
        }
        else if (action == WGCrewSetupAction.Orders)
        {
            var members = Members(grid, group);
            if (members.Count == 0)
                response.Message = Loc.GetString("wg-crew-setup-no-crew");
            else if (!members.Any(member => TryComp<WGCrewComponent>(member, out var crew) && crew.Duty == WGCrewDuties.Pilot && _mobs.IsAlive(member)))
                response.Message = Loc.GetString("wg-crew-setup-no-pilot");
            else if (!TryApplyMission(grid, request.Mission))
                response.Message = Loc.GetString("wg-crew-setup-invalid");
            else
                Audit(session, grid, group, $"gave {request.Mission.Order} orders to");
        }
        else if (action is WGCrewSetupAction.Spawn && IsTooLarge(grid))
            response.Message = Loc.GetString("wg-crew-setup-grid-too-large");
        else if (!Validate(grid, request.Posts, request.Mission))
            response.Message = Loc.GetString("wg-crew-setup-invalid");
        else if (action == WGCrewSetupAction.Spawn)
        {
            TrySpawn(grid, request.Posts, request.Mission, out var spawned);
            response.Message = Loc.GetString("wg-crew-setup-spawned", ("count", spawned.Count));
            Audit(session, grid, group, $"spawned {spawned.Count} crew members on");
        }
        else if (action == WGCrewSetupAction.Preview)
            response.Posts = request.Posts;
        else if (action == WGCrewSetupAction.Teleport)
        {
            if (!_admins.HasAdminFlag(session, AdminFlags.Admin))
                response.Message = Loc.GetString("wg-crew-setup-teleport-denied");
            else if (session.AttachedEntity is not { } actor)
                response.Message = Loc.GetString("wg-crew-setup-teleport-no-body");
            else
            {
                _transform.SetCoordinates(actor, new EntityCoordinates(grid, request.Posts[0].Position));
                _adminLog.Add(LogType.Teleport, LogImpact.Medium,
                    $"{session.Name} teleported to a crew post on {ToPrettyString(grid):entity} from the crew setup window");
            }
        }
        return false;
    }

    private void Audit(ICommonSession session, EntityUid grid, string group, string what)
    {
        _adminLog.Add(LogType.AdminCommands, LogImpact.Medium,
            $"{session.Name} {what} crew group {group} on {ToPrettyString(grid):entity}");
    }
}
