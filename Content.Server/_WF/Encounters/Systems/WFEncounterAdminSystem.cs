using System.Linq;
using System.Numerics;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Encounters;
using Content.Shared.Administration;
using Content.Shared.Database;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.Encounters.Systems;

/// <summary>The server side of the admin encounter window: checks permission, acts, and replies with the state.</summary>
public sealed partial class WFEncounterAdminSystem : EntitySystem
{
    [Dependency] private IAdminManager _admins = default!;
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private WFEncounterSystem _encounters = default!;
    [Dependency] private WFEncounterSchedulerSystem _scheduler = default!;
    [Dependency] private WFCrewObjectiveSystem _objectives = default!;
    [Dependency] private WFCrewShipStatusSystem _status = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WFEncounterAdminRequest>(OnRequest);
    }

    private void OnRequest(WFEncounterAdminRequest request, EntitySessionEventArgs args)
    {
        if (!_admins.HasAdminFlag(args.SenderSession, AdminFlags.Admin))
            return;

        var state = BuildState();
        state.Message = Handle(request, args.SenderSession) ?? string.Empty;
        if (request.Action != WFEncounterAdminAction.Refresh)
        {
            var message = state.Message;
            state = BuildState();
            state.Message = message;
        }

        RaiseNetworkEvent(state, args.SenderSession);
    }

    /// <summary>Carries out one request. Returns why it did nothing, or null.</summary>
    public string? Handle(WFEncounterAdminRequest request, ICommonSession session)
    {
        switch (request.Action)
        {
            case WFEncounterAdminAction.Spawn:
                if (session.AttachedEntity is not { } actor || _transform.GetMapCoordinates(actor) is var here && here.MapId == MapId.Nullspace)
                    return Loc.GetString("cmd-wf_encounter-no-player");
                if (!_prototypes.TryIndex<WFEncounterPrototype>(request.Prototype ?? string.Empty, out var prototype))
                    return Loc.GetString("cmd-wf_encounter-unknown", ("prototype", request.Prototype ?? string.Empty));
                if (!float.IsFinite(request.Distance) || request.Distance is < 0 or > 20000)
                    return Loc.GetString("cmd-wf_encounter-bad-distance", ("arg", request.Distance));
                var origin = new MapCoordinates(here.Position + new Vector2(0f, request.Distance), here.MapId);
                if (!_scheduler.TryStartAt(prototype, origin, out var spawned, actor, request.Pinned))
                    return Loc.GetString("cmd-wf_encounter-spawn-failed", ("prototype", prototype.ID));
                Audit(session, $"started encounter {prototype.ID} as {ToPrettyString(spawned):entity}{(request.Pinned ? ", kept until ended" : string.Empty)}");
                return null;

            case WFEncounterAdminAction.Resolve:
            case WFEncounterAdminAction.End:
                if (!TryGetEntity(request.Target, out var target) || !HasComp<WFEncounterComponent>(target))
                    return Loc.GetString("wf-encounter-admin-gone");
                Audit(session, $"{(request.Action == WFEncounterAdminAction.End ? "ended" : "resolved")} encounter {ToPrettyString(target.Value):entity}");
                if (request.Action == WFEncounterAdminAction.End)
                    _encounters.End(target.Value);
                else
                    _encounters.Resolve(target.Value, WFEncounterResolution.Ended);
                return null;

            case WFEncounterAdminAction.Schedule:
                if (!_scheduler.TrySchedule(out var scheduled))
                    return Loc.GetString("cmd-wf_encounter-not-scheduled");
                Audit(session, $"had the scheduler start encounter {ToPrettyString(scheduled):entity}");
                return null;

            case WFEncounterAdminAction.Scheduler:
                if (!float.IsFinite(request.IntervalMin) || !float.IsFinite(request.IntervalMax)
                    || request.IntervalMin < 30 || request.IntervalMax < request.IntervalMin || request.IntervalMax > 86400
                    || request.MaxActive is < 0 or > 10)
                    return Loc.GetString("wf-encounter-admin-bad-settings");
                _config.SetCVar(EncountersCVars.Enabled, request.Enabled);
                _config.SetCVar(EncountersCVars.IntervalMin, request.IntervalMin);
                _config.SetCVar(EncountersCVars.IntervalMax, request.IntervalMax);
                _config.SetCVar(EncountersCVars.MaxActive, request.MaxActive);
                _scheduler.Paused = request.Paused;
                Audit(session, $"set the encounter scheduler: enabled {request.Enabled}, paused {request.Paused}, every {request.IntervalMin}-{request.IntervalMax} s, cap {request.MaxActive}");
                return null;

            case WFEncounterAdminAction.Preset:
                if (!EntityManager.System<WFEncounterVoteSystem>().SetPreset(request.Prototype ?? string.Empty))
                    return Loc.GetString("wf-encounter-admin-gone");
                Audit(session, $"set the encounter preset to {request.Prototype}");
                return null;

            case WFEncounterAdminAction.Reveal:
                if (!TryGetEntity(request.Target, out var hidden) || !HasComp<WFEncounterComponent>(hidden))
                    return Loc.GetString("wf-encounter-admin-gone");
                _encounters.Reveal(hidden.Value);
                Audit(session, $"revealed encounter {ToPrettyString(hidden.Value):entity}");
                return null;

            case WFEncounterAdminAction.StartRound:
                var count = _scheduler.StartRound();
                Audit(session, $"had the scheduler place {count} round-start encounters");
                return count > 0 ? null : Loc.GetString("cmd-wf_encounter-not-scheduled");

            case WFEncounterAdminAction.Teleport:
                if (session.AttachedEntity is not { } body)
                    return Loc.GetString("cmd-wf_encounter-no-player");
                if (!TryGetEntity(request.Target, out var destination) || TerminatingOrDeleted(destination))
                    return Loc.GetString("wf-encounter-admin-gone");
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

    /// <summary>The scheduler settings, every prototype and every encounter with its ships.</summary>
    public WFEncounterAdminState BuildState()
    {
        var state = new WFEncounterAdminState
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
        foreach (var preset in _prototypes.EnumeratePrototypes<WFEncounterPresetPrototype>().OrderBy(preset => preset.Budget))
        {
            state.Presets.Add(new WFEncounterAdminPreset { Id = preset.ID, Name = Loc.GetString(preset.Name) });
        }

        var crews = _objectives.Snapshot();
        var running = new HashSet<string>();
        var query = EntityQueryEnumerator<WFEncounterComponent>();
        while (query.MoveNext(out var uid, out var encounter))
        {
            if (encounter.Resolution == null)
                running.Add(encounter.Prototype.Id);

            var entry = new WFEncounterAdminEntry
            {
                Uid = GetNetEntity(uid),
                Prototype = encounter.Prototype.Id,
                Name = encounter.Name,
                Resolved = encounter.Resolution != null,
                Hidden = encounter.Hidden,
                Pinned = encounter.Pinned,
                State = Loc.GetString(encounter.Resolution is { } resolution
                    ? $"wf-encounter-resolution-{resolution.ToString().ToLowerInvariant()}"
                    : "cmd-wf_encounter-state-active"),
                Age = (float) (_timing.CurTime - encounter.Started).TotalSeconds,
                ExpiresIn = encounter.Expires is { } expires ? MathF.Max(0f, (float) (expires - _timing.CurTime).TotalSeconds) : -1f,
            };
            foreach (var (key, ship) in encounter.Ships)
            {
                var exists = !TerminatingOrDeleted(ship.Grid);
                var net = exists ? GetNetEntity(ship.Grid) : NetEntity.Invalid;
                var crew = crews.FirstOrDefault(row => row.Grid == net && row.Group == ship.Group);
                entry.Ships.Add(new WFEncounterAdminShip
                {
                    Key = key,
                    Grid = net,
                    Name = exists ? MetaData(ship.Grid).EntityName : string.Empty,
                    Group = ship.Group,
                    Side = ship.Side,
                    Exists = exists,
                    Disabled = exists && _status.IsDisabled(ship.Grid),
                    Crew = crew?.Alive ?? 0,
                    Activity = crew?.Activity ?? string.Empty,
                });
            }

            state.Encounters.Add(entry);
        }

        foreach (var prototype in _prototypes.EnumeratePrototypes<WFEncounterPrototype>().OrderBy(prototype => prototype.ID))
        {
            state.Prototypes.Add(new WFEncounterAdminPrototype
            {
                Id = prototype.ID,
                Scheduled = prototype.Start != WFEncounterStart.Manual,
                Weight = prototype.Weight,
                Running = running.Contains(prototype.ID),
            });
        }

        return state;
    }
}
