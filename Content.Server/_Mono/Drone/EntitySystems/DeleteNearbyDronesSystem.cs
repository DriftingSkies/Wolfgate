using Content.Server._Mono.Drone.Components;
using Content.Server.StationEvents.Events;
using Robust.Server.GameObjects;

namespace Content.Server._Mono.Drone.EntitySystems;

public sealed partial class DeleteNearbyDronesSystem : EntitySystem
{
    [Dependency] private LinkedLifecycleGridSystem _lifecycleGridSystem = default!;
    [Dependency] private TransformSystem _xform = default!;
    private float _wfTimer; // WOLFGATE(Performance): throttle for Update
    private readonly List<(EntityUid Uid, Robust.Shared.Map.MapId Map, System.Numerics.Vector2 Pos)> _wfDrones = new(); // WOLFGATE(Performance): drones read once per pass

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // WOLFGATE(Performance) START: throttled to 2 Hz, drones read once per pass, and only drones on the grid's own map count
        _wfTimer += frameTime;
        if (_wfTimer < 0.5f)
            return;
        _wfTimer = 0f;

        _wfDrones.Clear();
        var droneScan = EntityQueryEnumerator<HostileDroneGridComponent, TransformComponent>();
        while (droneScan.MoveNext(out var droneUid, out _, out var droneXform))
        {
            _wfDrones.Add((droneUid, droneXform.MapID, _xform.GetWorldPosition(droneXform)));
        }

        if (_wfDrones.Count == 0)
            return;

        var gridQuery = EntityQueryEnumerator<DeleteNearbyDronesComponent, TransformComponent>();
        while (gridQuery.MoveNext(out _, out var delComp, out var gridXform))
        {
            var gridPos = _xform.GetWorldPosition(gridXform);
            var limit = delComp.Distance * delComp.Distance;
            foreach (var (droneUid, droneMap, dronePos) in _wfDrones)
            {
                if (droneMap != gridXform.MapID || (gridPos - dronePos).LengthSquared() > limit)
                    continue;
                if (TerminatingOrDeleted(droneUid))
                    continue;
                _lifecycleGridSystem.UnparentPlayersFromGrid(droneUid, true, false); // this also handles deleting the drone lmao
            }
        }
        // WOLFGATE END

        // Original all-pairs scan, ignoring MapId:
        // var gridQuery = EntityQueryEnumerator<DeleteNearbyDronesComponent>();
        // while (gridQuery.MoveNext(out var gridUid, out var delComp)) { ...every HostileDroneGridComponent, Distance() > delComp.Distance... }
    }
}