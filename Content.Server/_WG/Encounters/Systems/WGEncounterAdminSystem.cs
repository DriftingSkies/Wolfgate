using System.Linq;
using System.Numerics;
using Content.Server._WG.Encounters.Components;
using Content.Server._WG.NpcCrew.Systems;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Shared._WG.CCVar;
using Content.Shared._WG.Encounters;
using Content.Shared.Administration;
using Content.Shared.Database;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WG.Encounters.Systems;

/// <summary>The server side of the admin encounter window: checks permission, acts, and replies with the state.</summary>
public sealed partial class WGEncounterAdminSystem : EntitySystem
{
    [Dependency] private IAdminManager _admins = default!;
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private WGEncounterSystem _encounters = default!;
    [Dependency] private WGEncounterSchedulerSystem _scheduler = default!;
    [Dependency] private WGCrewObjectiveSystem _objectives = default!;
    [Dependency] private WGCrewShipStatusSystem _status = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private Content.Shared.Mobs.Systems.MobStateSystem _mobs = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WGEncounterAdminRequest>(OnRequest);
    }

    private void OnRequest(WGEncounterAdminRequest request, EntitySessionEventArgs args)
    {
        if (!_admins.HasAdminFlag(args.SenderSession, AdminFlags.Admin))
            return;

        var message = Handle(request, args.SenderSession) ?? string.Empty;
        var state = BuildState(args.SenderSession);
        state.Message = message;

        RaiseNetworkEvent(state, args.SenderSession);
    }

    /// <summary>Carries out one request. Returns why it did nothing, or null.</summary>
    public string? Handle(WGEncounterAdminRequest request, ICommonSession session)
    {
        switch (request.Action)
        {
            case WGEncounterAdminAction.Spawn:
                if (session.AttachedEntity is not { } actor || _transform.GetMapCoordinates(actor) is var here && here.MapId == MapId.Nullspace)
                    return Loc.GetString("cmd-wg_encounter-no-player");
                if (!_prototypes.TryIndex<WGEncounterPrototype>(request.Prototype ?? string.Empty, out var prototype))
                    return Loc.GetString("cmd-wg_encounter-unknown", ("prototype", request.Prototype ?? string.Empty));
                if (!float.IsFinite(request.Distance) || request.Distance is < 0 or > 20000)
                    return Loc.GetString("cmd-wg_encounter-bad-distance", ("arg", request.Distance));
                var origin = new MapCoordinates(here.Position + new Vector2(0f, request.Distance), here.MapId);
                var tier = request.Tier is { } asked ? Math.Clamp(asked, 0, WGEncounterThreat.MaxTier) : (int?) null;
                if (!_scheduler.TryStartAt(prototype, origin, out var spawned, actor, request.Pinned, tier))
                    return Loc.GetString("cmd-wg_encounter-spawn-failed", ("prototype", prototype.ID));
                Audit(session, $"started encounter {prototype.ID} as {ToPrettyString(spawned):entity}{(request.Pinned ? ", kept until ended" : string.Empty)}");
                return null;

            case WGEncounterAdminAction.Resolve:
            case WGEncounterAdminAction.End:
                if (!TryGetEntity(request.Target, out var target) || !HasComp<WGEncounterComponent>(target))
                    return Loc.GetString("wg-encounter-admin-gone");
                Audit(session, $"{(request.Action == WGEncounterAdminAction.End ? "ended" : "resolved")} encounter {ToPrettyString(target.Value):entity}");
                if (request.Action == WGEncounterAdminAction.End)
                    _encounters.End(target.Value);
                else
                    _encounters.Resolve(target.Value, WGEncounterResolution.Ended);
                return null;

            case WGEncounterAdminAction.Schedule:
                if (!_scheduler.TrySchedule(out var scheduled))
                    return Loc.GetString("cmd-wg_encounter-not-scheduled");
                Audit(session, $"had the scheduler start encounter {ToPrettyString(scheduled):entity}");
                return null;

            case WGEncounterAdminAction.Scheduler:
                if (!float.IsFinite(request.IntervalMin) || !float.IsFinite(request.IntervalMax)
                    || request.IntervalMin < 30 || request.IntervalMax < request.IntervalMin || request.IntervalMax > 86400
                    || request.MaxActive is < 0 or > 10)
                    return Loc.GetString("wg-encounter-admin-bad-settings");
                _config.SetCVar(EncountersCVars.Enabled, request.Enabled);
                _config.SetCVar(EncountersCVars.IntervalMin, request.IntervalMin);
                _config.SetCVar(EncountersCVars.IntervalMax, request.IntervalMax);
                _config.SetCVar(EncountersCVars.MaxActive, request.MaxActive);
                _scheduler.Paused = request.Paused;
                Audit(session, $"set the encounter scheduler: enabled {request.Enabled}, paused {request.Paused}, every {request.IntervalMin}-{request.IntervalMax} s, cap {request.MaxActive}");
                return null;

            case WGEncounterAdminAction.Preset:
                if (!EntityManager.System<WGEncounterVoteSystem>().SetPreset(request.Prototype ?? string.Empty))
                    return Loc.GetString("wg-encounter-admin-gone");
                Audit(session, $"set the encounter preset to {request.Prototype}");
                return null;

            case WGEncounterAdminAction.Reveal:
                if (!TryGetEntity(request.Target, out var hidden) || !HasComp<WGEncounterComponent>(hidden))
                    return Loc.GetString("wg-encounter-admin-gone");
                _encounters.Reveal(hidden.Value, true);
                Audit(session, $"revealed encounter {ToPrettyString(hidden.Value):entity}");
                return null;

            case WGEncounterAdminAction.StartRound:
                var count = _scheduler.StartRound();
                Audit(session, $"had the scheduler place {count} round-start encounters");
                return count > 0 ? null : Loc.GetString("cmd-wg_encounter-not-scheduled");

            case WGEncounterAdminAction.Order:
                if (!TryGetEntity(request.Target, out var ordered) || !TryComp<WGEncounterComponent>(ordered, out var orderedComp))
                    return Loc.GetString("wg-encounter-admin-gone");
                TryGetEntity(request.OrderTarget, out var aimedAt);
                MapCoordinates? where = session.AttachedEntity is { } admin ? _transform.GetMapCoordinates(admin) : null;
                if (_encounters.AdminOrder((ordered.Value, orderedComp), request.Key, request.Order, aimedAt, where) is { } refused)
                    return refused;
                Audit(session, $"ordered ship '{request.Key}' of encounter {ToPrettyString(ordered.Value):entity} to {request.Order}"
                               + (aimedAt is { } aim ? $" {ToPrettyString(aim):entity}" : string.Empty));
                return Loc.GetString("wg-encounter-admin-order-done", ("order", Loc.GetString($"wg-encounter-admin-order-{request.Order.ToString().ToLowerInvariant()}")),
                    ("key", request.Key));

            case WGEncounterAdminAction.Teleport:
                if (session.AttachedEntity is not { } body)
                    return Loc.GetString("cmd-wg_encounter-no-player");
                if (!TryGetEntity(request.Target, out var destination) || TerminatingOrDeleted(destination))
                    return Loc.GetString("wg-encounter-admin-gone");
                var point = TryComp<MapGridComponent>(destination, out var grid)
                    ? new EntityCoordinates(destination.Value, grid.LocalAABB.Center)
                    : Transform(destination.Value).Coordinates;
                _transform.SetCoordinates(body, point);
                _adminLog.Add(LogType.Teleport, LogImpact.Medium,
                    $"{session.Name} teleported to {ToPrettyString(destination.Value):entity} from the encounter window");
                return null;
        }

        return null;
    }

    private void Audit(ICommonSession session, string what)
    {
        _adminLog.Add(LogType.AdminCommands, LogImpact.Medium, $"{session.Name} {what}");
    }

    /// <summary>The scheduler settings, every prototype, every encounter with its ships, and the player ships an order can be aimed at, nearest the viewer first.</summary>
    public WGEncounterAdminState BuildState(ICommonSession? viewer = null)
    {
        var state = new WGEncounterAdminState
        {
            Enabled = _config.GetCVar(EncountersCVars.Enabled),
            Paused = _scheduler.Paused,
            IntervalMin = _config.GetCVar(EncountersCVars.IntervalMin),
            IntervalMax = _config.GetCVar(EncountersCVars.IntervalMax),
            MaxActive = _config.GetCVar(EncountersCVars.MaxActive),
            NextIn = _scheduler.Next is { } next ? MathF.Max(0f, (float) (next - _timing.CurTime).TotalSeconds) : -1f,
            Preset = _scheduler.Preset?.ID ?? string.Empty,
            Budget = _scheduler.Budget(),
            Cost = _encounters.ActiveCost(),
        };
        foreach (var preset in _prototypes.EnumeratePrototypes<WGEncounterPresetPrototype>().OrderBy(preset => preset.Budget))
        {
            state.Presets.Add(new WGEncounterAdminPreset { Id = preset.ID, Name = Loc.GetString(preset.Name) });
        }

        var crews = _objectives.Snapshot();
        var running = new HashSet<string>();
        var query = EntityQueryEnumerator<WGEncounterComponent>();
        while (query.MoveNext(out var uid, out var encounter))
        {
            if (encounter.Resolution == null)
                running.Add(encounter.Prototype.Id);

            var entry = new WGEncounterAdminEntry
            {
                Uid = GetNetEntity(uid),
                Prototype = encounter.Prototype.Id,
                Name = encounter.Name,
                Resolved = encounter.Resolution != null,
                Hidden = encounter.Hidden,
                Pinned = encounter.Pinned,
                State = _encounters.StateDetail(uid, encounter, Loc.GetString(encounter.Resolution is { } resolution
                    ? $"wg-encounter-resolution-{resolution.ToString().ToLowerInvariant()}"
                    : "cmd-wg_encounter-state-active"), false),
                Age = (float) (_timing.CurTime - encounter.Started).TotalSeconds,
                Threat = encounter.Threat,
                ExpiresIn = encounter.Expires is { } expires ? MathF.Max(0f, (float) (expires - _timing.CurTime).TotalSeconds) : -1f,
            };
            foreach (var (key, ship) in encounter.Ships)
            {
                var exists = !TerminatingOrDeleted(ship.Grid);
                var net = exists ? GetNetEntity(ship.Grid) : NetEntity.Invalid;
                var crew = crews.FirstOrDefault(row => row.Grid == net && row.Group == ship.Group);
                var activity = crew?.Activity ?? string.Empty;
                // An extorter's row leads with where it is in its round.
                if (encounter.Resolution == null && _encounters.ExtortStatus(ship) is { } extorting)
                {
                    activity = activity.Length > 0
                        ? Loc.GetString("wg-encounter-admin-state-detail", ("state", extorting), ("detail", activity))
                        : extorting;
                }

                entry.Ships.Add(new WGEncounterAdminShip
                {
                    Key = key,
                    Grid = net,
                    Name = exists ? MetaData(ship.Grid).EntityName : string.Empty,
                    Group = ship.Group,
                    Side = ship.Side,
                    Exists = exists,
                    Disabled = exists && _status.IsDisabled(ship.Grid),
                    Crew = crew?.Alive ?? 0,
                    Activity = activity,
                    Virtual = ship.Virtual,
                    Building = ship.Building,
                    Extorter = ship.Extort != null,
                    Hunter = ship.Hunt && ship.Extort == null,
                });
            }

            state.Encounters.Add(entry);
        }

        foreach (var prototype in _prototypes.EnumeratePrototypes<WGEncounterPrototype>().OrderBy(prototype => prototype.ID))
        {
            state.Prototypes.Add(new WGEncounterAdminPrototype
            {
                Id = prototype.ID,
                Scheduled = prototype.Start != WGEncounterStart.Manual,
                Weight = prototype.Weight,
                Running = running.Contains(prototype.ID),
            });
        }

        AddTargets(state, viewer);
        return state;
    }

    /// <summary>
    /// Every ship with a living player aboard that isn't an encounter's, with who is aboard, and every standing reality
    /// anchor station, nearest the viewer first.
    /// </summary>
    private void AddTargets(WGEncounterAdminState state, ICommonSession? viewer)
    {
        var from = viewer?.AttachedEntity is { } body ? _transform.GetMapCoordinates(body) : MapCoordinates.Nullspace;
        var aboard = new Dictionary<EntityUid, List<string>>();
        var players = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (players.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid is not { } grid || HasComp<WGEncounterGridComponent>(grid) || HasComp<Content.Shared.Ghost.GhostComponent>(uid)
                || _mobs.IsDead(uid) || grid == xform.MapUid)
                continue;

            if (!aboard.TryGetValue(grid, out var names))
                aboard[grid] = names = new List<string>();
            names.Add(Name(uid));
        }

        foreach (var (grid, names) in aboard)
        {
            var at = _transform.GetMapCoordinates(grid);
            state.Targets.Add(new WGEncounterAdminTarget
            {
                Grid = GetNetEntity(grid),
                Name = Name(grid),
                Players = string.Join(", ", names),
                Distance = at.MapId == from.MapId ? (at.Position - from.Position).Length() : -1f,
            });
        }

        // The Lattice's anchor stations that still stand, so a ship can be set on one.
        var anchors = EntityQueryEnumerator<Content.Shared._WG.Lattice.WGLatticeAnchorStationComponent, TransformComponent>();
        while (anchors.MoveNext(out var station, out var anchor, out var stationXform))
        {
            if (TerminatingOrDeleted(anchor.Core) || aboard.ContainsKey(station))
                continue;

            var at = _transform.GetMapCoordinates(station, stationXform);
            state.Targets.Add(new WGEncounterAdminTarget
            {
                Grid = GetNetEntity(station),
                Name = Name(station),
                Players = Loc.GetString("wg-encounter-admin-target-anchor"),
                Distance = at.MapId == from.MapId ? (at.Position - from.Position).Length() : -1f,
            });
        }

        state.Targets.Sort((a, b) => (a.Distance < 0f ? float.MaxValue : a.Distance).CompareTo(b.Distance < 0f ? float.MaxValue : b.Distance));
    }
}
