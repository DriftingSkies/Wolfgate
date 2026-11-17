using System.Numerics;
using Content.Server.Administration.Managers;
using Content.Shared._WG.NpcVehicleCrew;
using Content.Shared.Administration;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.Server._WG.NpcVehicleCrew;

/// <summary>The admin panel's endpoint: spawn crews beside the admin, list them, give them orders, go to them.</summary>
public sealed partial class WGVehicleCrewSystem
{
    [Dependency] private IAdminManager _admins = default!;

    /// <summary>The most crews one press spawns.</summary>
    public const int MaxPanelSpawn = 8;

    /// <summary>Metres between vehicles spawned together, side by side.</summary>
    private const float SpawnSpacing = 7f;

    private void InitializePanel()
    {
        SubscribeNetworkEvent<WGVehicleCrewPanelRequest>(OnPanelRequest);
    }

    private void OnPanelRequest(WGVehicleCrewPanelRequest request, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;
        if (!_admins.HasAdminFlag(session, AdminFlags.Admin))
            return;

        string? message = null;
        var error = false;
        switch (request.Action)
        {
            case WGVehicleCrewPanelAction.Spawn:
                error = !PanelSpawn(session, request, out message);
                break;
            case WGVehicleCrewPanelAction.Order:
                error = !PanelOrder(session, request, out message);
                break;
            case WGVehicleCrewPanelAction.HomeHere:
                error = !PanelHome(session, request, out message);
                break;
            case WGVehicleCrewPanelAction.GoTo:
                error = !PanelGoTo(session, request, out message);
                break;
            case WGVehicleCrewPanelAction.Delete:
                if (PanelCrew(request) is { } doomed)
                {
                    Clear(doomed.Comp.Group);
                    message = Loc.GetString("wg-vehiclecrew-panel-deleted", ("vehicle", Name(doomed)));
                }
                break;
            case WGVehicleCrewPanelAction.ClearAll:
                message = Loc.GetString("wg-vehiclecrew-cleared", ("count", Clear()));
                break;
        }

        RaiseNetworkEvent(new WGVehicleCrewPanelState { Crews = PanelEntries(), Message = message, Error = error }, session);
    }

    private bool PanelSpawn(ICommonSession session, WGVehicleCrewPanelRequest request, out string message)
    {
        if (session.AttachedEntity is not { } admin)
        {
            message = Loc.GetString("wg-vehiclecrew-no-entity");
            return false;
        }

        if (request.Crew is not { } crewId || !_prototypes.HasIndex<WGVehicleCrewPrototype>(crewId))
        {
            message = Loc.GetString("wg-vehiclecrew-unknown-crew", ("crew", request.Crew ?? string.Empty));
            return false;
        }

        var count = Math.Clamp(request.Count, 1, MaxPanelSpawn);
        var distance = Math.Clamp(request.Distance, 0f, 64f);
        var origin = Transform(admin).Coordinates;
        var spawned = 0;
        for (var i = 0; i < count; i++)
        {
            var coords = origin.Offset(new Vector2(distance, i * SpawnSpacing));
            if (!TrySpawn(crewId, coords, out var vehicle, request.Behaviour))
                continue;
            spawned++;
            if (request.Behaviour == WGVehicleBehaviour.Escort)
                SetLead(vehicle, admin);
        }

        message = Loc.GetString("wg-vehiclecrew-panel-spawned", ("count", spawned), ("crew", PanelCrewName(crewId)));
        return spawned > 0;
    }

    private bool PanelOrder(ICommonSession session, WGVehicleCrewPanelRequest request, out string message)
    {
        message = string.Empty;
        if (PanelCrew(request) is not { } vehicle || request.Behaviour is not { } behaviour)
            return false;

        if (vehicle.Comp.Bailed)
        {
            message = Loc.GetString("wg-vehiclecrew-panel-bailed", ("vehicle", Name(vehicle)));
            return false;
        }

        if (behaviour == WGVehicleBehaviour.Escort)
        {
            if (session.AttachedEntity is not { } admin)
            {
                message = Loc.GetString("wg-vehiclecrew-no-entity");
                return false;
            }
            SetLead(vehicle.AsNullable(), admin);
        }
        else
        {
            SetBehaviour(vehicle.AsNullable(), behaviour);
        }

        message = Loc.GetString("wg-vehiclecrew-panel-ordered", ("vehicle", Name(vehicle)),
            ("behaviour", Loc.GetString($"wg-vehiclecrew-behaviour-{behaviour.ToString().ToLowerInvariant()}")));
        return true;
    }

    private bool PanelHome(ICommonSession session, WGVehicleCrewPanelRequest request, out string message)
    {
        message = string.Empty;
        if (PanelCrew(request) is not { } vehicle)
            return false;
        if (session.AttachedEntity is not { } admin)
        {
            message = Loc.GetString("wg-vehiclecrew-no-entity");
            return false;
        }

        var here = _transform.GetMapCoordinates(admin);
        if (here.MapId != _transform.GetMapCoordinates(vehicle).MapId)
        {
            message = Loc.GetString("wg-vehiclecrew-panel-other-map", ("vehicle", Name(vehicle)));
            return false;
        }

        SetHome(vehicle.AsNullable(), here);
        message = Loc.GetString("wg-vehiclecrew-panel-homed", ("vehicle", Name(vehicle)));
        return true;
    }

    private bool PanelGoTo(ICommonSession session, WGVehicleCrewPanelRequest request, out string message)
    {
        message = string.Empty;
        if (PanelCrew(request) is not { } vehicle || session.AttachedEntity is not { } admin)
            return false;

        // Beside the hull rather than on it, so a ghost doesn't land inside the vehicle's own fixtures.
        _transform.SetCoordinates(admin, Transform(vehicle).Coordinates.Offset(new Vector2(0f, -3f)));
        _transform.AttachToGridOrMap(admin);
        return true;
    }

    private Entity<WGVehicleCrewComponent>? PanelCrew(WGVehicleCrewPanelRequest request)
    {
        return TryGetEntity(request.Vehicle, out var uid) && TryComp(uid, out WGVehicleCrewComponent? crew)
            ? (uid.Value, crew)
            : null;
    }

    private List<WGVehicleCrewPanelEntry> PanelEntries()
    {
        var entries = new List<WGVehicleCrewPanelEntry>();
        foreach (var (uid, crew) in GetCrews())
        {
            // A crew just deleted is only queued to go; the panel shouldn't list it.
            if (EntityManager.IsQueuedForDeletion(uid) || TerminatingOrDeleted(uid))
                continue;

            CollectMembers(crew);
            var total = _members.Count;
            var map = _transform.GetMapCoordinates(uid);
            var mapName = Transform(uid).MapUid is { } mapUid ? Name(mapUid) : "?";
            entries.Add(new WGVehicleCrewPanelEntry
            {
                Vehicle = GetNetEntity(uid),
                Name = Name(uid),
                Crew = PanelCrewName(crew.Prototype),
                Behaviour = crew.Behaviour,
                Bailed = crew.Bailed,
                Alive = CountAlive(crew),
                Total = total,
                Integrity = IntegrityFraction(uid),
                Location = $"{mapName} ({map.Position.X:0}, {map.Position.Y:0})",
            });
        }
        return entries;
    }

    private string PanelCrewName(string crewId)
    {
        return _prototypes.TryIndex<WGVehicleCrewPrototype>(crewId, out var proto) && proto.Name is { } name
            ? Loc.GetString(name)
            : crewId;
    }
}
